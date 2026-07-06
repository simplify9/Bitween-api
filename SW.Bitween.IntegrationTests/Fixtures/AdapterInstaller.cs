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
        string projectName, string adapterId, string entryAssembly)
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
        }

        var bytes = zipStream.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLower()[..16];

        using var uploadStream = new MemoryStream(bytes);
        await cloudFiles.WriteAsync(uploadStream, new WriteFileSettings
        {
            Key = $"adapters/{adapterId}".ToLower(),
            ContentType = "application/zip",
            Metadata = new Dictionary<string, string>
            {
                { "EntryAssembly", entryAssembly },
                { "Hash", hash }
            }
        });
    }
}
