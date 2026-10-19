using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>Storage that can't be written to stops the start, rather than the first exchange.</summary>
[Collection("Bitween")]
public class StorageCheckTests(BitweenFixture fixture)
{
    [Fact]
    public async Task Storage_that_takes_the_file_and_gives_it_back_passes()
    {
        await using var scope = fixture.CreateScope();
        Assert.Null(await StorageCheck.Check(scope.ServiceProvider.GetRequiredService<ICloudFilesService>(),
            "temp30/storage-check", "LocalTests, bucket tests"));
    }

    [Fact]
    public async Task Storage_that_cannot_be_reached_says_why_and_what_to_check()
    {
        var problem = await StorageCheck.Check(DispatchProxy.Create<ICloudFilesService, Unreachable>(),
            "temp30/storage-check", "S3, bucket bitween");

        Assert.Contains("(S3, bucket bitween)", problem);
        Assert.Contains("No such host is known", problem);
        Assert.Contains("CloudFiles settings", problem);
    }

    public class Unreachable : DispatchProxy
    {
        protected override object Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new HttpRequestException("No such host is known. (s3.example.invalid:443)");
    }
}
