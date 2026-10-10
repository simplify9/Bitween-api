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

    /// <summary>Every command and delete that names a record, by the route the API gives it.</summary>
    public static IEnumerable<object[]> KeyedCommands() =>
        Handlers()
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.Namespace == "SW.PrimitiveTypes" && i.Name is "ICommandHandler`3" or "IDeleteHandler`1")
                .Select(i => (Method: i.Name == "IDeleteHandler`1" ? "DELETE" : "POST",
                    Path: NameOf(t) is { } name ? $"/api/{Resource(t)}/999999/{name.ToLowerInvariant()}" : $"/api/{Resource(t)}/999999")))
            .Distinct().OrderBy(c => c.Path).ThenBy(c => c.Method)
            .Select(c => new object[] { c.Method, c.Path });

    /// <summary>
    /// A record that isn't there — deleted in another tab, a stale link — is the caller's mistake to
    /// be told about, not a server error.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeyedCommands))]
    public async Task A_command_on_a_record_that_does_not_exist_is_refused_not_crashed(string method, string path)
    {
        using var admin = await fixture.AdminAsync();
        using var request = new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod(method), path);
        if (method == "POST") request.Content = System.Net.Http.Json.JsonContent.Create(new { });
        var response = await admin.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"{(int)response.StatusCode} {method} {path}");
        Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} {method} {path}: {body[..Math.Min(body.Length, 400)]}");
    }

    /// <summary>Every read of one record, by a key that isn't there.</summary>
    public static IEnumerable<object[]> KeyedReads() =>
        Handlers().Where(t => t.GetInterfaces().Any(i => i.Namespace == "SW.PrimitiveTypes" && i.Name == "IGetHandler`2"))
            .Select(t => NameOf(t) is { } name ? $"/api/{Resource(t)}/999999/{name.ToLowerInvariant()}" : $"/api/{Resource(t)}/999999")
            .Distinct().OrderBy(p => p).Select(p => new object[] { p });

    /// <summary>
    /// Reading a record that isn't there — a stale link, one deleted in another tab — is answered,
    /// not crashed: a subscription that didn't exist was a NullReferenceException and a 500, which
    /// the page could only show as a server error.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeyedReads))]
    public async Task A_read_of_a_record_that_does_not_exist_is_answered_not_crashed(string path)
    {
        using var admin = await fixture.AdminAsync();
        var response = await admin.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"{(int)response.StatusCode} GET {path}");
        Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode} GET {path}: {body[..Math.Min(body.Length, 400)]}");
    }
}
