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
    /// <summary>OpenAI API key for legacy PDF parsing. Set via App__OpenAiKey or OPENAI_API_KEY env var. Never commit real keys.</summary>
    public string? OpenAiKey { get; init; }
    /// <summary>When set, overrides the environment-based check for opening the browser on web app start. Null = use ASPNETCORE_ENVIRONMENT (Development/Mvp = open).</summary>
    public bool? ShouldOpenBrowserOnStart { get; init; }
    public SellerAddress? SellerAddress { get; init; }
    public SellerBankTransferInfo? SellerBankTransferInfo { get; init; }
}

public enum HostingMode
{
    Desktop,
    LocalDocker,
    HetznerDocker
}

public record RuntimeConfig
{
    public HostingMode HostingMode { get; set; } = HostingMode.Desktop;
    public int? Port { get; set; }
    public string? BindAddress { get; set; }
}

public record SingleBoxConfig
{
    public string DatabasePath { get; set; } = string.Empty;
    public string BlobStoragePath { get; set; } = string.Empty;
    public string LogDirectory { get; set; } = string.Empty;
}

public record Config
{
    /// <summary>Builds the environment variable key for a nested config property (e.g. App__OpenAiKey).</summary>
    public static string ToEnvKey(string sectionName, string propertyName) => $"{sectionName}__{propertyName}";

    public AppConfig App { get; set; } = new();
    public RuntimeConfig Runtime { get; set; } = new();
    public SingleBoxConfig SingleBox { get; set; } = new();
}