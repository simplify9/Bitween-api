using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using SW.Bitween.NativeAdapters.S3Receiver;
using SW.Bitween.NativeAdapters.S3UploadHandler;
using SW.PrimitiveTypes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace SW.Bitween.TransportTests;

/// <summary>
/// An S3-compatible server. SeaweedFS rather than MinIO, whose images are no longer published to a
/// public registry; with no identities configured it accepts any key pair, which is all the
/// adapters need to send.
/// </summary>
public sealed class S3Fixture : IAsyncLifetime
{
    readonly IContainer container = new ContainerBuilder()
        .WithImage("chrislusf/seaweedfs:latest")
        .WithResourceMapping(Encoding.UTF8.GetBytes(
            """{"identities":[{"name":"tests","credentials":[{"accessKey":"transport-tests","secretKey":"transport-tests-secret"}],"actions":["Admin","Read","Write","List","Tagging"]}]}"""),
            "/etc/seaweedfs/s3.json")
        .WithCommand("server", "-s3", "-s3.config=/etc/seaweedfs/s3.json", "-dir=/data",
            "-master.volumeSizeLimitMB=64", "-volume.max=200")
        .WithPortBinding(8333, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8333).ForPath("/")
            .ForStatusCodeMatching(code => (int)code < 500)))
        .Build();

    public AmazonS3Client Client { get; private set; } = null!;
    public string Url => $"http://{container.Hostname}:{container.GetMappedPublicPort(8333)}";
    public string AccessKey => "transport-tests";
    public string SecretKey => "transport-tests-secret";

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        Client = new AmazonS3Client(AccessKey, SecretKey, new AmazonS3Config { ServiceURL = Url, ForcePathStyle = true });

        // The S3 port answers before a volume is ready to take writes; one real write says it is.
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                var bucket = await BucketAsync();
                await PutAsync(bucket, "ready", "ready");
                await Client.DeleteObjectAsync(bucket, "ready");
                return;
            }
            catch (AmazonS3Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000);
            }
        }
    }

    public async Task<string> BucketAsync()
    {
        var bucket = $"b{Guid.NewGuid():N}"[..20];
        await Client.PutBucketAsync(bucket);
        return bucket;
    }

    public async Task PutAsync(string bucket, string key, string text) =>
        await Client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, ContentBody = text });

    public async Task<List<string>> KeysAsync(string bucket) =>
        (await Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket })).S3Objects?
            .Select(o => o.Key).OrderBy(k => k).ToList() ?? [];

    public async Task<(string Text, string ContentType)> GetAsync(string bucket, string key)
    {
        using var response = await Client.GetObjectAsync(bucket, key);
        using var reader = new StreamReader(response.ResponseStream);
        return (await reader.ReadToEndAsync(), response.Headers.ContentType);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await container.DisposeAsync();
    }
}

/// <summary>The S3 upload handler and receiver against a real S3-compatible server (SeaweedFS).</summary>
public class S3Tests(S3Fixture minio) : IClassFixture<S3Fixture>
{
    Dictionary<string, string> Settings(string bucket, params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string>
        {
            ["AccessKeyId"] = minio.AccessKey,
            ["SecretAccessKey"] = minio.SecretKey,
            ["ServiceUrl"] = minio.Url,
            ["BucketName"] = bucket,
        };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    [Fact]
    public async Task The_upload_handler_writes_the_file_under_its_folder_with_a_generated_name()
    {
        var bucket = await minio.BucketAsync();
        var handler = Adapters.Create<NativeS3UploadHandler>(Settings(bucket,
            ("FolderName", "outbox"), ("FileExtension", ".json"), ("ContentType", "application/json")));

        var result = await handler.Handle(new XchangeFile("{\"order\":1}", "order.json"));

        Assert.Matches(@"^outbox/\d{14}_[0-9a-f]{32}\.json$", result.Data);
        Assert.Equal([result.Data], await minio.KeysAsync(bucket));
        var (text, contentType) = await minio.GetAsync(bucket, result.Data);
        Assert.Equal("{\"order\":1}", text);
        Assert.Equal("application/json", contentType);
    }

    /// <summary>
    /// As shipped, a fixed file name is the whole key: the folder is not put in front of it. Pinned
    /// so a change to it is a decision — deployments may be writing to the root on purpose.
    /// </summary>
    [Fact]
    public async Task A_fixed_file_name_is_the_whole_key_and_overwrites()
    {
        var bucket = await minio.BucketAsync();
        var settings = Settings(bucket, ("FolderName", "outbox"), ("FileName", "daily/orders.csv"));

        await Adapters.Create<NativeS3UploadHandler>(settings).Handle(new XchangeFile("first"));
        await Adapters.Create<NativeS3UploadHandler>(settings).Handle(new XchangeFile("second"));

        Assert.Equal(["daily/orders.csv"], await minio.KeysAsync(bucket));
        Assert.Equal("second", (await minio.GetAsync(bucket, "daily/orders.csv")).Text);
    }

    [Fact]
    public async Task The_receiver_lists_its_folder_in_batches_reads_each_file_and_moves_it_once_received()
    {
        var bucket = await minio.BucketAsync();
        await minio.PutAsync(bucket, "inbox/a.json", "{\"n\":\"a\"}");
        await minio.PutAsync(bucket, "inbox/sub/b.json", "{\"n\":\"b\"}");
        await minio.PutAsync(bucket, "inbox/c.json", "{\"n\":\"c\"}");
        await minio.PutAsync(bucket, "inbox/empty/", "");
        await minio.PutAsync(bucket, "elsewhere/x.json", "not mine");

        var receiver = Adapters.Create<NativeS3Receiver>(Settings(bucket,
            ("FolderName", "inbox"), ("BatchSize", "2"), ("DeleteMovesFileTo", "done")));
        await receiver.Initialize();

        var batch = (await receiver.ListFiles()).ToList();
        Assert.Equal(2, batch.Count);
        Assert.All(batch, key => Assert.StartsWith("inbox/", key));

        foreach (var key in batch)
        {
            var file = await receiver.GetFile(key);
            Assert.StartsWith("{\"n\":", file.Data);
            Assert.Equal(key, file.Filename);
            await receiver.DeleteFile(key);
        }

        var keys = await minio.KeysAsync(bucket);
        Assert.All(batch, key => Assert.DoesNotContain(key, keys));
        Assert.All(batch, key => Assert.Contains($"done/{key["inbox/".Length..]}", keys));
        Assert.Contains("elsewhere/x.json", keys);

        // The rest comes in the next batch, the folder marker never does.
        var next = (await receiver.ListFiles()).ToList();
        Assert.Single(next);
        await receiver.Finalize();
    }

    [Fact]
    public async Task The_receiver_deletes_a_received_file_when_there_is_nowhere_to_move_it()
    {
        var bucket = await minio.BucketAsync();
        await minio.PutAsync(bucket, "in/only.txt", "hello");
        var receiver = Adapters.Create<NativeS3Receiver>(Settings(bucket, ("FolderName", "in"), ("ResponseEncoding", "base64")));
        await receiver.Initialize();

        var key = Assert.Single(await receiver.ListFiles());
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("hello")), (await receiver.GetFile(key)).Data);
        await receiver.DeleteFile(key);

        Assert.Empty(await minio.KeysAsync(bucket));
    }
}
