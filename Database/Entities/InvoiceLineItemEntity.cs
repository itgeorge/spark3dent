namespace Database.Entities;

public class InvoiceLineItemEntity
{
    public long Id { get; set; }
    public long InvoiceEntityId { get; set; }
    public InvoiceEntity Invoice { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "Eur";
}
