using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using SW.Bitween.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The Bitween adapter contract, and the two .NET families that implement it: adapters built on
/// SW.PrimitiveTypes (IInfolinkHandler and friends) and on SW.Bitween.Adapters (IBitweenHandler and
/// friends). Bitween calls both by method name and reads their JSON into SW.PrimitiveTypes' types,
/// so the method names must be the contract's and the JSON must be identical — and JSON written by
/// an adapter in any other language must read the same way.
/// </summary>
[TestClass]
public class AdapterContractTests
{
    static JObject Contract => JObject.Parse(AdapterContract.Json);

    static string Json(object value) => JsonConvert.SerializeObject(value);

    [TestMethod]
    public void An_exchange_file_is_the_same_JSON_whichever_family_wrote_it()
    {
        var old = new XchangeFile("{\"order\":1}", "order.json", badData: true) { ContentType = "application/json" };
        var current = new ExchangeFile("{\"order\":1}", "order.json", badData: true) { ContentType = "application/json" };

        Assert.IsTrue(JToken.DeepEquals(JToken.Parse(Json(old)), JToken.Parse(Json(current))),
            $"old: {Json(old)}\nnew: {Json(current)}");
    }

    [TestMethod]
    public void An_exchange_file_reads_back_into_either_family()
    {
        var fromNew = JsonConvert.DeserializeObject<XchangeFile>(Json(new ExchangeFile("payload", "a.txt", true)))!;
        Assert.AreEqual("payload", fromNew.Data);
        Assert.AreEqual("a.txt", fromNew.Filename);
        Assert.IsTrue(fromNew.BadData);

        var fromOld = JsonConvert.DeserializeObject<ExchangeFile>(Json(new XchangeFile("payload", "a.txt", true)))!;
        Assert.AreEqual("payload", fromOld.Data);
        Assert.AreEqual("a.txt", fromOld.Filename);
        Assert.IsTrue(fromOld.BadData);
        Assert.AreEqual(new XchangeFile("payload").Hash, fromOld.Hash, "the hash is computed the same way");
    }

    /// <summary>What a Python, Node or Go adapter writes: no Hash, nulls, fields Bitween doesn't know.</summary>
    [DataTestMethod]
    [DataRow("{\"Data\":\"x\"}", "x", null, false)]
    [DataRow("{\"Data\":\"x\",\"Filename\":null,\"BadData\":false,\"ContentType\":null}", "x", null, false)]
    [DataRow("{\"Data\":\"rejected\",\"Filename\":\"r.json\",\"BadData\":true,\"Unknown\":{\"a\":1}}", "rejected", "r.json", true)]
    [DataRow("{\"Data\":\"x\",\"Hash\":\"not-the-real-hash\"}", "x", null, false)]
    public void Another_language_s_exchange_file_is_read_by_Bitween_as_it_means(string json, string data, string filename, bool badData)
    {
        var read = JsonConvert.DeserializeObject<XchangeFile>(json)!;

        Assert.AreEqual(data, read.Data);
        Assert.AreEqual(filename, read.Filename);
        Assert.AreEqual(badData, read.BadData);
        Assert.AreEqual(new XchangeFile(data).Hash, read.Hash, "a hash sent in is recomputed, never trusted");
    }

    [TestMethod]
    public void A_validation_result_is_the_same_JSON_whichever_family_wrote_it()
    {
        var old = new InfolinkValidatorResult();
        old.AddError("Id", "Must be under 10");
        old.AddError("Name", "Required");
        var current = new ValidationResult().AddError("Id", "Must be under 10").AddError("Name", "Required");

        Assert.IsTrue(JToken.DeepEquals(JToken.Parse(Json(old)), JToken.Parse(Json(current))),
            $"old: {Json(old)}\nnew: {Json(current)}");
        Assert.IsTrue(JToken.DeepEquals(JToken.Parse(Json(new InfolinkValidatorResult())), JToken.Parse(Json(new ValidationResult()))));
    }

