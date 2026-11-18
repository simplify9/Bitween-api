using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Client;
using SW.Bitween.Model;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The client's information type calls: which request each one makes, and what it makes of the answer.
/// What Bitween does with the request (the name, code and bus name rules) is InformationTypeTests'.
/// </summary>
[TestClass]
public class InformationTypesClientTests
{
    /// <summary>Answers the sign-in, then whatever <paramref name="answer"/> says, and keeps what it was asked.</summary>
    sealed class Stub(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellation);
            if (request.RequestUri!.AbsolutePath.EndsWith("api/accounts/servicetoken"))
                return Json("""{"jwt":"session","expiresIn":3600}""");
            Calls.Add((request.Method, request.RequestUri.PathAndQuery, body));
            return answer(request, body);
        }
    }

    static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    static BitweenClient ClientFor(Stub stub) =>
        new(new HttpClient(stub) { BaseAddress = new Uri("https://bitween.example.com/") }, "bwt_test");

    [TestMethod]
    public async Task Create_posts_the_type_to_documents_and_answers_its_id()
    {
        var stub = new Stub((_, _) => Json("42"));
        using var bitween = ClientFor(stub);

        var id = await bitween.InformationTypes.CreateAsync(new DocumentCreate
        {
            Code = "INVOICE_REQUEST", Name = "Invoice request", BusEnabled = true, BusMessageTypeName = "InvoiceRequested",
        });

        Assert.AreEqual(42, id);
        var call = stub.Calls.Single();
        Assert.AreEqual(HttpMethod.Post, call.Method);
        Assert.AreEqual("/api/documents", call.Path);
        var sent = JsonDocument.Parse(call.Body!).RootElement;
        Assert.AreEqual("INVOICE_REQUEST", sent.GetProperty("code").GetString());
        Assert.AreEqual("InvoiceRequested", sent.GetProperty("busMessageTypeName").GetString());
        Assert.IsTrue(sent.GetProperty("busEnabled").GetBoolean());
    }

    [TestMethod]
    public async Task Update_posts_to_the_types_own_address_with_its_id_in_the_body()
    {
        var stub = new Stub((_, _) => Json("null"));
        using var bitween = ClientFor(stub);

        await bitween.InformationTypes.UpdateAsync(7, new DocumentUpdate { Name = "Renamed", Code = "RENAMED" });

        var call = stub.Calls.Single();
        Assert.AreEqual(HttpMethod.Post, call.Method);
        Assert.AreEqual("/api/documents/7", call.Path);
        Assert.AreEqual(7, JsonDocument.Parse(call.Body!).RootElement.GetProperty("id").GetInt32(),
            "The server compares the body's id with the address's.");
    }

    [TestMethod]
    public async Task Retire_and_delete_use_the_types_address()
    {
        var stub = new Stub((_, _) => Json("null"));
        using var bitween = ClientFor(stub);

        await bitween.InformationTypes.RetireAsync(7);
        await bitween.InformationTypes.DeleteAsync(7);

        Assert.AreEqual((HttpMethod.Post, "/api/documents/7/retire"), (stub.Calls[0].Method, stub.Calls[0].Path));
        Assert.AreEqual((HttpMethod.Delete, "/api/documents/7"), (stub.Calls[1].Method, stub.Calls[1].Path));
    }

    [TestMethod]
    public async Task A_refused_create_says_why_and_with_which_code()
    {
        var stub = new Stub((_, _) => Json("""{"NAME_TAKEN":["An information type with this name already exists."]}""", HttpStatusCode.BadRequest));
        using var bitween = ClientFor(stub);

        var refused = await Assert.ThrowsExceptionAsync<BitweenClientException>(() =>
            bitween.InformationTypes.CreateAsync(new DocumentCreate { Name = "Invoice request" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, refused.Status);
        Assert.AreEqual("NAME_TAKEN", refused.Code);
    }

    [TestMethod]
    public async Task Find_by_code_asks_for_that_code_only_and_checks_it_exactly()
    {
        var stub = new Stub((_, _) => Json("""{"result":[{"id":3,"code":"INVOICE_REQUEST","name":"Invoice request"}],"total":1}"""));
        using var bitween = ClientFor(stub);

        var found = await bitween.InformationTypes.FindByCodeAsync("INVOICE_REQUEST");
        var other = await bitween.InformationTypes.FindByCodeAsync("INVOICE_REQUES");

        Assert.AreEqual(3, found!.Id);
        StringAssert.Contains(Uri.UnescapeDataString(stub.Calls[0].Path), "Code:1:INVOICE_REQUEST");
        Assert.IsNull(other, "A row Bitween answers with that isn't this code isn't a match.");
    }

    [TestMethod]
    public async Task Find_by_name_ignores_case_and_wants_the_whole_name()
    {
        var stub = new Stub((_, _) => Json("""{"result":[{"id":1,"name":"Invoice request archive"},{"id":2,"name":"INVOICE REQUEST"}],"total":2}"""));
        using var bitween = ClientFor(stub);

        var found = await bitween.InformationTypes.FindByNameAsync("Invoice request");
        var none = await bitween.InformationTypes.FindByNameAsync("Invoice");

        Assert.AreEqual(2, found!.Id, "Bitween holds one type per name in any case; a longer name isn't it.");
        Assert.IsNull(none);
    }
}
