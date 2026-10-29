using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SW.Bitween.Cli;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// bitween adapter test --json marks the checks that call the adapter, which need its settings and what they
/// point at: the registry's sandbox, with neither, judges a package by the rest.
/// </summary>
[Collection("Http")]
public class AdapterTestJsonTests
{
    /// <summary>A zip cut short, as the sandbox once got one: said plainly, and with --json, a report that fails it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_package_that_isnt_a_whole_zip_is_said_not_crashed_on(bool json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cut-{Guid.NewGuid():N}.zip");
        // The start of a real zip, and nothing after it.
        await File.WriteAllBytesAsync(path, [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00]);
        var (stdout, stderr) = (Console.Out, Console.Error);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        Console.SetOut(output);
        Console.SetError(errors);
        int exit;
        try
        {
            exit = await Program.RunAsync(json ? ["adapter", "test", path, "--json"] : ["adapter", "test", path],
                new Profiles(Path.Combine(Path.GetTempPath(), $"cli-{Guid.NewGuid():N}.json")), new StringReader(""), () => "");
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            File.Delete(path);
        }

        Assert.Equal(1, exit);
        if (!json)
        {
            Assert.Contains("can't be opened as a zip", output.ToString());
            return;
        }
        Assert.Contains("can't be opened as a zip", errors.ToString());
        var report = JsonDocument.Parse(output.ToString().Trim()).RootElement;
        Assert.False(report.GetProperty("passed").GetBoolean());
        var check = report.GetProperty("checks")[0];
        Assert.Equal("package", check.GetProperty("name").GetString());
        Assert.Equal("Failed", check.GetProperty("outcome").GetString());
        Assert.False(check.GetProperty("call").GetBoolean());
    }

    [Theory]
    [InlineData("manifest", false)]
    [InlineData("describe", false)]
    [InlineData("settings match the manifest", false)]
    [InlineData("starts", false)]
    [InlineData("an unknown command is refused", false)]
    [InlineData("bitween handler: methods", false)]
    [InlineData("bitween handler: Handle answers example 1", true)]
    [InlineData("bitween receiver: ListFiles", true)]
    [InlineData("bitween receiver: DeleteFile", true)]
    public void A_check_that_calls_the_adapter_is_marked(string name, bool call) =>
        Assert.Equal(call, Commands.IsCall(name));
}
