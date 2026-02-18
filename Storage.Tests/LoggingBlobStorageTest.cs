using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Storage;
using Utilities;
using Utilities.Tests;

namespace Storage.Tests;

[TestFixture]
[TestOf(typeof(LoggingBlobStorage))]
public class LoggingBlobStorageTest
{
    [Test]
    public async Task UploadAsync_WhenWrappingRealStorage_ThenDelegatesAndReturnsPath()
    {
        var (storage, tempDir) = CreateStorageFixture();
        var logger = new CapturingLogger();
        var sut = new LoggingBlobStorage(storage, logger);

        using var content = new MemoryStream();
        content.Write(new byte[] { 1, 2, 3 });
        content.Position = 0;

        var path = await sut.UploadAsync("bucket", "test-key", content, "application/pdf");

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(path), Is.True);
        Assert.That(logger.InfoMessages, Does.Contain("BlobStorage.UploadAsync bucket=bucket objectKey=test-key"));
        Assert.That(logger.InfoMessages, Does.Contain("BlobStorage.UploadAsync completed bucket=bucket objectKey=test-key"));
        Cleanup(tempDir);
    }

    [Test]
    public void UploadAsync_WhenBucketNotDefined_ThenPropagatesExceptionAndLogsError()
    {
        var storage = new LocalFileSystemBlobStorage(new Dictionary<string, string> { ["application/pdf"] = ".pdf" });
        var logger = new CapturingLogger();
        var sut = new LoggingBlobStorage(storage, logger);

        using var content = new MemoryStream();
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await sut.UploadAsync("undefined-bucket", "key", content, "application/pdf"));
        Assert.That(ex!.Message, Does.Contain("undefined-bucket"));
        Assert.That(logger.InfoMessages, Does.Contain("BlobStorage.UploadAsync bucket=undefined-bucket objectKey=key"));
        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task UploadAsync_WhenLoggerThrows_ThenOperationSucceedsAnyway()
    {
        var (storage, tempDir) = CreateStorageFixture();
        var logger = new ThrowingLogger();
        var sut = new LoggingBlobStorage(storage, logger);

        using var content = new MemoryStream();
        content.Write(new byte[] { 1, 2, 3 });
        content.Position = 0;

        var path = await sut.UploadAsync("bucket", "test-key", content, "application/pdf");

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(path), Is.True);
        Cleanup(tempDir);
    }

    private static (LocalFileSystemBlobStorage storage, string tempDir) CreateStorageFixture()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LoggingBlobStorageTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var storage = new LocalFileSystemBlobStorage(new Dictionary<string, string> { ["application/pdf"] = ".pdf" });
        storage.DefineBucket("bucket", tempDir);
        return (storage, tempDir);
    }

    private static void Cleanup(string tempDir)
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }
}
