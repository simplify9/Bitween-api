using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Every screen's read, opened by an administrator over HTTP on a running install: lists, and the
/// named views (operations, dashboard, audit, run history). Any of them failing is a page that
/// shows an error instead of its data.
/// </summary>
[Collection("Http")]
public class ReadTests(HttpFixture fixture, ITestOutputHelper output)
{
    static IEnumerable<Type> Handlers() =>
        typeof(BitweenDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        t.Namespace?.StartsWith("SW.Bitween.Resources.") == true);

    static bool Implements(Type t, string name) => t.GetInterfaces().Any(i => i.Namespace == "SW.PrimitiveTypes" && i.Name.StartsWith(name));

    static string? NameOf(Type t)
    {
        var attribute = t.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "HandlerNameAttribute");
        return (string?)attribute?.GetType().GetProperty("Name")?.GetValue(attribute);
    }

    static string Resource(Type t) => t.Namespace!.Split('.').Last().ToLowerInvariant();

    /// <summary>The paths an admin's pages read: each resource's list, and each named query with no key.</summary>
    public static IEnumerable<object[]> Reads() =>
        Handlers().Where(t => Implements(t, "ISearchyHandler")).Select(t => $"/api/{Resource(t)}")
            .Concat(Handlers()
                .Where(t => t.GetInterfaces().Any(i => i.Namespace == "SW.PrimitiveTypes" && i.Name == "IQueryHandler`1")
                            && NameOf(t) is not null)
                .Select(t => $"/api/{Resource(t)}/{NameOf(t)!.ToLowerInvariant()}"))
            .Distinct().OrderBy(p => p).Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task An_administrator_s_read_answers_without_a_server_error(string path)
    {
        using var admin = await fixture.AdminAsync();
        var response = await admin.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"{(int)response.StatusCode} {path}");
        Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} {path}: {body[..Math.Min(body.Length, 600)]}");
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {path}: {body[..Math.Min(body.Length, 600)]}");
    }

    /// <summary>Named queries that take a request, opened with none: a bad request is a fair answer, a crash is not.</summary>
    public static IEnumerable<object[]> ParameterisedReads() =>
        Handlers()
            .Where(t => t.GetInterfaces().Any(i => i.Namespace == "SW.PrimitiveTypes" && i.Name == "IQueryHandler`2")
                        && NameOf(t) is not null)
            .Select(t => $"/api/{Resource(t)}/{NameOf(t)!.ToLowerInvariant()}")
            .Distinct().OrderBy(p => p).Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(ParameterisedReads))]
    public async Task A_read_asked_without_its_parameters_is_refused_not_crashed(string path)
    {
        using var admin = await fixture.AdminAsync();
        var response = await admin.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"{(int)response.StatusCode} {path}");
        Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} {path}: {body[..Math.Min(body.Length, 600)]}");
    }
}
