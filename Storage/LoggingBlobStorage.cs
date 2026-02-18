using Utilities;

namespace Storage;

public class LoggingBlobStorage : IBlobStorage
{
    private readonly IBlobStorage _inner;
    private readonly ILogger _logger;

    public LoggingBlobStorage(IBlobStorage inner, ILogger logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = new SafeLogger(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public async Task<string> UploadAsync(string bucket, string objectKey, Stream content, string contentType)
    {
        _logger.LogInfo($"BlobStorage.UploadAsync bucket={bucket} objectKey={objectKey}");
        try
        {
            var result = await _inner.UploadAsync(bucket, objectKey, content, contentType);
            _logger.LogInfo($"BlobStorage.UploadAsync completed bucket={bucket} objectKey={objectKey}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.UploadAsync bucket={bucket} objectKey={objectKey} failed", ex);
            throw;
        }
    }

    public async Task<string> CreateUploadUrlAsync(string bucket, string objectKey, TimeSpan ttl, string contentType)
    {
        _logger.LogInfo($"BlobStorage.CreateUploadUrlAsync bucket={bucket} objectKey={objectKey}");
        try
        {
            return await _inner.CreateUploadUrlAsync(bucket, objectKey, ttl, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.CreateUploadUrlAsync bucket={bucket} objectKey={objectKey} failed", ex);
            throw;
        }
    }

    public async Task<Stream> OpenReadAsync(string bucket, string objectKey)
    {
        _logger.LogInfo($"BlobStorage.OpenReadAsync bucket={bucket} objectKey={objectKey}");
        try
        {
            return await _inner.OpenReadAsync(bucket, objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.OpenReadAsync bucket={bucket} objectKey={objectKey} failed", ex);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string bucket, string objectKey)
    {
        _logger.LogInfo($"BlobStorage.ExistsAsync bucket={bucket} objectKey={objectKey}");
        try
        {
            return await _inner.ExistsAsync(bucket, objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.ExistsAsync bucket={bucket} objectKey={objectKey} failed", ex);
            throw;
        }
    }

    public async Task DeleteAsync(string bucket, string objectKey)
    {
        _logger.LogInfo($"BlobStorage.DeleteAsync bucket={bucket} objectKey={objectKey}");
        try
        {
            await _inner.DeleteAsync(bucket, objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.DeleteAsync bucket={bucket} objectKey={objectKey} failed", ex);
            throw;
        }
    }

    public async Task RenameAsync(string bucket, string sourceObjectKey, string destinationObjectKey)
    {
        _logger.LogInfo($"BlobStorage.RenameAsync bucket={bucket} source={sourceObjectKey} dest={destinationObjectKey}");
        try
        {
            await _inner.RenameAsync(bucket, sourceObjectKey, destinationObjectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.RenameAsync bucket={bucket} failed", ex);
            throw;
        }
    }

    public string UriFor(string bucket, string objectKey)
    {
        _logger.LogInfo($"BlobStorage.UriFor bucket={bucket} objectKey={objectKey}");
        try
        {
            return _inner.UriFor(bucket, objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.UriFor bucket={bucket} objectKey={objectKey} failed", ex);
            throw;
        }
    }

    public async Task<IBlobStorage.BlobList> ListAsync(string bucket, string prefix, int limit = 1000, string? cursor = null)
    {
        _logger.LogInfo($"BlobStorage.ListAsync bucket={bucket} prefix={prefix}");
        try
        {
            return await _inner.ListAsync(bucket, prefix, limit, cursor);
        }
        catch (Exception ex)
        {
            _logger.LogError($"BlobStorage.ListAsync bucket={bucket} failed", ex);
            throw;
        }
    }
}
