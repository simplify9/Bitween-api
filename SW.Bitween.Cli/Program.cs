using CommandLine;
using CommandLine.Text;
using SW.Bitween.Adapters.Tooling;

namespace SW.Bitween.Cli;

/// <summary>
/// bitween - Bitween from a terminal.
///
///   bitween login https://bitween.example.com
///   bitween adapter init AcmeOrders --kind handler --lang python
///   bitween adapter build AcmeOrders
///   bitween adapter test AcmeOrders --settings settings.json
///   bitween adapter publish AcmeOrders/bin/serverless/acme.orders-0.1.0.zip
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args) => await RunAsync(args, new Profiles(), Console.In, ReadSecret);

    internal static async Task<int> RunAsync(string[] args, Profiles profiles, TextReader input, Func<string> readSecret)
    {
        BitweenAdapters.RegisterContract();
        try
        {
            switch (args.FirstOrDefault())
            {
                case "adapter":
                case "adapters":
                    return await Adapter(args[1..], profiles);
                case "mapping":
                case "mappings":
                    return await Mapping(args[1..], profiles);
                case "kit":
                case "kits":
                    return await KitVerb(args[1..], profiles);
                case "version":
                case "--version":
                    Console.WriteLine($"bitween {Version}");
                    return Commands.Success;
                case null:
                case "help":
                case "--help":
                case "-h":
                    Console.WriteLine(Usage);
                    return args.Length == 0 ? Commands.Failure : Commands.Success;
            }

            var parsed = Parser().ParseArguments<LoginOptions, LogoutOptions, WhoAmIOptions, PlanOptions, ApplyOptions, ExportOptions>(args);
            return await parsed.MapResult(
                (LoginOptions o) => Commands.Login(o, profiles, input, readSecret),
                (LogoutOptions o) => Commands.Logout(o, profiles),
                (WhoAmIOptions o) => Commands.WhoAmI(o, profiles),
                (PlanOptions o) => Commands.Plan(o, profiles),
                (ApplyOptions o) => Commands.Apply(o, profiles),
                (ExportOptions o) => Commands.Export(o, profiles),
                _ => Task.FromResult(Help(parsed, "bitween")));
        }
        catch (BitweenApiException ex)
        {
            Console.WriteLine(ex.Message);
            return Commands.Failure;
        }
        catch (Exception ex) when (ex is PrimitiveTypes.SWException or HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine(ex.Message);
            return Commands.Failure;
        }
    }

    static async Task<int> Mapping(string[] args, Profiles profiles)
    {
        // One command for now, as a verb so 'bitween mapping test --help' reads like the others.
        var parsed = Parser().ParseArguments<MappingTestOptions, MappingPlaceholderOptions>(args);
        return await parsed.MapResult(
            (MappingTestOptions o) => Commands.TestMappings(o, profiles),
            (MappingPlaceholderOptions _) => Task.FromResult(Help(parsed, "bitween mapping")),
            _ => Task.FromResult(Help(parsed, "bitween mapping")));
    }

    static async Task<int> KitVerb(string[] args, Profiles profiles)
    {
        var parsed = Parser().ParseArguments<KitTestOptions, KitInstallOptions, KitBuildOptions, KitPlaceholderOptions>(args);
        return await parsed.MapResult(
            (KitTestOptions o) => Commands.KitTest(o, profiles),
            (KitInstallOptions o) => Commands.KitInstall(o, profiles),
            (KitBuildOptions o) => Commands.KitBuild(o),
            (KitPlaceholderOptions _) => Task.FromResult(Help(parsed, "bitween kit")),
            _ => Task.FromResult(Help(parsed, "bitween kit")));
    }

    static async Task<int> Adapter(string[] args, Profiles profiles)
    {
        var parsed = Parser().ParseArguments<InitOptions, BuildOptions, TestOptions, RunOptions,
            PublishOptions, PromoteOptions, VersionsOptions, WithdrawOptions>(args);
        return await parsed.MapResult(
            (InitOptions o) => Task.FromResult(Commands.Init(o)),
            (BuildOptions o) => Commands.Build(o),
            (TestOptions o) => Commands.Test(o),
            (RunOptions o) => Commands.Run(o),
            (PublishOptions o) => Commands.Publish(o, profiles),
            (PromoteOptions o) => Commands.Promote(o, profiles),
            (VersionsOptions o) => Commands.Versions(o, profiles),
            (WithdrawOptions o) => Commands.Withdraw(o, profiles),
            _ => Task.FromResult(Help(parsed, "bitween adapter")));
    }

    static Parser Parser() => new(s =>
    {
        s.HelpWriter = null;
        s.CaseInsensitiveEnumValues = true;
        // --set NAME=value once per parameter: a value may hold commas, as a SOQL field list does.
        s.AllowMultiInstance = true;
    });

    static int Help<T>(ParserResult<T> parsed, string command)
    {
        var help = HelpText.AutoBuild(parsed, h =>
        {
            h.Heading = $"bitween {Version}";
            h.Copyright = "";
            h.AdditionalNewLineAfterOption = false;
            return HelpText.DefaultParsingErrorsHandler(parsed, h);
        }, e => e, verbsIndex: true);
        Console.WriteLine(help.ToString().Replace("bitween.dll", command));
        var asked = parsed.Errors.Any(e => e is HelpRequestedError or HelpVerbRequestedError or VersionRequestedError);
        return asked ? Commands.Success : Commands.Failure;
    }

    static string Version =>
        typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "dev";

    const string Usage = """
        bitween - Bitween from a terminal

          bitween login <url>              Sign in to a Bitween through the browser (--no-browser to paste
                                           a code; --email and --password-stdin for a password)
          bitween logout                   Sign out of the Bitween signed in to
          bitween whoami                   Which Bitween, and as whom

          bitween plan -f <file|folder>    What apply would change, changing nothing
          bitween apply -f <file|folder>   Make the Bitween's configuration what the file says (--prune
                                           also removes what an earlier apply made and the file dropped)
          bitween export [-o file]         Write the Bitween's configuration as a file apply takes
                                           (plan and apply take --set NAME=value and --values for ${param:NAME})

          bitween kit test <kit>           Check a kit (--plan also plans it on the signed-in Bitween)
          bitween kit install <kit>        Install its connectors and configuration (--set NAME=value,
                                           --values file, --name for a second installation, --dry-run)
          bitween kit build <kit>          Check it and pack it into a zip for the registry

          Without a profile, BITWEEN_URL and BITWEEN_TOKEN sign in as a service account.

          bitween adapter init <Name>      Write a new adapter: --kind handler|mapper|validator|receiver
                                           --lang dotnet|python|node|typescript
          bitween adapter build [folder]   Build it into a package
          bitween adapter test [package]   Check it against the Bitween contract (--settings file.json)
          bitween adapter run [package]    Call one command (--call Handle --input '{...}')
          bitween adapter publish <zip>    Publish through the signed-in Bitween (--current to make it run;
                                           -p and storage flags to publish straight to storage instead)
          bitween adapter promote <id> <version>
          bitween adapter versions <id>
          bitween adapter withdraw <id> <version>

          bitween mapping test <cases.yaml> Map sample inputs with subscriptions' saved mappings and compare
                                           them with what's expected (--update writes the outputs instead)

        bitween <command> --help shows a command's options.
        """;

    /// <summary>A password typed without echo.</summary>
    static string ReadSecret()
    {
        if (Console.IsInputRedirected) return Console.In.ReadLine();
        var secret = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) return secret.ToString();
            if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; }
            else if (!char.IsControl(key.KeyChar)) secret.Append(key.KeyChar);
        }
    }
}
