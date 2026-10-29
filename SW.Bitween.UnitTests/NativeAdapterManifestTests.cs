using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.Bitween.Services.Adapters;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Every native adapter carries a manifest, and the manifest says what its code says. The settings a
/// native adapter is configured with come from its startup-values type at run time; a manifest that
/// listed different ones would show a marketplace something the adapter does not do.
/// </summary>
[TestClass]
public class NativeAdapterManifestTests
{
    static readonly Type[] NativeTypes = typeof(INativeAdapter).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(INativeAdapter).IsAssignableFrom(t))
        .ToArray();

    static readonly Dictionary<string, AdapterManifest> Manifests =
        NativeAdapterManifests.Read(typeof(INativeAdapter).Assembly)
            .ToDictionary(m => m.Id, m => m.Manifest, StringComparer.OrdinalIgnoreCase);

    [TestMethod]
    public void Every_native_adapter_has_a_valid_manifest_named_after_it()
    {
        foreach (var type in NativeTypes)
        {
            Assert.IsTrue(Manifests.TryGetValue(type.Name, out var manifest), $"{type.Name} has no adapter.json");
            Assert.AreEqual(type.Name.ToLowerInvariant(), manifest.Id);
            Assert.AreEqual(0, manifest.Validate().Count, $"{type.Name}: {string.Join("; ", manifest.Validate())}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(manifest.DisplayName), $"{type.Name} has no display name");
            Assert.IsFalse(string.IsNullOrWhiteSpace(manifest.Summary), $"{type.Name} has no summary");
            Assert.AreEqual(1, manifest.Kinds.Count, $"{type.Name} should declare its one kind");
        }
    }

    [TestMethod]
    public void A_manifest_lists_exactly_the_settings_its_adapter_reads()
    {
        foreach (var type in NativeTypes)
        {
            var manifest = Manifests[type.Name];
            var startupType = (Type)type.GetProperty(nameof(INativeAdapter.StartupValuesType))!
                .GetValue(RuntimeHelpers.GetUninitializedObject(type));
            var defaults = Activator.CreateInstance(startupType);

            var fromCode = startupType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(p => p.Name, p =>
                {
                    var hasDefault = p.GetValue(defaults) != null;
                    var nullable = !p.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) != null;
                    var required = p.GetCustomAttribute<RequiredAttribute>() != null || (!nullable && !hasDefault);
                    return (Required: required, Secret: p.GetCustomAttribute<SecureAttribute>() != null);
                }, StringComparer.OrdinalIgnoreCase);

            CollectionAssert.AreEquivalent(
                fromCode.Keys.Select(k => k.ToLowerInvariant()).ToList(),
                manifest.Properties.Select(p => p.Name.ToLowerInvariant()).ToList(),
                $"{type.Name}: the manifest's properties differ from {startupType.Name}'s");

            foreach (var property in manifest.Properties)
            {
                var code = fromCode[property.Name];
                Assert.AreEqual(code.Secret, property.Secret, $"{type.Name}.{property.Name}: secret differs from the code");
                Assert.AreEqual(code.Required, property.Required, $"{type.Name}.{property.Name}: required differs from the code");
                if (property.Secret)
                    Assert.IsNull(property.Default, $"{type.Name}.{property.Name}: a secret's default must not be published");
            }
        }
    }
}
