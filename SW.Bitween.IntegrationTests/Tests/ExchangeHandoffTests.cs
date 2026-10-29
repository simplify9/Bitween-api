using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Adapters;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Exchange files are written beside the work instead of before it, and the rule that kept them safe
/// still holds: a saved exchange or result names only files storage has. Once the handler has
/// delivered, a file that couldn't be stored is recorded as missing, with a warning, on a result that
/// stays a success.
/// </summary>
[Collection("Bitween")]
public class ExchangeHandoffTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_new_exchange_is_saved_only_once_its_input_is_in_storage()
    {
        var subscriptionId = await SubscriptionAsync();

        await using var scope = fixture.CreateScope();
        var slow = Storage.Wrapping(scope.ServiceProvider.GetRequiredService<ICloudFilesService>(),
            delayWritesOf: "/input", delay: TimeSpan.FromMilliseconds(400));
        var service = ActivatorUtilities.CreateInstance<XchangeService>(scope.ServiceProvider, slow);
        var subscription = await scope.ServiceProvider.GetRequiredService<IInfolinkCache>().SubscriptionByIdAsync(subscriptionId);

        var xchange = await service.CreateXchange(subscription, new XchangeFile("{\"slow\":true}"));
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().SaveChangesAsync();

        // Read through storage that has no delay: the save waited for the slow write.
        await using var check = fixture.CreateScope();
        Assert.Equal("{\"slow\":true}", await check.ServiceProvider.GetRequiredService<XchangeService>()
            .GetFile(xchange.Id, XchangeFileType.Input));
    }

    [Fact]
    public async Task An_input_that_cannot_be_written_saves_no_exchange()
    {
        var subscriptionId = await SubscriptionAsync();

        await using var scope = fixture.CreateScope();
        var failing = Storage.Wrapping(scope.ServiceProvider.GetRequiredService<ICloudFilesService>(), failWritesOf: "/input");
        var service = ActivatorUtilities.CreateInstance<XchangeService>(scope.ServiceProvider, failing);
        var subscription = await scope.ServiceProvider.GetRequiredService<IInfolinkCache>().SubscriptionByIdAsync(subscriptionId);

        var xchange = await service.CreateXchange(subscription, new XchangeFile("{\"lost\":true}"));
        await Assert.ThrowsAsync<IOException>(() => scope.ServiceProvider.GetRequiredService<BitweenDbContext>().SaveChangesAsync());

        await using var check = fixture.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>()
            .AnyAsync(x => x.Id == xchange.Id));
    }

    [Fact]
    public async Task A_delivery_whose_response_cannot_be_written_succeeds_naming_no_response_and_is_not_retried()
    {
        var body = $"reply-{Guid.NewGuid():N}";
        var subscriptionId = await SubscriptionAsync(body: body);
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":7}");

        var result = await ProcessFailingWritesAsync(xchangeId, "/response");

        Assert.True(result.Success, result.Exception);
        Assert.Null(result.Exception);
        Assert.Equal(0, result.ResponseSize);
        Assert.Null(result.ResponseName);
        Assert.Null(result.ResponseHash);
        Assert.Equal("Delivered; the response file could not be stored: storage refused the request", result.Warning);
        Assert.Equal(0, result.ConsecutiveFailures);
        await AssertDeliveredOnceAndNotRetriedAsync(xchangeId, body);
    }

    [Fact]
    public async Task A_delivery_whose_output_cannot_be_written_succeeds_naming_no_output_and_is_not_retried()
    {
        var body = $"reply-{Guid.NewGuid():N}";
        var subscriptionId = await SubscriptionAsync(body: body, mapped: true);
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":11}");

        var result = await ProcessFailingWritesAsync(xchangeId, "/output");

        Assert.True(result.Success, result.Exception);
        Assert.Equal(0, result.OutputSize);
        Assert.Null(result.OutputName);
        Assert.Null(result.OutputHash);
        Assert.Equal("Delivered; the output file could not be stored: storage refused the request", result.Warning);
        // The response was stored as usual.
        await using (var check = fixture.CreateScope())
            Assert.Equal(body, await check.ServiceProvider.GetRequiredService<XchangeService>().GetFile(xchangeId, XchangeFileType.Response));
        await AssertDeliveredOnceAndNotRetriedAsync(xchangeId, body);
    }

    [Fact]
    public async Task A_delivery_whose_output_and_response_both_cannot_be_written_succeeds_with_both_in_its_warning()
    {
        var body = $"reply-{Guid.NewGuid():N}";
        var subscriptionId = await SubscriptionAsync(body: body, mapped: true);
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":12}");

        var result = await ProcessFailingWritesAsync(xchangeId, "/output", "/response");

        Assert.True(result.Success, result.Exception);
        Assert.Equal(0, result.OutputSize);
        Assert.Equal(0, result.ResponseSize);
        Assert.Equal("Delivered; the output file could not be stored: storage refused the request\n" +
                     "Delivered; the response file could not be stored: storage refused the request", result.Warning);
        await AssertDeliveredOnceAndNotRetriedAsync(xchangeId, body);
    }

    [Fact]
    public async Task A_handler_that_failed_fails_the_exchange_whatever_the_output_write_did()
    {
        var body = $"reply-{Guid.NewGuid():N}";
        var subscriptionId = await SubscriptionAsync(body: body, mapped: true, fail: true);
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":13}");

        var result = await ProcessFailingWritesAsync(xchangeId, "/output");

        Assert.False(result.Success);
        Assert.Contains("the partner refused the delivery", result.Exception);
        Assert.Null(result.Warning);
        // A failed result names only files storage has.
        Assert.Equal(0, result.OutputSize);
        Assert.Null(result.OutputName);
        await using var check = fixture.CreateScope();
        Assert.True(await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<DelayedRetry>()
            .AnyAsync(r => r.Id == xchangeId), "the retry policy schedules a failure as it always did");
    }

    /// <summary>Processes the exchange on storage that refuses writes of the given files, and returns its result.</summary>
    private async Task<XchangeResult> ProcessFailingWritesAsync(string xchangeId, params string[] files)
    {
        await using (var worker = fixture.CreateScope())
        {
            var storage = worker.ServiceProvider.GetRequiredService<ICloudFilesService>();
            foreach (var file in files) storage = Storage.Wrapping(storage, failWritesOf: file);
            await ActivatorUtilities.CreateInstance<XchangeService>(worker.ServiceProvider, storage)
                .Process("handoff-test", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        await using var check = fixture.CreateScope();
        return await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeResult>()
            .AsNoTracking().SingleAsync(r => r.Id == xchangeId);
    }

    /// <summary>The partner got the document once, and the retry policy, which retries every error, scheduled nothing.</summary>
    private async Task AssertDeliveredOnceAndNotRetriedAsync(string xchangeId, string body)
    {
        Assert.Equal(1, NativeTestResponder.Deliveries.GetValueOrDefault(body));
        await using var check = fixture.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<DelayedRetry>().AnyAsync(r => r.Id == xchangeId));
        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == xchangeId));
    }

    [Fact]
    public async Task A_response_written_beside_the_result_is_in_storage_once_the_result_is_saved()
    {
        var subscriptionId = await SubscriptionAsync();
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":8}");

        await using (var worker = fixture.CreateScope())
        {
            var slow = Storage.Wrapping(worker.ServiceProvider.GetRequiredService<ICloudFilesService>(),
                delayWritesOf: "/response", delay: TimeSpan.FromMilliseconds(400));
            await ActivatorUtilities.CreateInstance<XchangeService>(worker.ServiceProvider, slow)
                .Process("handoff-test", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        await using var check = fixture.CreateScope();
        var result = await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeResult>()
            .AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        Assert.True(result.Success, result.Exception);
        Assert.Equal("{\"order\":8}", await check.ServiceProvider.GetRequiredService<XchangeService>()
            .GetFile(xchangeId, XchangeFileType.Response));
    }

    [Fact]
    public async Task A_small_input_travels_with_its_created_message_while_the_outbox_row_stays_the_id_alone()
    {
        var subscriptionId = await SubscriptionAsync();
        var (xchangeId, published) = await CreateRecordingAsync(subscriptionId, "{\"small\":true}");

        var message = JsonConvert.DeserializeObject<Dictionary<string, string>>(published.Single(p => p.Contains(xchangeId)))!;
        Assert.Equal(xchangeId, message["Id"]);
        Assert.Equal("{\"small\":true}", message["Input"]);

        await using var check = fixture.CreateScope();
        var row = await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(m => m.Body.Contains(xchangeId));
        Assert.Equal(JsonConvert.SerializeObject(new { Id = xchangeId }), row.Body);
    }

    [Fact]
    public async Task An_input_over_the_message_cap_is_left_to_storage()
    {
        var subscriptionId = await SubscriptionAsync();
        var large = JsonConvert.SerializeObject(new { text = new string('x', 300 * 1024) });
        var (xchangeId, published) = await CreateRecordingAsync(subscriptionId, large);

        Assert.Equal(JsonConvert.SerializeObject(new { Id = xchangeId }), published.Single(p => p.Contains(xchangeId)));
    }

    [Fact]
    public async Task The_worker_takes_the_input_from_the_message_and_reads_no_storage()
    {
        var subscriptionId = await SubscriptionAsync();
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":9}");

        await using (var worker = fixture.CreateScope())
        {
            var noReads = Storage.Wrapping(worker.ServiceProvider.GetRequiredService<ICloudFilesService>(), failReadsOf: "/input");
            await ActivatorUtilities.CreateInstance<XchangeService>(worker.ServiceProvider, noReads)
                .Process("handoff-test", JsonConvert.SerializeObject(new { Id = xchangeId, Input = "{\"order\":9}" }));
        }

        Assert.Equal("{\"order\":9}", await ResponseOfAsync(xchangeId));
    }

    [Fact]
    public async Task A_carried_input_that_differs_from_the_stored_one_is_not_used()
    {
        var subscriptionId = await SubscriptionAsync();
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":10}");

        await using (var worker = fixture.CreateScope())
            await worker.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("handoff-test", JsonConvert.SerializeObject(new { Id = xchangeId, Input = "{\"order\":666}" }));

        Assert.Equal("{\"order\":10}", await ResponseOfAsync(xchangeId));
    }

    private async Task<string> ResponseOfAsync(string xchangeId)
    {
        await using var check = fixture.CreateScope();
        var result = await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeResult>()
            .AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        Assert.True(result.Success, result.Exception);
        return await check.ServiceProvider.GetRequiredService<XchangeService>().GetFile(xchangeId, XchangeFileType.Response);
    }

    /// <summary>Creates an exchange through a context whose publishes are recorded, and returns them.</summary>
    private async Task<(string XchangeId, List<string> Published)> CreateRecordingAsync(int subscriptionId, string payload)
    {
        await using var scope = fixture.CreateScope();
        var publish = new RecordingPublish();
        await using var db = ActivatorUtilities.CreateInstance<PgSql.BitweenDbContext>(scope.ServiceProvider, publish);
        var service = ActivatorUtilities.CreateInstance<XchangeService>(scope.ServiceProvider, (BitweenDbContext)db);
        var subscription = await scope.ServiceProvider.GetRequiredService<IInfolinkCache>().SubscriptionByIdAsync(subscriptionId);

        var xchange = await service.CreateXchange(subscription, new XchangeFile(payload));
        await db.SaveChangesAsync();
        return (xchange.Id, publish.Bodies);
    }

    private sealed class RecordingPublish : IPublish
    {
        public List<string> Bodies { get; } = [];
        public Task Publish<TMessage>(TMessage message) => Publish(typeof(TMessage).Name, JsonConvert.SerializeObject(message));
        public Task Publish(string messageTypeName, byte[] message) => Publish(messageTypeName, System.Text.Encoding.UTF8.GetString(message));

        public Task Publish(string messageTypeName, string message)
        {
            Bodies.Add(message);
            return Task.CompletedTask;
        }
    }

    private async Task<string> CreateAsync(int subscriptionId, string payload)
    {
        await using var scope = fixture.CreateScope();
        var subscription = await scope.ServiceProvider.GetRequiredService<IInfolinkCache>().SubscriptionByIdAsync(subscriptionId);
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(subscription, new XchangeFile(payload));
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().SaveChangesAsync();
        return xchange.Id;
    }

    /// <summary>
    /// A subscription delivering through <see cref="NativeTestResponder"/>, answering <paramref name="body"/>
    /// when given (or else echoing), after a mapping when <paramref name="mapped"/>, and failing when
    /// <paramref name="fail"/>. Its retry policy retries every error, so a failure shows as a retry.
    /// </summary>
    private async Task<int> SubscriptionAsync(string? body = null, bool mapped = false, bool fail = false)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, $"Handoff doc {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Handoff sub {Guid.NewGuid():N}", document.Id,
            SubscriptionType.GatewayApiCall) { Inactive = false, HandlerId = nameof(NativeTestResponder) };
        var handler = new Dictionary<string, string>();
        if (body != null) handler["Body"] = body;
        if (fail) handler["Fail"] = "true";
        var mapper = new Dictionary<string, string>();
        if (mapped)
        {
            subscription.MapperId = nameof(NativeAdapters.Mapper.NativeMapper);
            mapper["MappingRules"] = JsonConvert.SerializeObject(new
            {
                version = 1, sourceFormat = "json", targetFormat = "json",
                fields = new object[] { new { target = new[] { "ref" }, from = new { kind = "Path", path = "order" } } },
            });
        }

        subscription.SetDictionaries(
            handler, mapper, new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());
        subscription.SetRetryPolicy(null, new CustomRetryPolicy
        {
            Groups =
            [
                new RetryGroup
                {
                    Name = "Errors",
                    Priority = 10,
                    AppliesTo = [XchangeResultType.Error],
                    Matchers = [],
                    Budget = new RetryBudget
                    {
                        MaxAttemptsPerError = 5,
                        MaxAttemptsTotal = 1000,
                        DelayStrategy = new FixedDelayStrategy { DelayMs = 60_000 },
                    },
                },
            ],
        });
        db.Add(subscription);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription.Id;
    }

    /// <summary>The fixture's storage, with the writes of one kind of exchange file slowed down or refused, or its reads refused.</summary>
    public class Storage : DispatchProxy
    {
        private ICloudFilesService inner = null!;
        private string? delayed, failed, unreadable;
        private TimeSpan delay;

        public static ICloudFilesService Wrapping(ICloudFilesService inner, string? delayWritesOf = null,
            string? failWritesOf = null, TimeSpan delay = default, string? failReadsOf = null)
        {
            var proxy = Create<ICloudFilesService, Storage>();
            var storage = (Storage)(object)proxy;
            (storage.inner, storage.delayed, storage.failed, storage.delay, storage.unreadable) =
                (inner, delayWritesOf, failWritesOf, delay, failReadsOf);
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var key = args?.OfType<WriteFileSettings>().FirstOrDefault()?.Key;
            if (method!.Name.StartsWith("Write") && key != null)
            {
                if (failed != null && key.EndsWith(failed))
                    return Returning(method.ReturnType, nameof(Refused), () => null);
                if (delayed != null && key.EndsWith(delayed))
                    return Returning(method.ReturnType, nameof(Later), () => method.Invoke(inner, args));
            }

            if (unreadable != null && (method.Name.StartsWith("Open") || method.Name.StartsWith("Read")) &&
                args?.FirstOrDefault() is string readKey && readKey.EndsWith(unreadable))
                return Returning(method.ReturnType, nameof(Refused), () => null);

            return method.Invoke(inner, args);
        }

        /// <summary>The task the interface method returns, whichever <c>Task</c> or <c>Task&lt;T&gt;</c> that is.</summary>
        private object Returning(Type returnType, string how, Func<object?> call)
        {
            var result = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : typeof(bool);
            var task = typeof(Storage).GetMethod(how, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(result).Invoke(this, [call])!;
            return returnType.IsGenericType ? task : (Task)task;
        }

        private Task<T> Refused<T>(Func<object?> _) => Task.FromException<T>(new IOException("storage refused the request"));

        private async Task<T> Later<T>(Func<object?> call)
        {
            await Task.Delay(delay);
            var write = (Task)call()!;
            await write;
            return write is Task<T> typed ? typed.Result : default!;
        }
    }
}
