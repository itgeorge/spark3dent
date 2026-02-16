using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace Utilities;

public class EmbeddedResourceLoader
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResourceLocks;
    private static readonly ConcurrentDictionary<string, string> ResourceCache;
    private static readonly ConcurrentDictionary<string, byte[]> ResourceByteCache;

    static EmbeddedResourceLoader()
    {
        ResourceLocks = new();
        ResourceCache = new();
        ResourceByteCache = new();
    }

    /// <summary>
    /// Loads the contents of an embedded resource file (such as a CSV) as a string.
    /// Thread-safe implementation using locks on the resourceFile to prevent concurrent loading.
    /// </summary>
    /// <param name="resourceFileName">
    /// The name of the file (e.g. "ek_atte-trimmed.csv").
    /// </param>
    /// <param name="assembly"></param>
    /// <returns>The full text contents of the embedded resource.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown if the resource cannot be found in the assembly.
    /// </exception>
    public static async Task<string> LoadEmbeddedResourceAsync(string resourceFileName, Assembly assembly)
    {
        string cacheKey = $"{assembly.GetName().Name}:{resourceFileName}";
        // Check cache first
        if (ResourceCache.TryGetValue(cacheKey, out var cachedContent))
        {
            return cachedContent;
        }

        // Get or create a lock for this specific resource file
        var resourceLock = ResourceLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        await resourceLock.WaitAsync();

        try
        {
            // Double-check cache after acquiring lock (in case another thread loaded it while we were waiting)
            if (ResourceCache.TryGetValue(cacheKey, out cachedContent))
            {
                return cachedContent;
            }

            // Resource names follow the pattern: <DefaultNamespace>.<folder>.<filename>
            // List all names to find the correct one dynamically:
            var resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                name => name.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase)
            );

            if (resourceName is null)
            {
                var available = string.Join(", ", assembly.GetManifestResourceNames());
                throw new FileNotFoundException(
                    $"Embedded resource '{resourceFileName}' not found. Available resources: {available}");
            }

            await using var stream = assembly.GetManifestResourceStream(resourceName)
                                     ?? throw new FileNotFoundException($"Failed to open embedded resource '{resourceName}'.");

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            // Cache the result
            ResourceCache.TryAdd(cacheKey, content);

            return content;
        }
        finally
        {
            resourceLock.Release();
        }
    }

    /// <summary>
    /// Loads the contents of an embedded resource file as a byte array.
    /// Thread-safe implementation using locks on the resourceFile to prevent concurrent loading.
    /// </summary>
    /// <param name="resourceFileName">
    /// The name of the file.
    /// </param>
    /// <param name="assembly"></param>
    /// <returns>The binary contents of the embedded resource.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown if the resource cannot be found in the assembly.
    /// </exception>
    public static async Task<byte[]> LoadEmbeddedResourceBytesAsync(string resourceFileName, Assembly assembly)
    {
        string cacheKey = $"{assembly.GetName().Name}:{resourceFileName}";
        // Check cache first
        if (ResourceByteCache.TryGetValue(cacheKey, out var cachedContent))
        {
            return cachedContent;
        }

        // Get or create a lock for this specific resource file
        var resourceLock = ResourceLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        await resourceLock.WaitAsync();

        try
        {
            // Double-check cache after acquiring lock (in case another thread loaded it while we were waiting)
            if (ResourceByteCache.TryGetValue(cacheKey, out cachedContent))
            {
                return cachedContent;
            }

            // Resource names follow the pattern: <DefaultNamespace>.<folder>.<filename>
            // List all names to find the correct one dynamically:
            var resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                name => name.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase)
            );

            if (resourceName is null)
            {
                var available = string.Join(", ", assembly.GetManifestResourceNames());
                throw new FileNotFoundException(
                    $"Embedded resource '{resourceFileName}' not found. Available resources: {available}");
            }

            await using var stream = assembly.GetManifestResourceStream(resourceName)
                                     ?? throw new FileNotFoundException($"Failed to open embedded resource '{resourceName}'.");

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var content = memoryStream.ToArray();

            // Cache the result
            ResourceByteCache.TryAdd(cacheKey, content);

            return content;
        }
        finally
        {
            resourceLock.Release();
        }
    }
}