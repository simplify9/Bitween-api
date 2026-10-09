using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Adapters;

/// <summary>The source a published version carries, as listed in its manifest.</summary>
public class AdapterSourceListing
{
    public string AdapterId { get; set; }
    public string Version { get; set; }
    public string Language { get; set; }
    public string Runtime { get; set; }
    public string BuildCommand { get; set; }
    public List<string> Lockfiles { get; set; } = [];

    /// <summary>Path relative to the source folder, and its SHA-256: enough to tell which files two versions differ in.</summary>
    public List<AdapterSourceFileInfo> Files { get; set; } = [];
}

public class AdapterSourceFileInfo
{
    public string Path { get; set; }
    public string Sha256 { get; set; }
}

public class AdapterSourceFile
{
    public string Path { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }

    /// <summary>True when the file is not text, or too large to show; <see cref="Content"/> is then null.</summary>
    public bool Binary { get; set; }

    public string Content { get; set; }
}

/// <summary>
/// Reads the source a published adapter version carries — <c>serverless build</c> puts it in the
/// package under the manifest's source folder, whatever the language — so it can be shown and
/// compared in Bitween. Packages published before that, or with <c>--no-source</c>, carry none,
/// and say so by having no source in their manifest.
/// </summary>
/// <remarks>
/// The list comes from the catalog's manifest and costs no download. A file comes from the
/// package itself, and is checked against the hash its manifest recorded, so what is shown is
/// what was published. A package's source is read once and kept a few minutes: comparing two
/// versions reads several files of each.
/// </remarks>
public class AdapterSourceReader(
    AdapterCatalog catalog,
    ICloudFilesService cloudFiles,
    ServerlessOptions serverlessOptions,
    IMemoryCache cache)
{
    /// <summary>Larger than this is shown as binary: a source file this size is almost always generated.</summary>
    public const int MaxShownBytes = 1024 * 1024;

    /// <summary>A package whose source is larger than this is not read into memory at all.</summary>
    public const long MaxSourceBytes = 50L * 1024 * 1024;

    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    /// <summary>Null when the adapter or version is unknown; an empty listing when it carries no source.</summary>
    public async Task<AdapterSourceListing> ListAsync(string adapterId, string version)
    {
        var manifest = await ManifestOf(adapterId, version);
        if (manifest == null) return null;

        return new AdapterSourceListing
        {
            AdapterId = adapterId,
            Version = version,
            Language = manifest.Language,
            Runtime = manifest.Runtime,
            BuildCommand = manifest.Source?.BuildCommand,
            Lockfiles = manifest.Source?.Lockfiles ?? [],
            Files = (manifest.Source?.Files ?? [])
                .OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new AdapterSourceFileInfo { Path = f.Key, Sha256 = f.Value })
                .ToList()
        };
    }

    /// <summary>Null when the version, or the file in it, is unknown.</summary>
    public async Task<AdapterSourceFile> ReadAsync(string adapterId, string version, string path)
    {
        var manifest = await ManifestOf(adapterId, version);
        // Only what the manifest lists can be asked for, so a path can never reach outside the
        // source folder, nor into the package's binaries.
        if (manifest?.Source?.Files == null || path == null || !manifest.Source.Files.TryGetValue(path, out var expected))
            return null;

        var files = await SourceOf(adapterId, version, manifest.Source);
        if (!files.TryGetValue(path, out var bytes))
            throw new SWException($"Version {version} of '{adapterId}' lists {path} in its manifest, but its package does not carry it.");

        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new SWException($"{path} in version {version} of '{adapterId}' does not match the hash its manifest recorded; it is not shown.");

        var text = bytes.Length <= MaxShownBytes ? AsText(bytes) : null;
        return new AdapterSourceFile
        {
            Path = path,
            Sha256 = actual,
            Size = bytes.Length,
            Binary = text == null,
            Content = text
        };
    }

    async Task<AdapterManifest> ManifestOf(string adapterId, string version)
    {
        if (string.IsNullOrWhiteSpace(adapterId) || !AdapterCatalogPaths.IsVersion(version)) return null;
        var entry = await catalog.GetAsync(adapterId);
        return entry?.Find(version)?.Manifest;
    }

    async Task<Dictionary<string, byte[]>> SourceOf(string adapterId, string version, AdapterSource source)
    {
        var key = $"bitween.adapter-source.{adapterId.ToLowerInvariant()}.{version.ToLowerInvariant()}";
        if (cache.TryGetValue(key, out Dictionary<string, byte[]> cached)) return cached;

        var files = await Download(adapterId, version, source);
        cache.Set(key, files, CacheFor);
        return files;
    }

    async Task<Dictionary<string, byte[]>> Download(string adapterId, string version, AdapterSource source)
    {
        var root = serverlessOptions.AdapterRemotePath;
        var packageKey = AdapterCatalogPaths.Version(root, adapterId, version);
        if (!await Exists(packageKey))
        {
            var legacy = AdapterCatalogPaths.LegacyVersion(root, adapterId, version);
            if (!await Exists(legacy))
                throw new SWException($"The package of version {version} of '{adapterId}' is not in storage.");
            packageKey = legacy;
        }

        // A zip is read from its end, so it goes to a temp file first rather than being held whole.
        var temp = Path.GetTempFileName();
        try
        {
            await using (var remote = await cloudFiles.OpenReadAsync(packageKey))
            await using (var local = File.Create(temp))
                await remote.CopyToAsync(local);

            var prefix = (string.IsNullOrWhiteSpace(source.Path) ? AdapterSource.DefaultPath : source.Path).Trim('/') + "/";
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            using var zip = ZipFile.OpenRead(temp);
            foreach (var entry in zip.Entries)
            {
                // Zips written on Windows separate with '\'.
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.EndsWith('/')) continue;
                var relative = name[prefix.Length..];
                if (!source.Files.ContainsKey(relative)) continue;

                total += entry.Length;
                if (total > MaxSourceBytes)
                    throw new SWException($"The source of version {version} of '{adapterId}' is larger than Bitween reads.");

                await using var stream = entry.Open();
                using var buffer = new MemoryStream((int)entry.Length);
                await stream.CopyToAsync(buffer);
                files[relative] = buffer.ToArray();
            }

            return files;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    async Task<bool> Exists(string key) =>
        (await cloudFiles.ListAsync(key)).Any(f => string.Equals(f.Key, key, StringComparison.Ordinal) && f.Size > 0);

    /// <summary>The file as UTF-8 text, or null when it isn't text.</summary>
    internal static string AsText(byte[] bytes)
    {
        // A NUL byte is the reliable tell of a binary file; text in any encoding a source file
        // would use has none.
        if (Array.IndexOf(bytes, (byte)0) >= 0) return null;
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
