using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SW.Serverless.Contract.Catalog;
using SW.Serverless.Tooling.Building;
using SW.Serverless.Tooling.Conformance;
using SW.Serverless.Tooling.Scaffolding;

namespace SW.Bitween.Adapters.Tooling;

/// <summary>
/// What SW-Serverless's tooling needs to know about Bitween's adapters, and nothing else does: the
/// Bitween contract, Bitween's Python and Node packages to vendor into a build, and the templates
/// for its four kinds. The bitween CLI and the adapter editor both start here.
/// </summary>
public static class BitweenAdapters
{
    public const string ContractName = "bitween";
    public const int ContractVersion = 1;

    /// <summary>The kinds of adapter Bitween runs.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["handler", "mapper", "validator", "receiver"];

    /// <summary>The .NET contract package the templates reference: the first Bitween release that published it.</summary>
    public const string BitweenAdaptersPackageVersion = "10.0.59";

    static readonly Assembly Assembly = typeof(BitweenAdapters).Assembly;

    static string Text(string resource)
    {
        using var stream = Assembly.GetManifestResourceStream(resource) ?? throw new FileNotFoundException(resource);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The Bitween adapter contract, as the conformance kit reads it.</summary>
    public static ContractDocument Contract { get; } = ContractDocument.FromJson(
        Text($"contract/bitween-adapter-contract.v{ContractVersion}.json"), file => Text("contract/" + file));

    /// <summary>Makes the kit check every adapter that declares the Bitween contract, without being handed it.</summary>
    public static void RegisterContract() => ContractDocument.Register(Contract);

    /// <summary>
    /// Bitween's kinds for Python (simplyworks-bitween) and Node (@simplyworks/bitween), for a build to
    /// vendor beside the SDK.
    /// </summary>
    public static IList<VendoredPackage> Packages()
    {
        var python = new VendoredPackage { Runtime = AdapterManifest.PythonRuntime, Name = "simplyworks-bitween" };
        var node = new VendoredPackage { Runtime = AdapterManifest.NodeRuntime, Name = "@simplyworks/bitween" };
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            var normalized = name.Replace('\\', '/');
            using var stream = Assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (normalized.StartsWith("python/", StringComparison.Ordinal)) python.Files[normalized["python/".Length..]] = buffer.ToArray();
            else if (normalized.StartsWith("node/", StringComparison.Ordinal)) node.Files[normalized["node/".Length..]] = buffer.ToArray();
        }
        return [python, node];
    }

    /// <summary>A build request for a Bitween adapter: Bitween's packages are vendored.</summary>
    public static BuildRequest BuildRequest(string projectDirectory, string outputDirectory = null, Action<string> log = null)
    {
        var request = new BuildRequest { ProjectDirectory = projectDirectory, OutputDirectory = outputDirectory, Packages = Packages() };
        if (log != null) request.Log = log;
        return request;
    }

    /// <summary>Writes a new Bitween adapter of <paramref name="kind"/>, in any of the SDK's languages.</summary>
    public static ScaffoldResult Scaffold(ScaffoldRequest request)
    {
        if (!Kinds.Contains(request.Kind ?? ""))
        {
            var refused = new ScaffoldResult();
            refused.Problems.Add($"'{request.Kind}' isn't a Bitween kind: use {string.Join(", ", Kinds)}");
            return refused;
        }
        return Scaffolder.Scaffold(request, Templates);
    }

    /// <summary>The templates, for whoever scaffolds through <see cref="Scaffolder"/> itself.</summary>
    public static IEnumerable<(string File, string Content)> Templates(string name, string id, string language, string kind) =>
        language switch
        {
            "python" => PythonFiles(name, id, kind),
            "node" => NodeFiles(name, id, kind, typeScript: false),
            "typescript" => NodeFiles(name, id, kind, typeScript: true),
            _ => DotnetFiles(name, id, kind),
        };

