using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services;

/// <summary>
/// Whether Bitween can write its files and read them back. Every exchange writes to storage, and a
/// wrong bucket or credential used to show up only as the first exchange failing.
/// </summary>
public static class StorageCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Runs after the stored settings are applied, which hold the document prefix.</summary>
    public static IHost CheckStorage(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var options = services.GetRequiredService<BitweenOptions>();
        var problem = Check(services.GetRequiredService<ICloudFilesService>(), options.DocumentPrefix,
                $"{options.StorageProvider}, bucket {services.GetRequiredService<IConfiguration>()["CloudFiles:BucketName"]}")
            .GetAwaiter().GetResult();
        if (problem is not null) host.Services.GetRequiredService<StartupProblems>().Add(problem);
        return host;
    }

    /// <param name="storage">Which storage, for the message.</param>
    /// <returns>The startup problem, or null when the file came back as it was written.</returns>
    public static async Task<string> Check(ICloudFilesService cloudFiles, string documentPrefix, string storage)
    {
        // The file the settings page writes to tell whether the bucket is open to everyone.
        var key = $"{documentPrefix}/{StorageAccess.ProbeName}";
        try
        {
            await cloudFiles.WriteTextAsync(StorageAccess.ProbeText,
                new WriteFileSettings { Key = key, ContentType = "text/plain" }).WaitAsync(Timeout);
            await using var stream = await cloudFiles.OpenReadAsync(key).WaitAsync(Timeout);
            using var reader = new StreamReader(stream);
            if (await reader.ReadToEndAsync().WaitAsync(Timeout) == StorageAccess.ProbeText) return null;
            return Problem(storage, "the file read back is not the one written.");
        }
        catch (Exception ex)
        {
            return Problem(storage, ex is TimeoutException ? $"no answer within {Timeout.TotalSeconds:0} seconds." : ex.Message);
        }
    }

    public static string Problem(string storage, string reason) =>
        $"Bitween can't write its files to storage ({storage}) and read them back: {reason} Check " +
        "Bitween:StorageProvider and the CloudFiles settings: the address, the bucket and the credentials.";
}
