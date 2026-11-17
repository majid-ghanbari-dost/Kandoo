using System.Collections.Concurrent;

namespace Kandoo.Core;

/// <summary>Demo SMS provider for Order 15 local-demo week (D-L).</summary>
/// <remarks>
/// Real SMS is disabled in the demo. Every demo SMS is written to a local outbox
/// file plus an in-memory log so the admin/settings page can show it. No gateway
/// credentials and no external network calls.
/// </remarks>
public sealed class DemoSmsProvider
{
    private readonly object _gate = new();
    private readonly string? _outboxDirectory;
    private readonly ConcurrentQueue<DemoSmsMessage> _log = new();

    public DemoSmsProvider(string? outboxDirectory = null)
    {
        _outboxDirectory = outboxDirectory;
        if (!string.IsNullOrWhiteSpace(_outboxDirectory))
        {
            try
            {
                Directory.CreateDirectory(_outboxDirectory);
            }
            catch (Exception)
            {
                // Outbox directory creation must not break demo startup; messages
                // are still kept in the in-memory log.
            }
        }
    }

    public int CapturedCount
    {
        get
        {
            lock (_gate)
            {
                return _log.Count;
            }
        }
    }

    public IReadOnlyList<DemoSmsMessage> Log
    {
        get
        {
            lock (_gate)
            {
                return _log.ToArray();
            }
        }
    }

    /// <summary>Sends a demo SMS: appends to the outbox file and the in-memory log.</summary>
    public DemoSmsMessage Send(string recipientPhone, string body, string? storeName = null, long? displayNumber = null)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone))
            throw new ArgumentException("A recipient phone is required.", nameof(recipientPhone));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("An SMS body is required.", nameof(body));

        var message = new DemoSmsMessage(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            recipientPhone.Trim(),
            body,
            storeName,
            displayNumber);

        lock (_gate)
        {
            _log.Enqueue(message);
        }

        AppendToOutboxFile(message);

        return message;
    }

    private void AppendToOutboxFile(DemoSmsMessage message)
    {
        if (_outboxDirectory is null)
            return;

        try
        {
            var path = Path.Combine(_outboxDirectory, "demo-sms.log");
            var line = $"{message.SentAt:O}\t{message.RecipientPhone}\t{message.Body}";
            if (message.DisplayNumber is long n)
                line += $"\tdisplay_number={n}";
            if (!string.IsNullOrWhiteSpace(message.StoreName))
                line += $"\tstore={message.StoreName}";
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // Outbox writes are best-effort; the in-memory log remains authoritative.
        }
    }
}

public sealed record DemoSmsMessage(
    Guid MessageId,
    DateTimeOffset SentAt,
    string RecipientPhone,
    string Body,
    string? StoreName,
    long? DisplayNumber);
