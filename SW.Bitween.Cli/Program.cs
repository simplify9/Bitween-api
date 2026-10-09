using CommandLine;
using CommandLine.Text;
using SW.Bitween.Adapters.Tooling;

namespace SW.Bitween.Cli;

/// <summary>
/// bitween — Bitween from a terminal.
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

            var parsed = Parser().ParseArguments<LoginOptions, LogoutOptions, WhoAmIOptions>(args);
            return await parsed.MapResult(
                (LoginOptions o) => Commands.Login(o, profiles, input, readSecret),
                (LogoutOptions o) => Task.FromResult(Commands.Logout(o, profiles)),
                (WhoAmIOptions o) => Commands.WhoAmI(o, profiles),
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
        bitween — Bitween from a terminal

          bitween login <url>              Sign in to a Bitween (email and password)
          bitween logout                   Forget the Bitween signed in to
          bitween whoami                   Which Bitween, and as whom

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
