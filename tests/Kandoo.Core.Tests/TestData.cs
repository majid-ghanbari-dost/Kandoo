using System.Text.Json;
using Kandoo.Core;

namespace Kandoo.Core.Tests;

internal static class TestData
{
    internal static readonly Guid S1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid S2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    internal static readonly Guid D1 = Guid.Parse("33333333-3333-4333-8333-333333333333");
    internal static readonly Guid D2 = Guid.Parse("44444444-4444-4444-8444-444444444444");
    internal static readonly Guid D3 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    internal static readonly DateTimeOffset T0 = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    internal static JsonElement Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal static InvoiceItem Item(Guid storeId, int lineNumber = 1, long quantityMillis = 1000)
    {
        return new(Guid.NewGuid(), storeId, lineNumber, "synthetic item", quantityMillis, 1000, 1000);
    }

    internal static Invoice IssuableDraft(InvoiceService service, Guid storeId, string operationId = "op-create", Guid? deviceId = null, string? externalNumber = null)
    {
        var invoice = service.CreateDraft(Guid.NewGuid(), storeId, operationId, deviceId ?? D1, T0, externalNumber);
        invoice.AddItem(Item(storeId));
        return invoice;
    }
}

internal sealed class ManualClock : IClock
{
    internal ManualClock(DateTimeOffset initial) => UtcNow = initial;

    public DateTimeOffset UtcNow { get; private set; }

    internal void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}
