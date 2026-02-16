using System.Collections.Concurrent;

namespace Storage;

public class LocalFileSystemBlobStorage : IBlobStorage
{
    private readonly Dictionary<string, string> _contentTypeToExtension;
    private readonly ConcurrentDictionary<string, string> _bucketToDirectoryMap = new();

    public LocalFileSystemBlobStorage(Dictionary<string, string> contentTypeToExtension)
    {
        _contentTypeToExtension = contentTypeToExtension ?? throw new ArgumentNullException(nameof(contentTypeToExtension));
    }

    public void DefineBucket(string bucketName, string directoryPath)
    {
        if (string.IsNullOrEmpty(bucketName))
            throw new ArgumentException("Bucket name cannot be null or empty", nameof(bucketName));

        if (string.IsNullOrEmpty(directoryPath))
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

        Directory.CreateDirectory(directoryPath);

        _bucketToDirectoryMap[bucketName] = directoryPath;
    }

    public Task<string> UploadAsync(string bucket, string objectKey, Stream content, string contentType)
    {
        ValidateBucketExists(bucket);
        ValidateObjectKey(objectKey);

        var directoryPath = _bucketToDirectoryMap[bucket];
        var filePath = GetFilePath(directoryPath, objectKey, contentType);

        // Ensure the directory exists (handle both cases where objectKey contains path separators or not)
        var fileDirectory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(fileDirectory))
            Directory.CreateDirectory(fileDirectory);

        // Reset stream position if possible
        if (content.CanSeek)
            content.Seek(0, SeekOrigin.Begin);

        // Create a temporary file first, then move it to the final location
        var tempFilePath = filePath + ".tmp";
        try
        {
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                content.CopyTo(fileStream);
            }

