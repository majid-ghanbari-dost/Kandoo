using System.Text;
using Kandoo.Core;
using Xunit;

namespace Kandoo.Core.Tests;

public sealed class ProductQrCodecTests
{
    private static readonly Guid Store = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void C13_QrPayloadMatchesCanonicalDocumentExamplesAndRoundTrips()
    {
        const string plain = "{\"product_tag_number\":123,\"schema_version\":\"0\",\"store_id\":\"11111111-1111-4111-8111-111111111111\"}";
        const string checkedPayload = "{\"check_digit\":\"0\",\"product_tag_number\":123,\"schema_version\":\"0\",\"store_id\":\"11111111-1111-4111-8111-111111111111\"}";

        Assert.Equal("0", ProductQrCodec.LuhnCheckDigit(123));
        Assert.Equal(plain, ProductQrCodec.Encode(Store, 123));
        Assert.Equal(checkedPayload, ProductQrCodec.Encode(Store, 123, true));
        Assert.Equal("123", ProductQrCodec.FormatTag(123));
        Assert.Equal("123-0", ProductQrCodec.FormatTag(123, true));
        Assert.Equal(ProductQrCodec.EncodeUtf8(Store, 123, true), ProductQrCodec.EncodeUtf8(Store, 123, true));
        Assert.False(ProductQrCodec.Encode(Store, 123).EndsWith('\n'));

        var decodedPlain = ProductQrCodec.Decode(plain, Store);
        var decodedChecked = ProductQrCodec.Decode(checkedPayload, Store);
        Assert.Equal(new ProductQrLabel(Store, 123, null), decodedPlain);
        Assert.Equal(new ProductQrLabel(Store, 123, "0"), decodedChecked);
        Assert.Equal(plain, ProductQrCodec.Encode(decodedPlain.StoreId, decodedPlain.ProductTagNumber));
        Assert.Equal(checkedPayload, ProductQrCodec.Encode(decodedChecked.StoreId, decodedChecked.ProductTagNumber, true));
        Assert.Equal(plain, Encoding.UTF8.GetString(ProductQrCodec.EncodeUtf8(Store, 123)));
    }

    [Fact]
    public void C13_QrRejectsWrongStoreAndCheckDigit()
    {
        var payload = ProductQrCodec.Encode(Store, 123, true);
        var wrongStore = Assert.Throws<InvalidOperationException>(() => ProductQrCodec.Decode(payload, Guid.NewGuid()));
        Assert.Equal("PRODUCT_TAG_STORE_MISMATCH", wrongStore.Message);
        var badCheck = Assert.Throws<InvalidOperationException>(() => ProductQrCodec.Decode(payload.Replace("\"check_digit\":\"0\"", "\"check_digit\":\"1\""), Store));
        Assert.Equal("CHECK_DIGIT_INVALID", badCheck.Message);
    }

    [Fact]
    public void C13_QrRejectsDuplicateExtraAndNonIntegerFields()
    {
        var duplicate = "{\"product_tag_number\":123,\"product_tag_number\":124,\"schema_version\":\"0\",\"store_id\":\"11111111-1111-4111-8111-111111111111\"}";
        var extra = "{\"price\":1,\"product_tag_number\":123,\"schema_version\":\"0\",\"store_id\":\"11111111-1111-4111-8111-111111111111\"}";
        var fractional = "{\"product_tag_number\":123.5,\"schema_version\":\"0\",\"store_id\":\"11111111-1111-4111-8111-111111111111\"}";
        foreach (var payload in new[] { duplicate, extra, fractional })
        {
            var error = Assert.Throws<InvalidOperationException>(() => ProductQrCodec.Decode(payload, Store));
            Assert.Equal("PRODUCT_TAG_PAYLOAD_INVALID", error.Message);
        }
    }
}
