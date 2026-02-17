namespace Configuration;

public record AppConfig
{
    public int StartInvoiceNumber { get; init; } = 1;
    public string DbPath { get; init; } = "/data/app.db";
    public string BlobStoragePath { get; init; } = "/data/blobs";
}