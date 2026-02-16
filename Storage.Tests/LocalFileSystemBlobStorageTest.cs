using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Storage.Tests;

[TestFixture]
[Category("Integration")]
[Parallelizable(ParallelScope.Children)]
[TestOf(typeof(LocalFileSystemBlobStorage))]
public class LocalFileSystemBlobStorageIntegrationTest
{
    private sealed class Fixture : IDisposable
    {
        private readonly string _testDirectory;
        private readonly LocalFileSystemBlobStorage _storage;
        private readonly Dictionary<string, string> _contentTypeMapping;

        public const string TestBucket1 = "test-bucket-1";
        public const string TestBucket2 = "test-bucket-2";
        public const string TestBucket3 = "test-bucket-3";

        public Fixture()
        {
            _contentTypeMapping = new Dictionary<string, string>
            {
                ["image/jpeg"] = ".jpg",
                ["image/png"] = ".png",
                ["text/plain"] = ".txt",
                ["application/json"] = ".json"
            };

            _testDirectory = Path.Combine(Path.GetTempPath(), "LocalFileSystemBlobStorageTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);

            _storage = new LocalFileSystemBlobStorage(_contentTypeMapping);
            _storage.DefineBucket(TestBucket1, _testDirectory);
            _storage.DefineBucket(TestBucket2, _testDirectory);
            _storage.DefineBucket(TestBucket3, _testDirectory);
        }

        public IBlobStorage Storage => _storage;

        public string GetFullFilePath(string objectKey, string contentType)
        {
            var extension = _contentTypeMapping[contentType];
            return Path.Combine(_testDirectory, objectKey + extension);
        }

        public bool FileExists(string objectKey, string contentType)
        {
            return File.Exists(GetFullFilePath(objectKey, contentType));
        }

        public string ReadFileContent(string objectKey, string contentType)
        {
            var filePath = GetFullFilePath(objectKey, contentType);
            return File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        }

        public byte[] ReadFileBytes(string objectKey, string contentType)
        {
            var filePath = GetFullFilePath(objectKey, contentType);
            return File.Exists(filePath) ? File.ReadAllBytes(filePath) : Array.Empty<byte>();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
    }

    [Test]
    public void UploadAsync_GivenValidBucket_WhenUploadingValidData_ThenObjectCanBeRetrieved()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using var stream = new MemoryStream(contentBytes);

        var result = fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Result;

        Assert.That(result, Is.Not.Null);
        Assert.That(fixture.FileExists(objectKey, contentType), Is.True);
        Assert.That(fixture.ReadFileContent(objectKey, contentType), Is.EqualTo(content));
    }

    [Test]
    public void UploadAsync_GivenValidBucket_WhenUploadingFailingDataStream_ThenObjectDoesNotExist()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var contentType = "text/plain";

        // Create a stream that will fail when read
        var failingStream = new FailingStream();

        Assert.ThrowsAsync<Exception>(async () =>
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, failingStream, contentType));

