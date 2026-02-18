namespace Configuration;

/// <summary>
/// BillingAddress-compatible structure for JSON config binding.
/// Can be mapped to Invoices.BillingAddress when needed.
/// </summary>
public record SellerAddress
{
    public string Name { get; init; } = string.Empty;
    public string RepresentativeName { get; init; } = string.Empty;
    public string CompanyIdentifier { get; init; } = string.Empty;
    public string? VatIdentifier { get; init; }
    public string Address { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
}

/// <summary>
/// Bank transfer info for the seller (IBAN, BankName, Bic).
/// Can be mapped to Invoices.BankTransferInfo when needed.
/// </summary>
public record SellerBankTransferInfo
{
    public string Iban { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string Bic { get; init; } = string.Empty;
}

public record AppConfig
{
    public int StartInvoiceNumber { get; init; } = 1;
    public SellerAddress? SellerAddress { get; init; }
    public SellerBankTransferInfo? SellerBankTransferInfo { get; init; }
}

public record DesktopConfig
{
    public string DatabasePath { get; set; } = string.Empty;
    public string BlobStoragePath { get; set; } = string.Empty;
    public string LogDirectory { get; set; } = string.Empty;
}

public record Config
{
    public AppConfig App { get; set; } = new();
    public DesktopConfig Desktop { get; set; } = new();
}