    [TestMethod]
    public void A_validation_result_reads_back_into_either_family()
    {
        var fromNew = JsonConvert.DeserializeObject<InfolinkValidatorResult>(Json(new ValidationResult().AddError("Id", "Bad")))!;
        Assert.IsFalse(fromNew.Success);
        Assert.AreEqual("Id", fromNew.Validations.Single().Key);

        var old = new InfolinkValidatorResult();
        old.AddError("Id", "Bad");
        var fromOld = JsonConvert.DeserializeObject<ValidationResult>(Json(old))!;
        Assert.IsFalse(fromOld.Success);
        Assert.IsTrue(fromOld.Has("Id"));

        // Another language may leave Success out; Bitween derives it from the failures.
        Assert.IsTrue(JsonConvert.DeserializeObject<InfolinkValidatorResult>("{\"Validations\":[]}")!.Success);
        Assert.IsFalse(JsonConvert.DeserializeObject<InfolinkValidatorResult>(
            "{\"Validations\":[{\"Key\":\"Id\",\"Value\":\"Bad\"}]}")!.Success);
    }

    [TestMethod]
    public async Task What_both_families_write_satisfies_the_published_schemas()
    {
        var fileSchema = await JsonSchema.FromJsonAsync(AdapterContract.ExchangeFileSchema);
        var resultSchema = await JsonSchema.FromJsonAsync(AdapterContract.ValidationResultSchema);
        var old = new InfolinkValidatorResult();
        old.AddError("Id", "Bad");

        foreach (var json in new[] { Json(new XchangeFile("x", "f")), Json(new ExchangeFile("x", "f")), "{\"Data\":\"x\"}" })
            Assert.AreEqual(0, fileSchema.Validate(json).Count, json);
        foreach (var json in new[] { Json(old), Json(new ValidationResult().AddError("Id", "Bad")), "{\"Validations\":[]}" })
            Assert.AreEqual(0, resultSchema.Validate(json).Count, json);

        Assert.AreNotEqual(0, fileSchema.Validate("{\"Filename\":\"no data\"}").Count, "a file without Data is not a file");
        Assert.AreNotEqual(0, resultSchema.Validate("{\"Validations\":[{\"Key\":\"Id\"}]}").Count, "a failure needs its message");
    }

    /// <summary>Bitween calls by method name: each kind's names in the contract are the methods both families declare.</summary>
    [DataTestMethod]
    [DataRow("handler", typeof(IBitweenHandler), typeof(IInfolinkHandler))]
    [DataRow("mapper", typeof(IBitweenMapper), typeof(IInfolinkHandler))]
    [DataRow("validator", typeof(IBitweenValidator), typeof(IInfolinkValidator))]
    [DataRow("receiver", typeof(IBitweenReceiver), typeof(IInfolinkReceiver))]
    public void Each_kind_s_methods_are_the_contract_s_in_both_families(string kind, System.Type current, System.Type old)
    {
        var names = Contract["kinds"]![kind]!["methods"]!.Select(m => (string)m["name"]!).OrderBy(n => n).ToList();

        CollectionAssert.AreEqual(names, current.GetMethods().Select(m => m.Name).OrderBy(n => n).ToList(), current.Name);
        CollectionAssert.AreEqual(names, old.GetMethods().Select(m => m.Name).OrderBy(n => n).ToList(), old.Name);

        foreach (var method in Contract["kinds"]![kind]!["methods"]!)
        {
            var name = (string)method["name"]!;
            Assert.AreEqual(Shape(current.GetMethod(name)!), Shape(old.GetMethod(name)!), $"{kind}.{name} differs between the families");
            Assert.AreEqual(ContractShape(method), Shape(current.GetMethod(name)!), $"{kind}.{name} differs from the contract");
        }
    }

    /// <summary>A method's argument and result, in the contract's words.</summary>
    static string Shape(MethodInfo method)
    {
        static string Of(System.Type? type) => type switch
        {
            null => "none",
            _ when type == typeof(Task) => "none",
            _ when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) => Of(type.GetGenericArguments()[0]),
            _ when type == typeof(XchangeFile) || type == typeof(ExchangeFile) => "ExchangeFile",
            _ when type == typeof(InfolinkValidatorResult) || type == typeof(ValidationResult) => "ValidationResult",
            _ when type == typeof(string) => "FileId",
            _ when type == typeof(IEnumerable<string>) => "FileIdList",
            _ => type.Name,
        };
        return $"{Of(method.GetParameters().SingleOrDefault()?.ParameterType)} -> {Of(method.ReturnType)}";
    }

    static string ContractShape(JToken method) =>
        $"{(string?)method["input"] ?? "none"} -> {(string?)method["output"] ?? "none"}";
}
