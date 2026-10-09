using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Controllers;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;
using SW.Serverless.Tooling;
using SW.Serverless.Tooling.Building;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Adapters written in Python and JavaScript, built and published as serverless build and publish
/// do it — so only to their versions and the catalog, never to adapters/{id} — then listed,
/// described from their manifests, and run through subscriptions with their settings, exactly as
/// .NET ones are.
/// </summary>
[Collection("Bitween")]
public class ScriptAdapterTests(BitweenFixture fixture)
{
    static int _seq;
    static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref _seq)}";

    static readonly SemaphoreSlim Published = new(1, 1);
    static bool _published;

    /// <summary>Builds and publishes both Python adapters once for the class.</summary>
    async Task PublishAsync()
    {
        await Published.WaitAsync();
        try
        {
            if (_published) return;
            await using var scope = fixture.CreateScope();
            var files = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
            foreach (var (folder, project) in new[] { ("PythonAdapters", "orders"), ("PythonAdapters", "checks"), ("NodeAdapters", "orders") })
            {
                var source = Path.Combine(AppContext.BaseDirectory, folder, project);
                var work = Path.Combine(Path.GetTempPath(), "bitween-python", Guid.NewGuid().ToString("N"));
                CopyFolder(source, Path.Combine(work, folder, project));

                var built = await PackageBuilder.BuildAsync(new BuildRequest
                {
                    ProjectDirectory = Path.Combine(work, folder, project),
                    OutputDirectory = Path.Combine(work, "out"),
                });
                Assert.True(built.Succeeded, string.Join("; ", built.Problems));

                var published = await PackagePublisher.PublishPackageAsync(files, new PublishPackageRequest
                {
                    PackagePath = built.ZipPath,
                    PublishedBy = "tests",
                }, _ => { });
                scope.ServiceProvider.GetRequiredService<AdapterCatalog>().Forget(published.Manifest.Id);
            }
            _published = true;
        }
        finally
        {
            Published.Release();
        }
    }

    static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    async Task<int> CreateSubscription(SubscriptionType type, Action<SubscriptionCreate> configure)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("Python doc"), DocumentFormat.Json);
        var partner = new Partner(Unique("Python partner"));
        db.AddRange(document, partner);
        await db.SaveChangesAsync();

        scope.Superuser();
        var request = new SubscriptionCreate
        {
            Name = Unique("Python subscription"),
            DocumentId = document.Id,
            PartnerId = partner.Id,
            Type = type,
        };
        configure(request);
        var id = (int)await ActivatorUtilities.CreateInstance<Resources.Subscriptions.Create>(scope.ServiceProvider).Handle(request);
        await db.Set<Subscription>().Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.Inactive, false));
        return id;
    }

    async Task<(XchangeResult Result, string Response)> Run(int subscriptionId, string input)
    {
        string xchangeId;
        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
            xchangeId = await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .SubmitSubscriptionXchange(subscriptionId, new XchangeFile(input));
        }

        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<XchangeService>()
                .Process("XchangeCreated", JsonConvert.SerializeObject(new { Id = xchangeId }));

        await using (var scope = fixture.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
                .Set<XchangeResult>().AsNoTracking().SingleAsync(r => r.Id == xchangeId);
            var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
            string response = null;
            try { response = await service.GetFile(xchangeId, XchangeFileType.Response); } catch { }
            return (result, response);
        }
    }

    [Fact]
    public async Task A_python_handler_is_listed_with_the_settings_its_code_declares()
    {
        await PublishAsync();
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var rows = JArray.FromObject(await ActivatorUtilities.CreateInstance<Resources.Adapters.Catalog>(scope.ServiceProvider)
            .Handle(new AdapterSearchRequest { Prefix = "handlers" }));
        var row = rows.Single(r => (string)r["Key"] == "infolink6.handlers.pyorders");

        Assert.Equal("python", (string)row["Runtime"]);
        Assert.Equal("1.0.0", (string)row["CurrentVersion"]);
        Assert.Equal("Python orders", (string)row["DisplayName"]);
        Assert.False((bool)row["StartupValues"]!["Partner"]!["Optional"]);
        Assert.True((bool)row["StartupValues"]!["Token"]!["Private"]);
        // Its source came with it, so it can be read in Bitween.
        Assert.True((bool)row["VersionHistory"]![0]!["HasSource"]);
    }

    [Fact]
    public async Task A_subscription_delivers_through_a_python_handler_with_its_settings()
    {
        await PublishAsync();
        var subscription = await CreateSubscription(SubscriptionType.ApiCall, s =>
        {
            s.HandlerId = "infolink6.handlers.pyorders";
            s.HandlerProperties =
            [
                new KeyAndValue { Key = "Partner", Value = "acme" },
                new KeyAndValue { Key = "Token", Value = "secret-123" },
            ];
        });

        var (result, response) = await Run(subscription, "{\"orderId\":\"SO-7\"}");

        Assert.True(result.Success, result.Exception);
        Assert.False(result.ResponseBad);
        var answer = JObject.Parse(response);
        Assert.Equal("acme", (string)answer["to"]);
        Assert.Equal(10, (int)answer["token"]);
        Assert.Equal("SO-7", (string)answer["order"]!["orderId"]);
    }

    [Fact]
    public async Task A_subscription_delivers_through_a_node_handler_with_its_settings()
    {
        await PublishAsync();
        var subscription = await CreateSubscription(SubscriptionType.ApiCall, s =>
        {
            s.HandlerId = "infolink6.handlers.nodeorders";
            s.HandlerProperties =
            [
                new KeyAndValue { Key = "Partner", Value = "globex" },
                new KeyAndValue { Key = "Token", Value = "abc" },
            ];
        });

        var (result, response) = await Run(subscription, "{\"orderId\":\"SO-9\"}");

        Assert.True(result.Success, result.Exception);
        var answer = JObject.Parse(response);
        Assert.Equal("globex", (string)answer["to"]);
        Assert.Equal(3, (int)answer["token"]);
        Assert.Equal("SO-9", (string)answer["order"]!["orderId"]);

        var (rejected, body) = await Run(subscription, "{\"reject\":true}");
        Assert.True(rejected.ResponseBad);
        Assert.Equal("{\"error\":\"rejected\"}", body);
    }

    [Fact]
    public async Task A_rejection_from_a_python_handler_is_a_bad_response_not_a_failure()
    {
        await PublishAsync();
        var subscription = await CreateSubscription(SubscriptionType.ApiCall, s =>
        {
            s.HandlerId = "infolink6.handlers.pyorders";
            s.HandlerProperties = [new KeyAndValue { Key = "Partner", Value = "acme" }, new KeyAndValue { Key = "Token", Value = "t" }];
        });

        var (result, response) = await Run(subscription, "{\"reject\":true}");

        Assert.True(result.ResponseBad);
        Assert.Equal("{\"error\":\"rejected\"}", response);
    }

    /// <summary>A gateway whose subscription is validated by the Python validator, as ValidatorStepTests builds one.</summary>
    async Task<(string UrlName, string Key, int SubscriptionId)> ValidatedGateway()
    {
        var urlName = Unique("pyvalidated").ToLowerInvariant();
        var key = Guid.NewGuid().ToString("N");
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var partner = new Partner(Unique("Python validated partner"));
        partner.SetApiCredentials([new ApiCredential("main", key)]);
        var doc = new Document(null, Unique("Python validated doc"), DocumentFormat.Json);
        db.AddRange(partner, doc);
        await db.SaveChangesAsync();
        var subscription = new Subscription(Unique("Python validated sub"), doc.Id, SubscriptionType.GatewayApiCall)
        {
            Inactive = false,
            ValidatorId = "infolink6.validators.pychecks",
        };
        db.Add(subscription);
        await db.SaveChangesAsync();
        db.Add(new ApiGateway
        {
            Name = Unique("Python validated gateway"),
            UrlName = urlName,
            Partners = [new ApiGatewayPartner { PartnerId = partner.Id, SubscriptionId = subscription.Id }],
        });
        await db.SaveChangesAsync();
        return (urlName, key, subscription.Id);
    }

    async Task<IActionResult> Call(string urlName, string key, string body)
    {
        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInfolinkCache>().Revoke();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Set(
            new ClaimsPrincipal(new ClaimsIdentity()),
            [new RequestValue("partnerkey", key, RequestValueType.HttpHeader)]);
        var controller = ActivatorUtilities.CreateInstance<GatewayController>(scope.ServiceProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return await controller.Post($"{urlName}/async");
    }

    [Fact]
    public async Task A_python_validator_refuses_a_gateway_call_with_its_reasons_and_lets_a_good_one_through()
    {
        await PublishAsync();
        var (urlName, key, subscriptionId) = await ValidatedGateway();

        var refused = await Assert.ThrowsAsync<SWValidationException>(() => Call(urlName, key, "{\"lines\":1}"));
        Assert.Equal("An order needs an id.", refused.Validations.Single(v => v.Key == "orderId").Value);

        Assert.IsType<AcceptedResult>(await Call(urlName, key, "{\"orderId\":\"SO-8\"}"));
        await using var scope = fixture.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().CountAsync(x => x.SubscriptionId == subscriptionId));
    }
}
