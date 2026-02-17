namespace Configuration;

public record AppConfig
{
    public int StartInvoiceNumber { get; init; } = 1;
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
    public DesktopConfig? Desktop { get; set; }
}