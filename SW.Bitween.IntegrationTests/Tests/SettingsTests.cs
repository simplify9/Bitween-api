using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Instance settings — the values an administrator can change without a redeploy.
/// </summary>
/// <remarks>
/// Two things make this worth covering. Secrets are encrypted before they are written, so a
/// database dump never carries a license key in the clear — and an encryption bug is invisible
/// from the UI, which shows the value correctly either way. And only some settings are editable at
/// all: the page also lists environment-owned ones, and accepting a write to those would report
/// success for a change that can never take effect.
/// </remarks>
[Collection("Bitween")]
public class SettingsTests : IAsyncLifetime
{
    private const string SecretKey = "Bitween.RebexLicenseKey";
    private const string EditableKey = "Bitween.JwtExpiryMinutes";
    private const string EnvironmentOwnedKey = "Bitween.DocumentPrefix";

    private readonly BitweenFixture _fixture;
    private readonly Dictionary<string, string> _originals = new();

    public SettingsTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Applying a setting mutates a process-wide options singleton that every test in this
    /// collection shares, so anything written here has to be put back. Nothing depends on these
    /// two today — but the Rebex key decides which adapters are offered at all, and a future test
    /// of that would pass or fail purely on whether this class happened to run first.
    /// </summary>
    public async Task InitializeAsync()
    {
        foreach (var key in new[] { SecretKey, EditableKey })
            _originals[key] = await LiveValue(key);
    }

    public async Task DisposeAsync()
    {
        foreach (var (key, value) in _originals)
            await Store(key, value);
    }

    private async Task Store(string key, string value)
    {
        await using var scope = _fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Settings.Update>(scope.ServiceProvider);
        await handler.Handle(key, new SettingUpdate { Value = value });
    }

    private async Task<string> RawStored(string key)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var row = await db.Set<Setting>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == key);
        return row?.Value;
    }

    private async Task<string> LiveValue(string key)
    {
        await using var scope = _fixture.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        return settings.LiveValue(SettingsCatalog.Find(key));
    }

    [Fact]
    public async Task A_secret_is_never_written_in_the_clear()
    {
        const string licenseKey = "REBEX-1234-SECRET-VALUE";
        await Store(SecretKey, licenseKey);

        var raw = await RawStored(SecretKey);

        // The whole point: a database dump or backup must not carry this readable.
        Assert.DoesNotContain(licenseKey, raw);
        Assert.StartsWith("enc.v1:", raw);
    }

    [Fact]
    public async Task A_secret_comes_back_as_it_went_in()
    {
        const string licenseKey = "REBEX-ROUND-TRIP-9876";
        await Store(SecretKey, licenseKey);

        // Encryption that cannot be reversed is indistinguishable from a corrupted value until
        // the adapter it licenses refuses to start.
        Assert.Equal(licenseKey, await LiveValue(SecretKey));
    }

    [Fact]
    public async Task The_same_secret_stored_twice_does_not_produce_the_same_ciphertext()
    {
        await Store(SecretKey, "REBEX-REPEATED-VALUE");
        var first = await RawStored(SecretKey);
        await Store(SecretKey, "REBEX-REPEATED-VALUE");
        var second = await RawStored(SecretKey);

        // A fresh salt and nonce each time, so anyone reading the table cannot tell that two
        // instances share a license key, or spot when one goes back to a previous value.
        Assert.NotEqual(first, second);
        Assert.Equal("REBEX-REPEATED-VALUE", await LiveValue(SecretKey));
    }

    [Fact]
    public async Task An_ordinary_setting_takes_effect_immediately_and_is_stored_as_written()
    {
        await Store(EditableKey, "45");

        // Applied to the live options singleton as well as stored, so the very next request
        // already sees it — there is no restart-required concept in this product.
        Assert.Equal("45", await LiveValue(EditableKey));
        Assert.Equal("45", await RawStored(EditableKey));
    }

    [Fact]
    public async Task An_environment_owned_setting_is_refused_rather_than_quietly_ignored()
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            Store(EnvironmentOwnedKey, "some-other-prefix"));

        // The page lists these so an administrator can see them, which makes it easy to try
        // editing one. Accepting the write would report success for a change that can never
        // take effect — and for this key, one that would strand everything already stored.
        Assert.StartsWith("SETTING_NOT_EDITABLE", ex.Message);
        Assert.Null(await RawStored(EnvironmentOwnedKey));
    }

    [Fact]
    public async Task A_key_that_is_not_a_setting_is_refused()
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            Store("Bitween.NotARealSetting", "value"));

        // The catalog is the whole list of what exists. Storing an unknown key would leave a row
        // nothing ever reads, looking like a setting that simply does not work.
        Assert.StartsWith("SETTING_NOT_FOUND", ex.Message);
    }

    [Fact]
    public async Task A_value_the_setting_cannot_hold_is_refused()
    {
        var ex = await Assert.ThrowsAsync<SWValidationException>(() => Store(EditableKey, "not-a-number"));

        // Stored unchecked, this fails much later — when something reads the session length and
        // cannot parse it, far from the screen where it was typed.
        Assert.StartsWith("SETTING_INVALID_VALUE", ex.Message);
    }

    [Fact]
    public async Task Clearing_an_optional_setting_is_a_real_change_not_a_no_op()
    {
        await Store(SecretKey, "REBEX-TO-BE-CLEARED");
        Assert.NotEqual(string.Empty, await LiveValue(SecretKey));

        await Store(SecretKey, string.Empty);

        // Empty is how you remove a license key or an optional link. Treating it as "nothing to
        // do" would make a setting impossible to unset once set.
        Assert.Equal(string.Empty, await LiveValue(SecretKey));
    }
}
