using System.IO;
using System.Reflection;

namespace SW.Bitween.Adapters;

/// <summary>The Bitween adapter contract, as the JSON it is written in.</summary>
public static class AdapterContract
{
    /// <summary>The contract version this package describes.</summary>
    public const int Version = 1;

    /// <summary>The kinds of adapter, as adapter manifests name them.</summary>
    public static class Kinds
    {
        /// <summary>Delivers a message: <see cref="IBitweenHandler"/>.</summary>
        public const string Handler = "handler";
        /// <summary>Transforms a message: <see cref="IBitweenMapper"/>.</summary>
        public const string Mapper = "mapper";
        /// <summary>Checks a message: <see cref="IBitweenValidator"/>.</summary>
        public const string Validator = "validator";
        /// <summary>Fetches files on a schedule: <see cref="IBitweenReceiver"/>.</summary>
        public const string Receiver = "receiver";
    }

    /// <summary>The contract: kinds, their methods, payload types and encodings.</summary>
    public static string Json => Read("bitween-adapter-contract.v1.json");

    /// <summary>The JSON Schema of <see cref="ExchangeFile"/>.</summary>
    public static string ExchangeFileSchema => Read("exchange-file.schema.json");

    /// <summary>The JSON Schema of <see cref="ValidationResult"/>.</summary>
    public static string ValidationResultSchema => Read("validation-result.schema.json");

    private static string Read(string name)
    {
        using var stream = typeof(AdapterContract).Assembly
            .GetManifestResourceStream($"SW.Bitween.Adapters.Contract.{name}")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