            // If we reach here, the copy was successful
            File.Move(tempFilePath, filePath, overwrite: true);
            return Task.FromResult(filePath);
        }
        catch
        {
            // If copying failed, clean up the temporary file
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
            throw;
        }
    }

    public Task<string> CreateUploadUrlAsync(string bucket, string objectKey, TimeSpan ttl, string contentType)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> OpenReadAsync(string bucket, string objectKey)
    {
        ValidateBucketExists(bucket);
        ValidateObjectKey(objectKey);

        var directoryPath = _bucketToDirectoryMap[bucket];
        var filePath = FindFilePath(directoryPath, objectKey);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Object not found: {objectKey}", filePath);

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream>(stream);
    }

    public Task<bool> ExistsAsync(string bucket, string objectKey)
    {
        ValidateObjectKey(objectKey);

        if (!_bucketToDirectoryMap.TryGetValue(bucket, out var directoryPath))
            return Task.FromResult(false);

        var filePath = FindFilePath(directoryPath, objectKey);

        return Task.FromResult(File.Exists(filePath));
    }

    public Task DeleteAsync(string bucket, string objectKey)
    {
        ValidateBucketExists(bucket);
        ValidateObjectKey(objectKey);

        var directoryPath = _bucketToDirectoryMap[bucket];
        var filePath = FindFilePath(directoryPath, objectKey);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Object not found: {objectKey}", filePath);

        File.Delete(filePath);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string bucket, string sourceObjectKey, string destinationObjectKey)
    {
        ValidateBucketExists(bucket);
        ValidateObjectKey(sourceObjectKey);
        ValidateObjectKey(destinationObjectKey);

        if (string.Equals(sourceObjectKey, destinationObjectKey, StringComparison.Ordinal))
            throw new ArgumentException("Source and destination object keys must be different", nameof(destinationObjectKey));

        var directoryPath = _bucketToDirectoryMap[bucket];
        var sourceFilePath = FindFilePath(directoryPath, sourceObjectKey);

        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Source object not found: {sourceObjectKey}", sourceFilePath);

        // Determine content type from the source file extension
        var sourceExtension = Path.GetExtension(sourceFilePath);
        var contentType = GetContentTypeFromExtension(sourceExtension);

        // Create destination file path
        var destinationFilePath = GetFilePath(directoryPath, destinationObjectKey, contentType);

        // Ensure the destination directory exists
        var destinationDirectory = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        // Move the file
        File.Move(sourceFilePath, destinationFilePath, overwrite: true);

        return Task.CompletedTask;
    }

    public string UriFor(string bucket, string objectKey)
    {
        ValidateBucketExists(bucket);
        ValidateObjectKey(objectKey);

        var directoryPath = _bucketToDirectoryMap[bucket];
        var filePath = FindFilePath(directoryPath, objectKey);

        return new Uri(filePath).AbsoluteUri;
    }

    public Task<IBlobStorage.BlobList> ListAsync(string bucket, string prefix, int limit = 1000, string? cursor = null)
    {
        ValidateBucketExists(bucket);

        var directoryPath = _bucketToDirectoryMap[bucket];

        // Get all files recursively
        var allFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);

        // Convert file paths to object keys by removing directory prefix and extensions
        var objectKeys = new List<string>();
        foreach (var filePath in allFiles)
        {
            var relativePath = Path.GetRelativePath(directoryPath, filePath);
            // Normalize path separators to forward slashes for consistency
            relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            var objectKey = RemoveKnownExtensions(relativePath);
            objectKeys.Add(objectKey);
        }

        // Filter by prefix
        if (!string.IsNullOrEmpty(prefix))
        {
            objectKeys = objectKeys.Where(key => key.StartsWith(prefix)).ToList();
        }

        // Sort for consistent ordering
        objectKeys.Sort();

        // Handle pagination
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            // Find the index after the cursor
            var cursorIndex = objectKeys.BinarySearch(cursor);
            if (cursorIndex >= 0)
            {
                startIndex = cursorIndex + 1;
            }
            else
            {
                // If cursor not found, start from the first key greater than cursor
                startIndex = ~cursorIndex;
            }
        }

        // Apply limit and get next cursor
        var endIndex = Math.Min(startIndex + limit, objectKeys.Count);
        var resultKeys = objectKeys.Skip(startIndex).Take(limit).ToList();
        string? nextCursor = null;

        if (endIndex < objectKeys.Count)
        {
            nextCursor = resultKeys.Last();
        }

        return Task.FromResult(new IBlobStorage.BlobList(resultKeys, nextCursor));
    }

    private void ValidateBucketExists(string bucket)
    {
        if (!_bucketToDirectoryMap.ContainsKey(bucket))
            throw new ArgumentException($"Bucket not defined: {bucket}", nameof(bucket));
    }

    private void ValidateObjectKey(string objectKey)
    {
        if (string.IsNullOrEmpty(objectKey))
            throw new ArgumentException("Object key cannot be null or empty", nameof(objectKey));
    }

    private string GetFilePath(string directoryPath, string objectKey, string contentType)
    {
        var extension = GetExtensionForContentType(contentType);
        return Path.Combine(directoryPath, objectKey + extension);
    }

    private string FindFilePath(string directoryPath, string objectKey)
    {
        // First try to find the exact file with any of the known extensions
        foreach (var extension in _contentTypeToExtension.Values)
        {
            var potentialPath = Path.Combine(directoryPath, objectKey + extension);
            if (File.Exists(potentialPath))
                return potentialPath;
        }

        // If no extension match found, try without extension (for backward compatibility)
        var pathWithoutExtension = Path.Combine(directoryPath, objectKey);
        if (File.Exists(pathWithoutExtension))
            return pathWithoutExtension;

        // Return the path without extension as fallback (will be created with proper extension)
        return pathWithoutExtension;
    }

    private string GetExtensionForContentType(string contentType)
    {
        if (_contentTypeToExtension.TryGetValue(contentType, out var extension))
            return extension;

        throw new ArgumentException($"Unsupported content type: {contentType}", nameof(contentType));
    }

    private string GetContentTypeFromExtension(string extension)
    {
        foreach (var kvp in _contentTypeToExtension)
        {
            if (string.Equals(kvp.Value, extension, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }

        throw new ArgumentException($"Unsupported extension: {extension}", nameof(extension));
    }

    private string RemoveKnownExtensions(string filePath)
    {
        // Try removing each known extension
        foreach (var extension in _contentTypeToExtension.Values)
        {
            if (filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(0, filePath.Length - extension.Length);
            }
        }

        // Return as-is if no known extension found (backward compatibility)
        return filePath;
    }
}
