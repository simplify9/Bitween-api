using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The startup step that keeps Bitween's container private on Azure, against Azurite — the Azure storage
/// emulator — with the container set up the way the storage library sets it up: open to everyone.
/// </summary>
public sealed class PrivateAzureContainerTests : IAsyncLifetime
{
    private const string Account = "devstoreaccount1";

    // Azurite's published development key.
    private const string AccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private readonly IContainer _azurite = new ContainerBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:3.35.0")
        .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck", "--loose")
        .WithPortBinding(10000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("successfully listens"))
        .Build();

    public Task InitializeAsync() => _azurite.StartAsync();

    public Task DisposeAsync() => _azurite.DisposeAsync().AsTask();

    [Fact]
    public async Task A_container_open_to_everyone_is_made_private_and_keeps_its_stored_policies()
    {
        var container = Container("open-container");
        await container.CreateAsync(PublicAccessType.BlobContainer);
        await container.SetAccessPolicyAsync(PublicAccessType.BlobContainer,
        [
            new BlobSignedIdentifier
            {
                Id = "kept",
                AccessPolicy = new BlobAccessPolicy { Permissions = "r", PolicyExpiresOn = DateTimeOffset.UtcNow.AddDays(1) }
            }
        ]);
        var blob = container.GetBlobClient("temp30/docs/abc/input");
        await blob.UploadAsync(BinaryData.FromString("{}"));
        using var anonymous = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(blob.Uri)).StatusCode);

        var access = new StorageAccess(null!, new BitweenOptions(), NullLogger<StorageAccess>.Instance);
        await Run(new PrivateAzureContainer(container, access, NullLogger<PrivateAzureContainer>.Instance));

        var policy = (await container.GetAccessPolicyAsync()).Value;
        Assert.Equal(PublicAccessType.None, policy.BlobPublicAccess);
        Assert.Equal("kept", Assert.Single(policy.SignedIdentifiers).Id);
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync(blob.Uri)).StatusCode);
        Assert.Null(access.CouldNotMakePrivate);
    }

    [Fact]
    public async Task When_it_isnt_allowed_the_reason_is_kept_for_the_settings_page()
    {
        await Container("refused-container").CreateAsync(PublicAccessType.BlobContainer);
        var access = new StorageAccess(null!, new BitweenOptions(), NullLogger<StorageAccess>.Instance);

        // Signed with the wrong key, Azure refuses — as it does a login without the right role.
        var wrongKey = Convert.ToBase64String(new byte[64]);
        await Run(new PrivateAzureContainer(Container("refused-container", wrongKey), access,
            NullLogger<PrivateAzureContainer>.Instance));

        Assert.StartsWith("Azure answered 403", access.CouldNotMakePrivate);
        Assert.Equal(PublicAccessType.BlobContainer,
            (await Container("refused-container").GetAccessPolicyAsync()).Value.BlobPublicAccess);
    }

    private BlobContainerClient Container(string name, string key = AccountKey) =>
        new BlobServiceClient(new Uri($"http://{_azurite.Hostname}:{_azurite.GetMappedPublicPort(10000)}/{Account}"),
            new StorageSharedKeyCredential(Account, key)).GetBlobContainerClient(name);

    private static async Task Run(PrivateAzureContainer step)
    {
        await step.StartAsync(CancellationToken.None);
        await step.ExecuteTask!;
    }
}
