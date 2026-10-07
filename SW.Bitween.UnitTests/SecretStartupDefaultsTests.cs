using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// An adapter's description goes to the browser to draw its form, and a secret property's default
/// went with it — shown as the field's placeholder. One adapter shipped a production storage key
/// that way.
/// </summary>
[TestClass]
public class SecretStartupDefaultsTests
{
    private static Dictionary<string, StartupValue> Described() => new()
    {
        ["Url"] = new StartupValue { Optional = false },
        ["Verb"] = new StartupValue { Optional = true, Default = "post" },
        ["SecretAccessKey"] = new StartupValue
            { Optional = true, Default = "a-real-key", Private = true, Type = "text", Description = "Storage key" }
    };

    [TestMethod]
    public void A_secret_property_loses_its_default()
    {
        var shown = AdapterStartupValues.WithoutSecretDefaults(Described());

        Assert.IsNull(shown["SecretAccessKey"].Default);
    }

    [TestMethod]
    public void A_secret_property_keeps_everything_else_the_form_needs()
    {
        var secret = AdapterStartupValues.WithoutSecretDefaults(Described())["SecretAccessKey"];

        Assert.IsTrue(secret.Private);
        Assert.IsTrue(secret.Optional);
        Assert.AreEqual("text", secret.Type);
        Assert.AreEqual("Storage key", secret.Description);
    }

    [TestMethod]
    public void An_ordinary_default_is_still_shown()
    {
        var shown = AdapterStartupValues.WithoutSecretDefaults(Described());

        Assert.AreEqual("post", shown["Verb"].Default);
        Assert.AreEqual(3, shown.Count);
    }

    /// <summary>
    /// The native branch of <c>adapters/{id}/properties</c> builds its own labels rather than going
    /// through <see cref="AdapterStartupValues.WithoutSecretDefaults"/>.
    /// </summary>
    [TestMethod]
    public void A_native_secret_default_is_left_out_of_its_label()
    {
        var discovery = new NativeAdapterDiscoveryService([], [], [], [], [new FakeNativeAdapter()], new BitweenOptions());

        var labels = discovery.GetExpectedStartupValues(nameof(FakeNativeAdapter));

        Assert.AreEqual("Token (null)", labels["Token"]);
        Assert.AreEqual("Verb (post)", labels["Verb"]);
    }

    private class FakeNativeAdapter : INativeAdapter
    {
        public string Name => nameof(FakeNativeAdapter);
        public void InitializeStartupValues(IDictionary<string, string> settings) { }
        public Type StartupValuesType => typeof(FakeInput);
    }

    private class FakeInput
    {
        [Secure, DefaultValue("native-secret")]
        public string? Token { get; set; }

        [DefaultValue("post")]
        public string? Verb { get; set; }
    }

    /// <summary>
    /// The description is cached and shared with the callers that mask and validate, so the copy
    /// handed out must not reach back into it.
    /// </summary>
    [TestMethod]
    public void The_description_it_was_given_is_left_alone()
    {
        var described = Described();

        AdapterStartupValues.WithoutSecretDefaults(described);

        Assert.AreEqual("a-real-key", described["SecretAccessKey"].Default);
    }
}
