namespace Kandoo.HolooConnector;

/// <summary>
/// نسخه سما Holoo را برای همزمانی افزایشی ردیابی می‌کند.
/// فقط خواندنی است؛ به Holoo نوشته نمی‌شود.
/// </summary>
public sealed class HolooIncrementalSync
{
    private HolooIncrementalSync(HolooSchemaVersion version, long lastRowVersion)
    {
        Version = version;
        LastRowVersion = lastRowVersion;
    }

    public HolooSchemaVersion Version { get; }
    public long LastRowVersion { get; }

    /// <summary>
    /// یک نمونه جدید می‌سازد اگر نسخه سما تغییر کرده باشد.
    /// اگر نسخه سما قبلیnull باشد یا نسخه جدید متفاوت باشد، یک نمونه جدید برمی‌گرداند.
    /// </summary>
    public static HolooIncrementalSync? CreateIfChanged(
        HolooSchemaVersion? previous,
        HolooSchemaVersion current,
        long lastRowVersion)
    {
        if (previous is null || !previous.Value.Equals(current.Value))
            return new HolooIncrementalSync(current, lastRowVersion);

        return null;
    }

    /// <summary>
    /// تشخیص می‌کند آیا ردیابی نسخه سما برای همزمانی افزایشی لازم است یا خیر.
    /// </summary>
    public bool NeedsSync => LastRowVersion > 0;

    public override string ToString() => $"sync v={Version.Value} lastRow={LastRowVersion}";
}