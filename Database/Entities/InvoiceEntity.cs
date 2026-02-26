namespace Database.Entities;

public class InvoiceEntity
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public long NumberNumeric { get; set; }
    public DateTime Date { get; set; }

    // Seller (BillingAddress)
    public string SellerName { get; set; } = string.Empty;
    public string SellerRepresentativeName { get; set; } = string.Empty;
    public string SellerCompanyIdentifier { get; set; } = string.Empty;
    public string? SellerVatIdentifier { get; set; }
    public string SellerAddress { get; set; } = string.Empty;
    public string SellerCity { get; set; } = string.Empty;
    public string SellerPostalCode { get; set; } = string.Empty;
    public string SellerCountry { get; set; } = string.Empty;

    // Buyer (BillingAddress)
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerRepresentativeName { get; set; } = string.Empty;
    public string BuyerCompanyIdentifier { get; set; } = string.Empty;
    public string? BuyerVatIdentifier { get; set; }
    public string BuyerAddress { get; set; } = string.Empty;
    public string BuyerCity { get; set; } = string.Empty;
    public string BuyerPostalCode { get; set; } = string.Empty;
    public string BuyerCountry { get; set; } = string.Empty;

    // Bank transfer
    public string BankIban { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankBic { get; set; } = string.Empty;

    public bool IsCorrected { get; set; }
    public bool IsLegacy { get; set; }

    public ICollection<InvoiceLineItemEntity> LineItems { get; set; } = new List<InvoiceLineItemEntity>();
}
