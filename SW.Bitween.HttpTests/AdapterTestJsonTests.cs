using SW.Bitween.Cli;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// bitween adapter test --json marks the checks that call the adapter, which need its settings and what they
/// point at: the registry's sandbox, with neither, judges a package by the rest.
/// </summary>
public class AdapterTestJsonTests
{
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
