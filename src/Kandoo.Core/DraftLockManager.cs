namespace Kandoo.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public enum DraftLockState
{
    Active,
    Released
}

public enum LockReleaseReason
{
    Issue,
    Cancel,
    Disconnect,
    Timeout
}

public enum DraftChangeKind
{
    Item,
    Quantity,
    Customer
}

public sealed class DraftLockException : InvalidOperationException
{
    public DraftLockException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

public sealed class DraftLock
{
    internal DraftLock(Guid lockId, Guid storeId, string customerPhone, Guid invoiceId, Guid deviceId, DateTimeOffset acquiredAt, int timeoutSeconds)
    {
        LockId = lockId;
        StoreId = storeId;
        CustomerPhone = customerPhone;
        InvoiceId = invoiceId;
        DeviceId = deviceId;
        AcquiredAt = acquiredAt;
        LastActivityAt = acquiredAt;
        TimeoutSeconds = timeoutSeconds;
        State = DraftLockState.Active;
    }

    public Guid LockId { get; }
    public Guid StoreId { get; }
    public string CustomerPhone { get; }
    public Guid InvoiceId { get; }
    public Guid DeviceId { get; }
    public DateTimeOffset AcquiredAt { get; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public int TimeoutSeconds { get; }
    public DateTimeOffset ExpiresAt => LastActivityAt.AddSeconds(TimeoutSeconds);
    public DraftLockState State { get; private set; }
    public LockReleaseReason? ReleaseReason { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    internal void AcceptChange(DateTimeOffset acceptedAt)
    {
        LastActivityAt = acceptedAt;
    }

    internal void Release(DateTimeOffset releasedAt, LockReleaseReason reason)
    {
        if (State == DraftLockState.Released)
        {
            return;
        }

        State = DraftLockState.Released;
        ReleaseReason = reason;
        ReleasedAt = releasedAt;
    }
}

/// <summary>Hub-time, per-store/per-phone draft leases with accepted-change-only renewal.</summary>
public sealed class DraftLockManager
{
    private readonly object _gate = new();
    private readonly IClock _clock;
    private readonly Action<Guid>? _timeoutRetirement;
    private readonly Dictionary<(Guid StoreId, string Phone), DraftLock> _locks = new();

    public DraftLockManager(IClock clock, Action<Guid>? timeoutRetirement = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeoutRetirement = timeoutRetirement;
    }

    public DraftLock Acquire(Guid storeId, string phone, Guid invoiceId, Guid deviceId, Guid lockId, int timeoutSeconds = 300)
    {
        if (storeId == Guid.Empty || invoiceId == Guid.Empty || deviceId == Guid.Empty || lockId == Guid.Empty || timeoutSeconds <= 0)
        {
            throw new ArgumentException("A lock requires store, invoice, device, lock ID, and a positive timeout.");
        }

        var normalizedPhone = PhoneNormalizer.Normalize(phone);
        lock (_gate)
        {
            var key = (storeId, normalizedPhone);
            if (_locks.TryGetValue(key, out var existing))
            {
                ExpireIfNeeded(existing);
                if (existing.State == DraftLockState.Active)
                {
                    var code = existing.DeviceId == deviceId ? "DRAFT_ALREADY_ACTIVE" : "DRAFT_LOCK_CONFLICT";
                    throw new DraftLockException(code, "An active draft lock already exists for this store and phone.");
                }
            }

            var created = new DraftLock(lockId, storeId, normalizedPhone, invoiceId, deviceId, _clock.UtcNow, timeoutSeconds);
            _locks[key] = created;
            return created;
        }
    }

    public bool AcceptChange(Guid storeId, string phone, Guid deviceId, DraftChangeKind changeKind)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(phone);
        lock (_gate)
        {
            if (!_locks.TryGetValue((storeId, normalizedPhone), out var current))
            {
                return false;
            }

            ExpireIfNeeded(current);
            if (current.State != DraftLockState.Active || current.DeviceId != deviceId)
            {
                return false;
            }

            // This method is called only after the Hub has accepted a new item,
            // quantity, or customer change. Passive/replayed activity has no method
            // that can update LastActivityAt.
            current.AcceptChange(_clock.UtcNow);
            return true;
        }
    }

    public bool PassiveHeartbeat(Guid storeId, string phone, Guid deviceId)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(phone);
        lock (_gate)
        {
            if (!_locks.TryGetValue((storeId, normalizedPhone), out var current))
            {
                return false;
            }

            ExpireIfNeeded(current);
            return current.State == DraftLockState.Active && current.DeviceId == deviceId;
        }
    }

    public bool Evaluate(Guid storeId, string phone)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(phone);
        lock (_gate)
        {
            if (!_locks.TryGetValue((storeId, normalizedPhone), out var current))
            {
                return false;
            }

            ExpireIfNeeded(current);
            return current.State == DraftLockState.Active;
        }
    }

    public bool Release(Guid storeId, string phone, Guid deviceId, LockReleaseReason reason)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(phone);
        lock (_gate)
        {
            if (!_locks.TryGetValue((storeId, normalizedPhone), out var current))
            {
                return false;
            }

            ExpireIfNeeded(current);
            if (current.State != DraftLockState.Active || current.DeviceId != deviceId)
            {
                return false;
            }

            current.Release(_clock.UtcNow, reason);
            return true;
        }
    }

    public int Disconnect(Guid deviceId)
    {
        lock (_gate)
        {
            var released = 0;
            foreach (var current in _locks.Values.Where(l => l.DeviceId == deviceId && l.State == DraftLockState.Active))
            {
                current.Release(_clock.UtcNow, LockReleaseReason.Disconnect);
                released++;
            }

            return released;
        }
    }

    public DraftLock? Get(Guid storeId, string phone)
    {
        var normalizedPhone = PhoneNormalizer.Normalize(phone);
        lock (_gate)
        {
            return _locks.GetValueOrDefault((storeId, normalizedPhone));
        }
    }

    private void ExpireIfNeeded(DraftLock current)
    {
        if (current.State == DraftLockState.Active && _clock.UtcNow >= current.ExpiresAt)
        {
            current.Release(_clock.UtcNow, LockReleaseReason.Timeout);
            _timeoutRetirement?.Invoke(current.InvoiceId);
        }
    }
}
