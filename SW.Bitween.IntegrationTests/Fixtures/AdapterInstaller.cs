using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Fixtures;

internal static class AdapterInstaller
{
    private static string AdaptersRoot =>
        Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "test-adapters");

    public static async Task InstallAsync(ICloudFilesService cloudFiles,
        string projectName, string adapterId, string entryAssembly,
        IDictionary<string, string>? extraMetadata = null, string? version = null,
        IDictionary<string, string>? extraFiles = null)
    {
        var publishDir = Path.Combine(AdaptersRoot, projectName);

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(publishDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".pdb" or ".xml" or ".http") continue;
                var entryName = Path.GetRelativePath(publishDir, file);
                var entry = archive.CreateEntry(entryName);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream);
            }

            // Files a test adds to the package — an adapter.json, typically.
            foreach (var (name, content) in extraFiles ?? new Dictionary<string, string>())
            {
                var entry = archive.CreateEntry(name);
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync(content);
            }
        }

        var bytes = zipStream.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLower()[..16];
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLower();

        var metadata = new Dictionary<string, string>
        {
            { "EntryAssembly", entryAssembly },
            { "Hash", hash }
        };
        metadata["Sha256"] = sha256;
        foreach (var kv in extraMetadata ?? new Dictionary<string, string>())
            metadata[kv.Key] = kv.Value;

        using var uploadStream = new MemoryStream(bytes);
        await cloudFiles.WriteAsync(uploadStream, new WriteFileSettings
        {
            // A version is published beside the current package, as the installer does it.
            Key = (version == null ? $"adapters/{adapterId}" : $"adapters-versions/{adapterId}/{version}").ToLower(),
            ContentType = "application/zip",
            Metadata = metadata
        });
    }
}
