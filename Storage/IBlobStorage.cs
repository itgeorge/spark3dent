namespace Storage;

public interface IBlobStorage
{
    public record BlobList(List<string> ObjectKeys, string? NextCursor);

    Task<string> UploadAsync(string bucket, string objectKey, Stream content, string contentType);
    Task<string> CreateUploadUrlAsync(string bucket, string objectKey, TimeSpan ttl, string contentType);
    Task<Stream> OpenReadAsync(string bucket, string objectKey);
    Task<bool> ExistsAsync(string bucket, string objectKey);
    Task DeleteAsync(string bucket, string objectKey);
    Task RenameAsync(string bucket, string sourceObjectKey, string destinationObjectKey);
    string UriFor(string bucket, string objectKey);
    Task<BlobList> ListAsync(string bucket, string prefix, int limit = 1000, string? cursor = null);
}