using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Infolink.Domain;
using SW.Infolink.Model;

namespace SW.Infolink.UnitTests;

[TestClass]
public class TestSubscriptionExtensions
{
    
    
    [TestMethod]
    public void TestBackwardCompatibility()
    {
        var sampleDocument = new Document(1, "test", DocumentFormat.Json);
        var sampleSubscription = new Subscription("test", 1, SubscriptionType.Internal, 1);
        sampleDocument.SetDictionaries(new Dictionary<string, string>(){ { "name", "name"},{"country","address.country"}});
        sampleSubscription.SetDictionaries(new Dictionary<string, string>(){},
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>()
            {
                {"name","samer"},
                {"country","Jordan"}
            },
            new Dictionary<string, string>());

        var matchExpression = sampleSubscription.BackwardCompatibleMatchExpression(sampleDocument);
        Console.WriteLine(matchExpression.ToString());
        Assert.AreEqual("(name is one of [samer]) AND (address.country is one of [Jordan])",matchExpression.ToString());

    }
    
    [TestMethod]
    public void TestBackwardCompatibilityWhenNull()
    {
        var sampleDocument = new Document(1, "test", DocumentFormat.Json);
        var sampleSubscription = new Subscription("test", 1, SubscriptionType.Internal, 1);
        sampleDocument.SetDictionaries(new Dictionary<string, string>(){ { "name", "name"},{"country","address.country"}});

        var matchExpression = sampleSubscription.BackwardCompatibleMatchExpression(sampleDocument);
        Assert.AreEqual(null,matchExpression);

    }
}