        static IEnumerable<(string File, string Content)> DotnetFiles(string name, string id, string kind)
        {
            yield return ($"{name}.csproj", $$"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <!-- Locked, so the source this package carries rebuilds with exactly these dependencies. -->
                    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                  </PropertyGroup>

                  <ItemGroup>
                    <PackageReference Include="SimplyWorks.Serverless.Sdk" Version="{{Scaffolder.SdkPackageVersion}}" />
                    <PackageReference Include="SimplyWorks.Bitween.Adapters" Version="{{BitweenAdaptersPackageVersion}}" />
                  </ItemGroup>

                </Project>
                """);

            yield return ("adapter.json", $$"""
                {
                  "id": "{{id}}",
                  "version": "0.1.0",
                  "displayName": "{{Scaffolder.Spaced(name)}}",
                  "summary": "What this {{kind}} does, in one sentence, for the adapter list.",
                  "lifecycle": "classic"
                }
                """);

            yield return ("Program.cs", Program(name, kind));

            yield return ("settings.example.json", """
                {
                  "BaseUrl": "https://partner.example.test",
                  "ApiKey": "put a test key here, and keep this file out of version control once it holds one"
                }
                """);

            yield return (".gitignore", """
                bin/
                obj/
                settings.json
                """);

            yield return ("README.md", $$"""
                # {{Scaffolder.Spaced(name)}}

                A Bitween {{kind}} adapter.

                ```sh
                bitween adapter build                                   # builds bin/serverless/{{id}}-0.1.0.zip
                cp settings.example.json settings.json             # then fill in real values
                bitween adapter test --settings settings.json           # checks it against the Bitween contract
                bitween adapter publish bin/serverless/{{id}}-0.1.0.zip  # to the Bitween you signed in to
                ```

                Settings are declared in code with `Runner.Expect`; `bitween adapter build` writes them into
                the manifest Bitween reads.
                """);
        }

        static string Program(string name, string kind) => kind switch
        {
            "receiver" => $$"""
                using SW.Bitween.Adapters;
                using SW.Serverless.Sdk;

                namespace {{name}};

                /// <summary>Fetches files on a schedule. Bitween calls Initialize, ListFiles, then GetFile and DeleteFile for each file, then Finalize.</summary>
                [AdapterKind("receiver")]
                [AdapterContract("bitween", 1)]
                public class Receiver : IBitweenReceiver
                {
                    public Receiver()
                    {
                        // Declare settings here; read them in the methods, never in the constructor.
                        Runner.Expect("BaseUrl", "https://partner.example.test", description: "Where files are fetched from.");
                        Runner.Expect("ApiKey", isPrivate: true, description: "The partner's key.");
                    }

                    public Task Initialize() => Task.CompletedTask;

                    public Task<IEnumerable<string>> ListFiles() =>
                        Task.FromResult<IEnumerable<string>>(new[] { "example-1" });

                    public Task<ExchangeFile> GetFile(string fileId) =>
                        Task.FromResult(new ExchangeFile("{\"id\":\"" + fileId + "\"}", fileId + ".json"));

                    public Task DeleteFile(string fileId) => Task.CompletedTask;

                    public Task Finalize() => Task.CompletedTask;
                }

                static class Program
                {
                    static Task Main() => Runner.Run(new Receiver());
                }
                """,
            "validator" => $$"""
                using SW.Bitween.Adapters;
                using SW.Serverless.Sdk;

                namespace {{name}};

                /// <summary>Checks a message before Bitween accepts it.</summary>
                [AdapterKind("validator")]
                [AdapterContract("bitween", 1)]
                public class Validator : IBitweenValidator
                {
                    public Validator()
                    {
                        Runner.Expect("MaxBytes", "1000000", description: "The largest message accepted.");
                    }

                    public Task<ValidationResult> Validate(ExchangeFile file)
                    {
                        var result = new ValidationResult();
                        if (file.Data.Length > Runner.StartupValueOf<int>("MaxBytes"))
                            result.AddError("Data", "The message is larger than allowed.");
                        return Task.FromResult(result);
                    }
                }

                static class Program
                {
                    static Task Main() => Runner.Run(new Validator());
                }
                """,
            var handlerOrMapper => $$"""
                using SW.Bitween.Adapters;
                using SW.Serverless.Sdk;

                namespace {{name}};

                /// <summary>{{(handlerOrMapper == "mapper" ? "Maps a message into the shape the next step expects." : "Delivers a message and returns the partner's response.")}}</summary>
                [AdapterKind("{{handlerOrMapper}}")]
                [AdapterContract("bitween", 1)]
                public class {{(handlerOrMapper == "mapper" ? "Mapper : IBitweenMapper" : "Handler : IBitweenHandler")}}
                {
                    public {{(handlerOrMapper == "mapper" ? "Mapper" : "Handler")}}()
                    {
                        // Declare settings here; read them in the methods, never in the constructor.
                        Runner.Expect("BaseUrl", "https://partner.example.test", description: "Where messages go.");
                        Runner.Expect("ApiKey", isPrivate: true, description: "The partner's key.");
                    }

                    public Task<ExchangeFile> Handle(ExchangeFile file)
                    {
                        // {{(handlerOrMapper == "mapper" ? "Return the message in its new shape." : "Send file.Data to the partner. A rejection is returned with BadData set, not thrown.")}}
                        return Task.FromResult(new ExchangeFile(file.Data, file.Filename));
                    }
                }

                static class Program
                {
                    static Task Main() => Runner.Run(new {{(handlerOrMapper == "mapper" ? "Mapper" : "Handler")}}());
                }
                """,
        };

        static IEnumerable<(string File, string Content)> PythonFiles(string name, string id, string kind)
        {
            yield return ("adapter.json", $$"""
                {
                  "id": "{{id}}",
                  "version": "0.1.0",
                  "displayName": "{{Scaffolder.Spaced(name)}}",
                  "summary": "What this {{kind}} does, in one sentence, for the adapter list.",
                  "runtime": "python",
                  "entry": "main.py"
                }
                """);

            yield return ("main.py", PythonMain(name, kind));

            yield return ("requirements.txt", $$"""
                # What the adapter imports, pinned, one per line; bitween adapter build vendors them into the
                # package. The two SDKs are vendored by bitween adapter build itself: they're listed here for
                # your editor and for running the tests outside a build.
                sw-serverless=={{Scaffolder.PythonSdkVersion}}
                simplyworks-bitween>={{BitweenAdaptersPackageVersion}}
                """);

            yield return ("settings.example.json", """
                {
                  "BaseUrl": "https://partner.example.test",
                  "ApiKey": "put a test key here, and keep this file out of version control once it holds one"
                }
                """);

            yield return (".gitignore", """
                __pycache__/
                .venv/
                bin/
                settings.json
                """);

            yield return ("README.md", $$"""
                # {{Scaffolder.Spaced(name)}}

                A Bitween {{kind}} adapter in Python (3.12 or later).

                ```sh
                bitween adapter build                                   # builds bin/serverless/{{id}}-0.1.0.zip
                cp settings.example.json settings.json             # then fill in real values
                bitween adapter test --settings settings.json           # checks it against the Bitween contract
                bitween adapter publish bin/serverless/{{id}}-0.1.0.zip  # to the Bitween you signed in to
                ```

                Settings are declared in code with `sw.expect`; `bitween adapter build` writes them into the
                manifest Bitween reads. Dependencies go in `requirements.txt`, pinned.
                """);
        }

        static string PythonMain(string name, string kind) => kind switch
        {
            "receiver" => $$""""
                import sw_serverless as sw
                from simplyworks_bitween import ExchangeFile, Receiver


                class {{name}}(Receiver):
                    """Fetches files on a schedule. Bitween calls initialize, list_files, then get_file and
                    delete_file for each file, then finalize."""

                    def __init__(self):
                        # Declare settings here; read them in the methods with sw.value_of.
                        sw.expect("BaseUrl", "https://partner.example.test", description="Where files are fetched from.")
                        sw.expect("ApiKey", secret=True, description="The partner's key.")

                    def list_files(self) -> list[str]:
                        return ["example-1"]

                    def get_file(self, file_id: str) -> ExchangeFile:
                        return ExchangeFile(data='{"id": "%s"}' % file_id, filename=file_id + ".json")

                    def delete_file(self, file_id: str) -> None:
                        pass


                if __name__ == "__main__":
                    sw.run({{name}})
                """",
            "validator" => $$""""
                import sw_serverless as sw
                from simplyworks_bitween import ExchangeFile, ValidationResult, Validator


                class {{name}}(Validator):
                    """Checks a message before Bitween accepts it."""

                    def __init__(self):
                        sw.expect("MaxBytes", "1000000", type="number", description="The largest message accepted.")

                    def validate(self, file: ExchangeFile) -> ValidationResult:
                        result = ValidationResult()
                        if len(file.data) > int(sw.value_of("MaxBytes")):
                            result.add("Data", "The message is larger than allowed.")
                        return result


                if __name__ == "__main__":
                    sw.run({{name}})
                """",
            "mapper" => $$""""
                import sw_serverless as sw
                from simplyworks_bitween import ExchangeFile, Mapper


                class {{name}}(Mapper):
                    """Maps a message into the shape the next step expects."""

                    def map(self, file: ExchangeFile) -> ExchangeFile:
                        # Return the message in its new shape.
                        return ExchangeFile(data=file.data, filename=file.filename)


                if __name__ == "__main__":
                    sw.run({{name}})
                """",
            _ => $$""""
                import sw_serverless as sw
                from simplyworks_bitween import ExchangeFile, Handler


                class {{name}}(Handler):
                    """Delivers a message and returns the partner's response."""

                    def __init__(self):
                        # Declare settings here; read them in the methods with sw.value_of.
                        sw.expect("BaseUrl", "https://partner.example.test", description="Where messages go.")
                        sw.expect("ApiKey", secret=True, description="The partner's key.")

                    def handle(self, file: ExchangeFile) -> ExchangeFile:
                        # Send file.data to the partner. A rejection is returned with bad_data=True, not raised.
                        return ExchangeFile(data=file.data, filename=file.filename)


                if __name__ == "__main__":
                    sw.run({{name}})
                """",
        };

        static IEnumerable<(string File, string Content)> NodeFiles(string name, string id, string kind, bool typeScript)
        {
            var entry = typeScript ? "main.ts" : "main.js";
            yield return ("adapter.json", $$"""
                {
                  "id": "{{id}}",
                  "version": "0.1.0",
                  "displayName": "{{Scaffolder.Spaced(name)}}",
                  "summary": "What this {{kind}} does, in one sentence, for the adapter list.",
                  "runtime": "node",
                  "entry": "{{entry}}"
                }
                """);

            yield return (entry, NodeMain(name, kind, typeScript));

            // The SDKs are named for the editor and for running outside a build; bitween adapter build
            // vendors the copies it carries. Other dependencies go here too, and are installed into
            // the package.
            yield return ("package.json", Scaffolder.NodePackageJson(id, typeScript,
                new Dictionary<string, string> { ["@simplyworks/bitween"] = ">=" + BitweenAdaptersPackageVersion }));

            if (typeScript)
                yield return ("tsconfig.json", Scaffolder.TsConfig);

            yield return ("settings.example.json", """
                {
                  "BaseUrl": "https://partner.example.test",
                  "ApiKey": "put a test key here, and keep this file out of version control once it holds one"
                }
                """);

            yield return (".gitignore", """
                node_modules/
                bin/
                settings.json
                """);

            yield return ("README.md", $$"""
                # {{Scaffolder.Spaced(name)}}

                A Bitween {{kind}} adapter in {{(typeScript ? "TypeScript" : "JavaScript")}}, for Node 22 or later.

                ```sh
                bitween adapter build                                   # builds bin/serverless/{{id}}-0.1.0.zip
                cp settings.example.json settings.json             # then fill in real values
                bitween adapter test --settings settings.json           # checks it against the Bitween contract
                bitween adapter publish bin/serverless/{{id}}-0.1.0.zip  # to the Bitween you signed in to
                ```

                Settings are declared in code with `expect`; `bitween adapter build` writes them into the
                manifest Bitween reads.{{(typeScript ? " The build strips the types with Node itself, so no compiler is needed; `tsc --noEmit` checks them." : "")}}
                """);
        }

        static string NodeMain(string name, string kind, bool ts)
        {
            var header = ts
                ? $$"""
                    import { expect, run, valueOf } from "@simplyworks/sw-serverless";
                    import { ExchangeFile, {{Pascal(kind)}}{{(kind == "validator" ? ", ValidationResult" : "")}} } from "@simplyworks/bitween";
                    """
                : $$"""
                    const { expect, run, valueOf } = require("@simplyworks/sw-serverless");
                    const { ExchangeFile, {{Pascal(kind)}}{{(kind == "validator" ? ", ValidationResult" : "")}} } = require("@simplyworks/bitween");
                    """;
            string T(string type) => ts ? type : "";
            var body = kind switch
            {
                "receiver" => $$"""
                    /** Fetches files on a schedule. Bitween calls initialize, listFiles, then getFile and deleteFile for each file, then finalize. */
                    class {{name}} extends Receiver {
                      constructor() {
                        super();
                        // Declare settings here; read them in the methods with valueOf.
                        expect("BaseUrl", { default: "https://partner.example.test", description: "Where files are fetched from." });
                        expect("ApiKey", { secret: true, description: "The partner's key." });
                      }

                      listFiles(){{T(": string[]")}} {
                        return ["example-1"];
                      }

                      getFile(fileId{{T(": string")}}){{T(": ExchangeFile")}} {
                        return new ExchangeFile({ data: JSON.stringify({ id: fileId }), filename: `${fileId}.json` });
                      }

                      deleteFile(fileId{{T(": string")}}){{T(": void")}} {}
                    }
                    """,
                "validator" => $$"""
                    /** Checks a message before Bitween accepts it. */
                    class {{name}} extends Validator {
                      constructor() {
                        super();
                        expect("MaxBytes", { default: "1000000", type: "number", description: "The largest message accepted." });
                      }

                      validate(file{{T(": ExchangeFile")}}){{T(": ValidationResult")}} {
                        const result = new ValidationResult();
                        if (file.data.length > Number(valueOf("MaxBytes"))) result.add("Data", "The message is larger than allowed.");
                        return result;
                      }
                    }
                    """,
                "mapper" => $$"""
                    /** Maps a message into the shape the next step expects. */
                    class {{name}} extends Mapper {
                      map(file{{T(": ExchangeFile")}}){{T(": ExchangeFile")}} {
                        // Return the message in its new shape.
                        return new ExchangeFile({ data: file.data, filename: file.filename });
                      }
                    }
                    """,
                _ => $$"""
                    /** Delivers a message and returns the partner's response. */
                    class {{name}} extends Handler {
                      constructor() {
                        super();
                        // Declare settings here; read them in the methods with valueOf.
                        expect("BaseUrl", { default: "https://partner.example.test", description: "Where messages go." });
                        expect("ApiKey", { secret: true, description: "The partner's key." });
                      }

                      handle(file{{T(": ExchangeFile")}}){{T(": ExchangeFile")}} {
                        // Send file.data to valueOf("BaseUrl"). A rejection is returned with badData: true, not thrown.
                        return new ExchangeFile({ data: file.data, filename: file.filename });
                      }
                    }
                    """,
            };
            // valueOf is used by every template but the mapper; keep the import list honest there.
            if (kind == "mapper") header = header.Replace("expect, run, valueOf", "run");
            else if (kind == "receiver") header = header.Replace("expect, run, valueOf", "expect, run");
            return header + "\n\n" + body + "\n\nrun(" + name + ");\n";
        }

        static string Pascal(string kind) => char.ToUpperInvariant(kind[0]) + kind[1..];

}
