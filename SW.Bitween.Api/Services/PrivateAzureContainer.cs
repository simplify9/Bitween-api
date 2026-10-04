using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SW.Bitween;

/// <summary>
/// Keeps Bitween's container private on Azure. Azure has no private setting per file — the container
/// decides for everything in it — and the storage library creates the container open to everyone, so at
/// startup it's switched to private. Bitween reads and writes with its own credentials and hands out links
/// it serves itself, so nothing of Bitween's needs anonymous access. When the switch isn't allowed, the
/// reason goes to <see cref="StorageAccess"/> for the settings page.
/// </summary>
public class PrivateAzureContainer(BlobContainerClient container, StorageAccess storageAccess,
    ILogger<PrivateAzureContainer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var policy = (await container.GetAccessPolicyAsync(cancellationToken: stoppingToken)).Value;
            if (policy.BlobPublicAccess == PublicAccessType.None) return;

            // Its stored access policies go back as they were: only anonymous access changes.
            await container.SetAccessPolicyAsync(PublicAccessType.None, policy.SignedIdentifiers,
                cancellationToken: stoppingToken);
            logger.LogWarning("Container {Container} let anyone read its files ({Access}); it is private now.",
                container.Name, policy.BlobPublicAccess);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not make container {Container} private.", container.Name);
            storageAccess.CouldNotMakePrivate = ex is RequestFailedException failed
                ? $"Azure answered {failed.Status} {failed.ErrorCode}"
                : ex.Message;
        }
    }
}
