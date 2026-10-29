using System;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace SW.Bitween.HttpTests;

/// <summary>
/// Which release a Bitween says it is: Bitween:Version when set, as the Helm chart sets it; else what CI
/// stamped on the build; else the baseline. The About page shows it, and marketplace connectors are
/// checked against it.
/// </summary>
public class BitweenVersionTests
{
    /// <summary>An assembly carrying the informational version a build gave it.</summary>
    static Assembly Built(string informationalVersion)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Stamp" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        if (informationalVersion != null)
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion]));
        return builder;
    }

    [Theory]
    [InlineData("10.0.93+abc1234def", "10.0.93+abc1234def")]
    [InlineData("10.1.0", "10.1.0")]
    // What every unstamped build says, and anything not above the baseline: not a release.
    [InlineData("1.0.0", null)]
    [InlineData("1.0.0+abc1234def", null)]
    [InlineData("10.0.0", null)]
    [InlineData(null, null)]
    public void A_build_is_stamped_only_with_a_release_above_the_baseline(string informational, string stamped) =>
        Assert.Equal(stamped, BitweenInfo.Stamped(Built(informational)));

    [Fact]
    public void The_setting_wins_and_reads_a_build_suffix_or_a_leading_v()
    {
        Assert.Equal(new Version(10, 0, 93), BitweenInfo.Version(new BitweenOptions { Version = "10.0.93" }));
        Assert.Equal(new Version(10, 0, 93), BitweenInfo.Version(new BitweenOptions { Version = "v10.0.93-dev.160+abc" }));
    }

    [Fact]
    public void Without_the_setting_an_unstamped_build_is_the_baseline()
    {
        // The test host builds Bitween unstamped, as a local build is.
        Assert.Null(BitweenInfo.Build);
        Assert.Equal(Version.Parse(BitweenInfo.Baseline), BitweenInfo.Version(new BitweenOptions()));
        Assert.Equal(Version.Parse(BitweenInfo.Baseline), BitweenInfo.Version(new BitweenOptions { Version = "not a version" }));
    }
}
