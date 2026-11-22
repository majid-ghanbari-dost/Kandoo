namespace Kandoo.Cloud;

public interface IPublicInvoiceStore
{
    PublicInvoice? GetPublicInvoice(string token);
}