        Assert.That(fixture.FileExists(objectKey, contentType), Is.False);
    }

    [Test]
    public void UploadAsync_GivenNonExistingBucket_WhenUploadingToIt_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using var stream = new MemoryStream(contentBytes);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Storage.UploadAsync("non-existing-bucket", objectKey, stream, contentType));
    }

    [Test]
    public void UploadAsync_GivenContentTypeMapping_WhenUploadingValidData_ThenFileCreatedWithMappedContentType()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using var stream = new MemoryStream(contentBytes);

        var result = fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Result;

        Assert.That(result, Is.Not.Null);
        Assert.That(fixture.FileExists(objectKey, contentType), Is.True);
        var expectedFilePath = fixture.GetFullFilePath(objectKey, contentType);
        Assert.That(result, Is.EqualTo(expectedFilePath));
        Assert.That(fixture.ReadFileContent(objectKey, contentType), Is.EqualTo(content));
    }

    [Test]
    public void UploadAsync_GivenValidContentTypeMapping_WhenUploadingValidDataWithNonMatchingContentType_ThenThrowsAndNoObjectCanBeRetrieved()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "application/xml"; // Not in the mapping
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using var stream = new MemoryStream(contentBytes);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType));

        Assert.That(fixture.FileExists(objectKey, "text/plain"), Is.False);
        Assert.That(fixture.FileExists(objectKey, "image/jpeg"), Is.False);
    }

    [Test]
    public async Task OpenReadAsync_GivenExistingBlob_WhenOpenReadAsyncMatchingBlob_ThenStreamContainsBlobData()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType);
        }

        using var readStream = await fixture.Storage.OpenReadAsync(Fixture.TestBucket1, objectKey);
        using var reader = new StreamReader(readStream);
        var readContent = await reader.ReadToEndAsync();

        Assert.That(readContent, Is.EqualTo(content));
    }

    [Test]
    public void OpenReadAsync_GivenExistingBlob_WhenOpenReadAsyncWithDifferentBucket_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Wait();
        }

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Storage.OpenReadAsync("different-bucket", objectKey));
    }

    [Test]
    public void OpenReadAsync_GivenExistingBlob_WhenOpenReadAsyncWithDifferentObjectKey_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Wait();
        }

        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.Storage.OpenReadAsync(Fixture.TestBucket1, "different-object"));
    }

    [Test]
    public void OpenReadAsync_GivenNonExistingBlob_WhenOpenReadAsync_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "non-existing-object";

        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.Storage.OpenReadAsync(Fixture.TestBucket1, objectKey));
    }

    [Test]
    public async Task ExistsAsync_GivenExistingBlob_WhenExistsAsyncMatchingBlob_ThenReturnsTrue()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType);
        }

        var exists = await fixture.Storage.ExistsAsync(Fixture.TestBucket1, objectKey);

        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task ExistsAsync_GivenExistingBlob_WhenExistsAsyncWithDifferentBucket_ThenReturnsFalse()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType);
        }

        var exists = await fixture.Storage.ExistsAsync("different-bucket", objectKey);

        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task ExistsAsync_GivenExistingBlob_WhenExistsAsyncWithDifferentObjectKey_ThenReturnsFalse()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType);
        }

        var exists = await fixture.Storage.ExistsAsync(Fixture.TestBucket1, "different-object");

        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task ExistsAsync_GivenNonExistingBlob_WhenExistsAsyncCalled_ThenReturnsFalse()
    {
        using var fixture = new Fixture();
        var objectKey = "non-existing-object";

        var exists = await fixture.Storage.ExistsAsync(Fixture.TestBucket1, objectKey);

        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task DeleteAsync_GivenExistingBlob_WhenDeleteAsyncMatchingBlob_ThenObjectCannotBeRetrieved()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType);
        }

        await fixture.Storage.DeleteAsync(Fixture.TestBucket1, objectKey);

        Assert.That(fixture.FileExists(objectKey, contentType), Is.False);
        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.Storage.OpenReadAsync(Fixture.TestBucket1, objectKey));
    }

    [Test]
    public void DeleteAsync_GivenExistingBlob_WhenDeleteAsyncWithDifferentBucket_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Wait();
        }

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Storage.DeleteAsync("different-bucket", objectKey));
    }

    [Test]
    public void DeleteAsync_GivenExistingBlob_WhenDeleteAsyncWithDifferentObjectKey_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Wait();
        }

        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.Storage.DeleteAsync(Fixture.TestBucket1, "different-object"));
    }

    [Test]
    public async Task DeleteAsync_GivenMultipleExistingBlobs_WhenDeleteAsyncOnOneOfThem_ThenOnlyThatObjectCannotBeRetrieved()
    {
        using var fixture = new Fixture();
        var objectKey1 = "test-object-1";
        var objectKey2 = "test-object-2";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey1, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, stream, contentType);
        }

        await fixture.Storage.DeleteAsync(Fixture.TestBucket1, objectKey1);

        Assert.That(fixture.FileExists(objectKey1, contentType), Is.False);
        Assert.That(fixture.FileExists(objectKey2, contentType), Is.True);
        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.Storage.OpenReadAsync(Fixture.TestBucket1, objectKey1));
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, objectKey1), Is.False);
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, objectKey2), Is.True);
    }

    [Test]
    public void DeleteAsync_GivenNonExistingBlob_WhenDeleteAsync_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "non-existing-object";

        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.Storage.DeleteAsync(Fixture.TestBucket1, objectKey));
    }

    [Test]
    public void UriFor_GivenValidBucketAndObjectKey_ThenReturnsValidUrl()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using (var stream = new MemoryStream(contentBytes))
        {
            fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Wait();
        }

        var uri = fixture.Storage.UriFor(Fixture.TestBucket1, objectKey);

        Assert.That(uri, Is.Not.Null);
        Assert.That(uri, Does.StartWith("file://"));
        var expectedFilePath = fixture.GetFullFilePath(objectKey, contentType);
        Assert.That(uri, Is.EqualTo(new Uri(expectedFilePath).AbsoluteUri));
    }

    [Test]
    public void UploadAsync_GivenObjectKeyWithPathSeparators_ThenCreatesNestedDirectories()
    {
        using var fixture = new Fixture();
        var objectKey = "path/to/test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using var stream = new MemoryStream(contentBytes);

        var result = fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Result;

        Assert.That(result, Is.Not.Null);
        var expectedFilePath = fixture.GetFullFilePath(objectKey, contentType);
        Assert.That(result, Is.EqualTo(expectedFilePath));
        Assert.That(File.Exists(expectedFilePath), Is.True);
        Assert.That(fixture.ReadFileContent(objectKey, contentType), Is.EqualTo(content));
    }

    [Test]
    public async Task UploadAsync_GivenExistingBlob_WhenUploadAsyncWithMatchingBucketAndKey_ThenBlobOverwritten()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var originalContent = "Original content";
        var newContent = "New overwritten content";
        var contentType = "text/plain";
        var originalBytes = Encoding.UTF8.GetBytes(originalContent);
        var newBytes = Encoding.UTF8.GetBytes(newContent);

        // Upload original content
        using (var originalStream = new MemoryStream(originalBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, originalStream, contentType);
        }

        // Verify original content exists
        Assert.That(fixture.FileExists(objectKey, contentType), Is.True);
        Assert.That(fixture.ReadFileContent(objectKey, contentType), Is.EqualTo(originalContent));

        // Upload new content with same key
        using (var newStream = new MemoryStream(newBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, newStream, contentType);
        }

        // Verify content was overwritten
        Assert.That(fixture.FileExists(objectKey, contentType), Is.True);
        Assert.That(fixture.ReadFileContent(objectKey, contentType), Is.EqualTo(newContent));
        Assert.That(fixture.ReadFileContent(objectKey, contentType), Is.Not.EqualTo(originalContent));
    }

    [Test]
    public async Task UploadAsync_GivenMultipleExistingBlobs_WhenUploadAsyncMatchingOneOfThem_ThenThatBlobIsOverwritten()
    {
        using var fixture = new Fixture();
        var objectKey1 = "test-object-1";
        var objectKey2 = "test-object-2";
        var objectKey3 = "test-object-3";
        var originalContent = "Original content";
        var newContent = "New overwritten content";
        var contentType = "text/plain";
        var originalBytes = Encoding.UTF8.GetBytes(originalContent);
        var newBytes = Encoding.UTF8.GetBytes(newContent);

        // Upload multiple blobs with original content
        using (var originalStream = new MemoryStream(originalBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey1, originalStream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, originalStream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey3, originalStream, contentType);
        }

        // Verify all original blobs exist with original content
        Assert.That(fixture.FileExists(objectKey1, contentType), Is.True);
        Assert.That(fixture.FileExists(objectKey2, contentType), Is.True);
        Assert.That(fixture.FileExists(objectKey3, contentType), Is.True);
        Assert.That(fixture.ReadFileContent(objectKey1, contentType), Is.EqualTo(originalContent));
        Assert.That(fixture.ReadFileContent(objectKey2, contentType), Is.EqualTo(originalContent));
        Assert.That(fixture.ReadFileContent(objectKey3, contentType), Is.EqualTo(originalContent));

        // Upload new content to only one of the blobs (objectKey2)
        using (var newStream = new MemoryStream(newBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, newStream, contentType);
        }

        // Verify only objectKey2 was overwritten, others remain unchanged
        Assert.That(fixture.FileExists(objectKey1, contentType), Is.True);
        Assert.That(fixture.FileExists(objectKey2, contentType), Is.True);
        Assert.That(fixture.FileExists(objectKey3, contentType), Is.True);

        Assert.That(fixture.ReadFileContent(objectKey1, contentType), Is.EqualTo(originalContent));
        Assert.That(fixture.ReadFileContent(objectKey2, contentType), Is.EqualTo(newContent));
        Assert.That(fixture.ReadFileContent(objectKey3, contentType), Is.EqualTo(originalContent));

        // Ensure overwritten content is different from original
        Assert.That(fixture.ReadFileContent(objectKey2, contentType), Is.Not.EqualTo(originalContent));
    }

    [Test]
    public void CreateUploadUrlAsync_ThenThrowsNotImplementedException()
    {
        using var fixture = new Fixture();

        Assert.Throws<NotImplementedException>(() =>
            fixture.Storage.CreateUploadUrlAsync(Fixture.TestBucket1, "test-object", TimeSpan.FromMinutes(5), "text/plain").GetAwaiter().GetResult());
    }

    [Test]
    public async Task ListAsync_GivenExistingBlobs_WhenListAsyncWithMatchingBucket_ThenReturnsAllObjectKeys()
    {
        using var fixture = new Fixture();
        var objectKey1 = "test-object-1";
        var objectKey2 = "test-object-2";
        var objectKey3 = "test-object-3";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload multiple objects
        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey1, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey3, stream, contentType);
        }

        var result = await fixture.Storage.ListAsync(Fixture.TestBucket1, "");

        Assert.That(result.ObjectKeys, Is.Not.Null);
        Assert.That(result.ObjectKeys.Count, Is.EqualTo(3));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey1));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey2));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey3));
        Assert.That(result.NextCursor, Is.Null); // No more results
    }

    [Test]
    public async Task ListAsync_GivenExistingBlobs_WhenListAsyncWithPrefix_ThenReturnsFilteredObjectKeys()
    {
        using var fixture = new Fixture();
        var objectKey1 = "prefix-test-object-1";
        var objectKey2 = "prefix-test-object-2";
        var objectKey3 = "other-test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload objects with different prefixes
        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey1, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey3, stream, contentType);
        }

        var result = await fixture.Storage.ListAsync(Fixture.TestBucket1, "prefix-");

        Assert.That(result.ObjectKeys, Is.Not.Null);
        Assert.That(result.ObjectKeys.Count, Is.EqualTo(2));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey1));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey2));
        Assert.That(result.ObjectKeys, Does.Not.Contain(objectKey3));
        Assert.That(result.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_GivenExistingBlobs_WhenListAsyncWithLimit_ThenReturnsLimitedResults()
    {
        using var fixture = new Fixture();
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload 5 objects
        var objectKeys = new List<string>();
        using (var stream = new MemoryStream(contentBytes))
        {
            for (int i = 1; i <= 5; i++)
            {
                var objectKey = $"test-object-{i}";
                objectKeys.Add(objectKey);
                await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType);
            }
        }

        var result = await fixture.Storage.ListAsync(Fixture.TestBucket1, "", limit: 3);

        Assert.That(result.ObjectKeys, Is.Not.Null);
        Assert.That(result.ObjectKeys.Count, Is.EqualTo(3));
        Assert.That(result.NextCursor, Is.Not.Null);
        Assert.That(result.NextCursor, Is.EqualTo(result.ObjectKeys.Last()));
    }

    [Test]
    public async Task ListAsync_GivenExistingBlobs_WhenListAsyncWithCursor_ThenReturnsNextPage()
    {
        using var fixture = new Fixture();
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload 5 objects
        var objectKeys = new List<string>();
        using (var stream = new MemoryStream(contentBytes))
        {
            for (int i = 1; i <= 5; i++)
            {
                var objectKey = $"test-object-{i}";
                objectKeys.Add(objectKey);
                await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType);
            }
        }

        // Get first page
        var firstPage = await fixture.Storage.ListAsync(Fixture.TestBucket1, "", limit: 2);
        Assert.That(firstPage.ObjectKeys.Count, Is.EqualTo(2));
        Assert.That(firstPage.NextCursor, Is.Not.Null);

        // Get second page using cursor
        var secondPage = await fixture.Storage.ListAsync(Fixture.TestBucket1, "", limit: 2, cursor: firstPage.NextCursor);
        Assert.That(secondPage.ObjectKeys.Count, Is.EqualTo(2));
        Assert.That(secondPage.NextCursor, Is.Not.Null);

        // Get third page
        var thirdPage = await fixture.Storage.ListAsync(Fixture.TestBucket1, "", limit: 2, cursor: secondPage.NextCursor);
        Assert.That(thirdPage.ObjectKeys.Count, Is.EqualTo(1)); // Only one left
        Assert.That(thirdPage.NextCursor, Is.Null);

        // Verify no overlap between pages
        var allKeys = firstPage.ObjectKeys.Concat(secondPage.ObjectKeys).Concat(thirdPage.ObjectKeys).ToList();
        Assert.That(allKeys.Count, Is.EqualTo(5));
        Assert.That(allKeys.Distinct().Count(), Is.EqualTo(5));
    }

    [Test]
    public async Task ListAsync_GivenEmptyBucket_WhenListAsync_ThenReturnsEmptyList()
    {
        using var fixture = new Fixture();

        var result = await fixture.Storage.ListAsync(Fixture.TestBucket1, "");

        Assert.That(result.ObjectKeys, Is.Not.Null);
        Assert.That(result.ObjectKeys.Count, Is.EqualTo(0));
        Assert.That(result.NextCursor, Is.Null);
    }

    [Test]
    public void ListAsync_GivenNonExistingBucket_WhenListAsync_ThenThrows()
    {
        using var fixture = new Fixture();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Storage.ListAsync("non-existing-bucket", ""));
    }

    [Test]
    public async Task ListAsync_GivenBlobsWithPathSeparators_WhenListAsync_ThenReturnsCorrectObjectKeys()
    {
        using var fixture = new Fixture();
        var objectKey1 = "folder/test-object-1";
        var objectKey2 = "folder/subfolder/test-object-2";
        var objectKey3 = "other-folder/test-object-3";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload objects with path separators
        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey1, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey3, stream, contentType);
        }

        var result = await fixture.Storage.ListAsync(Fixture.TestBucket1, "");

        Assert.That(result.ObjectKeys, Is.Not.Null);
        Assert.That(result.ObjectKeys.Count, Is.EqualTo(3));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey1));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey2));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey3));
        Assert.That(result.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_GivenBlobsWithPathSeparators_WhenListAsyncWithPrefix_ThenReturnsFilteredResults()
    {
        using var fixture = new Fixture();
        var objectKey1 = "folder/test-object-1";
        var objectKey2 = "folder/subfolder/test-object-2";
        var objectKey3 = "other-folder/test-object-3";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload objects with path separators
        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey1, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, stream, contentType);
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey3, stream, contentType);
        }

        var result = await fixture.Storage.ListAsync(Fixture.TestBucket1, "folder/");

        Assert.That(result.ObjectKeys, Is.Not.Null);
        Assert.That(result.ObjectKeys.Count, Is.EqualTo(2));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey1));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey2));
        Assert.That(result.ObjectKeys, Does.Not.Contain(objectKey3));
        Assert.That(result.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_GivenDifferentContentTypes_WhenListAsync_ThenReturnsObjectKeysWithoutExtensions()
    {
        using var fixture = new Fixture();
        var objectKey1 = "test-object-1";
        var objectKey2 = "test-object-2";
        var objectKey3 = "test-object-3";
        var content = "Hello, World!";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload objects with different content types
        using (var stream1 = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey1, stream1, "text/plain");
        }
        using (var stream2 = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey2, stream2, "image/jpeg");
        }
        using (var stream3 = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey3, stream3, "application/json");
        }

        var result = await fixture.Storage.ListAsync(Fixture.TestBucket1, "");

        Assert.That(result.ObjectKeys, Is.Not.Null);
        Assert.That(result.ObjectKeys.Count, Is.EqualTo(3));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey1));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey2));
        Assert.That(result.ObjectKeys, Does.Contain(objectKey3));
        Assert.That(result.NextCursor, Is.Null);
    }

    [Test]
    public async Task RenameAsync_GivenExistingBlob_WhenRenameAsyncToNewKey_ThenBlobRenamedAndContentPreserved()
    {
        using var fixture = new Fixture();
        var sourceObjectKey = "source-object";
        var destinationObjectKey = "destination-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload the source blob
        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, sourceObjectKey, stream, contentType);
        }

        // Verify source exists
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, sourceObjectKey), Is.True);
        Assert.That(fixture.FileExists(sourceObjectKey, contentType), Is.True);

        // Rename the blob
        await fixture.Storage.RenameAsync(Fixture.TestBucket1, sourceObjectKey, destinationObjectKey);

        // Verify source no longer exists
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, sourceObjectKey), Is.False);
        Assert.That(fixture.FileExists(sourceObjectKey, contentType), Is.False);

        // Verify destination exists with correct content
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, destinationObjectKey), Is.True);
        Assert.That(fixture.FileExists(destinationObjectKey, contentType), Is.True);
        Assert.That(fixture.ReadFileContent(destinationObjectKey, contentType), Is.EqualTo(content));
    }

    [Test]
    public void RenameAsync_GivenExistingBlob_WhenRenameAsyncToSameKey_ThenThrows()
    {
        using var fixture = new Fixture();
        var objectKey = "test-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload the blob
        using (var stream = new MemoryStream(contentBytes))
        {
            fixture.Storage.UploadAsync(Fixture.TestBucket1, objectKey, stream, contentType).Wait();
        }

        // Attempt to rename to the same key
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Storage.RenameAsync(Fixture.TestBucket1, objectKey, objectKey));
    }

    [Test]
    public void RenameAsync_GivenNonExistingSourceBlob_WhenRenameAsync_ThenThrows()
    {
        using var fixture = new Fixture();
        var sourceObjectKey = "non-existing-source";
        var destinationObjectKey = "destination-object";

        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await fixture.Storage.RenameAsync(Fixture.TestBucket1, sourceObjectKey, destinationObjectKey));
    }

    [Test]
    public void RenameAsync_GivenNonExistingBucket_WhenRenameAsync_ThenThrows()
    {
        using var fixture = new Fixture();
        var sourceObjectKey = "source-object";
        var destinationObjectKey = "destination-object";

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await fixture.Storage.RenameAsync("non-existing-bucket", sourceObjectKey, destinationObjectKey));
    }

    [Test]
    public async Task RenameAsync_GivenExistingDestinationBlob_WhenRenameAsync_ThenDestinationOverwritten()
    {
        using var fixture = new Fixture();
        var sourceObjectKey = "source-object";
        var destinationObjectKey = "destination-object";
        var sourceContent = "Source content";
        var destinationContent = "Destination content";
        var contentType = "text/plain";

        // Upload source blob
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sourceContent)))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, sourceObjectKey, stream, contentType);
        }

        // Upload destination blob
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(destinationContent)))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, destinationObjectKey, stream, contentType);
        }

        // Verify both exist with their original content
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, sourceObjectKey), Is.True);
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, destinationObjectKey), Is.True);
        Assert.That(fixture.ReadFileContent(sourceObjectKey, contentType), Is.EqualTo(sourceContent));
        Assert.That(fixture.ReadFileContent(destinationObjectKey, contentType), Is.EqualTo(destinationContent));

        // Rename source to destination (should overwrite)
        await fixture.Storage.RenameAsync(Fixture.TestBucket1, sourceObjectKey, destinationObjectKey);

        // Verify source no longer exists
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, sourceObjectKey), Is.False);
        Assert.That(fixture.FileExists(sourceObjectKey, contentType), Is.False);

        // Verify destination exists with source content (overwritten)
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, destinationObjectKey), Is.True);
        Assert.That(fixture.FileExists(destinationObjectKey, contentType), Is.True);
        Assert.That(fixture.ReadFileContent(destinationObjectKey, contentType), Is.EqualTo(sourceContent));
    }

    [Test]
    public async Task RenameAsync_GivenObjectKeyWithPathSeparators_WhenRenameAsync_ThenCreatesNestedDirectories()
    {
        using var fixture = new Fixture();
        var sourceObjectKey = "source-object";
        var destinationObjectKey = "path/to/destination-object";
        var content = "Hello, World!";
        var contentType = "text/plain";
        var contentBytes = Encoding.UTF8.GetBytes(content);

        // Upload the source blob
        using (var stream = new MemoryStream(contentBytes))
        {
            await fixture.Storage.UploadAsync(Fixture.TestBucket1, sourceObjectKey, stream, contentType);
        }

        // Rename the blob
        await fixture.Storage.RenameAsync(Fixture.TestBucket1, sourceObjectKey, destinationObjectKey);

        // Verify destination exists with correct content
        Assert.That(await fixture.Storage.ExistsAsync(Fixture.TestBucket1, destinationObjectKey), Is.True);
        Assert.That(fixture.FileExists(destinationObjectKey, contentType), Is.True);
        Assert.That(fixture.ReadFileContent(destinationObjectKey, contentType), Is.EqualTo(content));
    }

    private class FailingStream : Stream
    {
        private int _readCount = 0;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush() => throw new NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            _readCount++;
            throw new Exception("Stream failed during read");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromException<int>(new Exception("Stream failed during async read"));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(Task.FromException<int>(new Exception("Stream failed during async read")));
        }
    }
}
