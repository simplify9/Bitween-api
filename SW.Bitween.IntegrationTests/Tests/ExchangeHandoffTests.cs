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
/// Exchange files are written beside the work instead of before it (finding 3 of
/// docs/design/processing-performance.md), and the rule that kept them safe still holds: a saved
/// exchange or result names only files storage has.
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
    public async Task A_response_that_cannot_be_written_is_a_failure_that_names_no_response()
    {
        var subscriptionId = await SubscriptionAsync();
        var xchangeId = await CreateAsync(subscriptionId, "{\"order\":7}");

        await using (var worker = fixture.CreateScope())
        {
            var failing = Storage.Wrapping(worker.ServiceProvider.GetRequiredService<ICloudFilesService>(), failWritesOf: "/response");
            await ActivatorUtilities.CreateInstance<XchangeService>(worker.ServiceProvider, failing)
                .Process("handoff-test", JsonConvert.SerializeObject(new { Id = xchangeId }));
        }

        await using var check = fixture.CreateScope();
        var result = await check.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<XchangeResult>()
            .AsNoTracking().SingleAsync(r => r.Id == xchangeId);
        Assert.False(result.Success);
        Assert.Contains("storage refused", result.Exception);
        Assert.Equal(0, result.ResponseSize);
        Assert.Null(result.ResponseName);
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

    private async Task<string> CreateAsync(int subscriptionId, string payload)
    {
        await using var scope = fixture.CreateScope();
        var subscription = await scope.ServiceProvider.GetRequiredService<IInfolinkCache>().SubscriptionByIdAsync(subscriptionId);
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(subscription, new XchangeFile(payload));
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().SaveChangesAsync();
        return xchange.Id;
    }

    private async Task<int> SubscriptionAsync()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, $"Handoff doc {Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"Handoff sub {Guid.NewGuid():N}", document.Id,
            SubscriptionType.GatewayApiCall) { Inactive = false, HandlerId = nameof(NativeTestResponder) };
        subscription.SetDictionaries(
            new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>());
        db.Add(subscription);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        return subscription.Id;
    }

    /// <summary>The fixture's storage, with the writes of one kind of exchange file slowed down or refused.</summary>
    public class Storage : DispatchProxy
    {
        private ICloudFilesService inner = null!;
        private string? delayed, failed;
        private TimeSpan delay;

        public static ICloudFilesService Wrapping(ICloudFilesService inner, string? delayWritesOf = null,
            string? failWritesOf = null, TimeSpan delay = default)
        {
            var proxy = Create<ICloudFilesService, Storage>();
            var storage = (Storage)(object)proxy;
            (storage.inner, storage.delayed, storage.failed, storage.delay) = (inner, delayWritesOf, failWritesOf, delay);
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

        private Task<T> Refused<T>(Func<object?> _) => Task.FromException<T>(new IOException("storage refused the write"));

        private async Task<T> Later<T>(Func<object?> call)
        {
            await Task.Delay(delay);
            var write = (Task)call()!;
            await write;
            return write is Task<T> typed ? typed.Result : default!;
        }
    }
}
