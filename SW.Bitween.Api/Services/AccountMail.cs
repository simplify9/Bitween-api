using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.ChannelMessages;
using SW.Bitween.NativeAdapters.EmailMessageHandler;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services;

/// <summary>
/// Emails about a person's own account - an invite, a password reset, a password changed, an
/// account locked - sent to that person through the mail server set in Settings.
/// </summary>
/// <remarks>
/// Not through notification channels: those tell a team about its integrations, and a channel can
/// be a shared Teams or Slack room. These go to exactly one person, and carry links that sign in.
/// Laid out by the Email channel's own adapter, so they look like Bitween's other emails.
/// </remarks>
public class AccountMail(BitweenOptions options, ILogger<AccountMail> logger,
    IHttpContextAccessor httpContextAccessor, DestinationCircuits circuits = null, ThemeOptions theme = null)
{
    /// <summary>Whether there is a mail server to send through.</summary>
    public bool CanSend => options.MailServer.IsSet;

    /// <summary>Whether the sign-in page can offer "Forgot password?".</summary>
    public bool ResetAvailable => ResetAvailableFor(options);

    /// <remarks>
    /// Needs the public address as well as a mail server. Anyone can ask for a reset, and the link
    /// in it must not be built on the address their request came in on: a forged Host header would
    /// send the victim a link to the attacker's site, with the victim's reset token in it.
    /// </remarks>
    public static bool ResetAvailableFor(BitweenOptions options) =>
        options.MailServer.IsSet && !string.IsNullOrWhiteSpace(options.PublicUrl) && !options.DisableEmailPasswordLogin;

    /// <summary>The public address the chart sets, or null.</summary>
    public string PublicBase => string.IsNullOrWhiteSpace(options.PublicUrl) ? null : options.PublicUrl.TrimEnd('/');

    /// <summary>
    /// Where links point for a request a signed-in member made: the public address, or else the one
    /// they are using. Never for a request anyone could make; see <see cref="ResetAvailableFor"/>.
    /// </summary>
    public string SignedInBase
    {
        get
        {
            if (PublicBase is { } publicBase) return publicBase;
            var request = httpContextAccessor.HttpContext?.Request;
            return request is { Host.HasValue: true }
                ? $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/')
                : "";
        }
    }

    /// <summary>Sends one email; null when it went, else why it didn't.</summary>
    public async Task<string> SendAsync(string to, ChannelMessage message)
    {
        if (!CanSend) return "No mail server is set up. Add one in Settings, under Mail server.";
        try
        {
            var email = new NativeEmailMessageHandler(options.MailServer, circuits, theme?.PrimaryColor);
            email.InitializeStartupValues(new Dictionary<string, string> { ["To"] = to });
            await email.Handle(new XchangeFile(JsonConvert.SerializeObject(message)));
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "An account email ({Title}) could not be sent: {Error}",
                message.Title, ex.GetBaseException().Message);
            return ex.GetBaseException().Message;
        }
    }

    /// <summary>
    /// Sends without waiting, for an email the request doesn't depend on. Also what keeps a reset
    /// from taking longer for an address that has an account than for one that hasn't.
    /// </summary>
    public void SendInBackground(string to, ChannelMessage message)
    {
        if (!CanSend || string.IsNullOrWhiteSpace(to)) return;
        _ = Task.Run(() => SendAsync(to, message));
    }
}

/// <summary>What each account email says.</summary>
public static class AccountEmails
{
    public enum ChangedBy
    {
        Themselves,
        Administrator,
        ResetLink
    }

    public static ChannelMessage Invite(string inviter, string email, string linkBase, string token) => new()
    {
        Title = "You're invited to Bitween",
        Tone = ChannelMessageTone.Info,
        Text = $"{inviter} added you to Bitween{On(linkBase)}. Choose your password to sign in. " +
               $"The link works once, for {Hours(Domain.Accounts.AccountLink.InviteLifetime)}.",
        Facts = [new("Email", email)],
        LinkUrl = $"{linkBase}/set-password?token={token}",
        LinkLabel = "Choose your password"
    };

    /// <summary>For an instance that signs in only with Microsoft or single sign-on: no password to choose.</summary>
    public static ChannelMessage InviteToSignIn(string inviter, string email, string linkBase) => new()
    {
        Title = "You're invited to Bitween",
        Tone = ChannelMessageTone.Info,
        Text = $"{inviter} added you to Bitween{On(linkBase)}. Sign in with your company account.",
        Facts = [new("Email", email)],
        LinkUrl = string.IsNullOrEmpty(linkBase) ? null : $"{linkBase}/login",
        LinkLabel = "Sign in"
    };

    public static ChannelMessage Reset(string email, string linkBase, string token) => new()
    {
        Title = "Reset your Bitween password",
        Tone = ChannelMessageTone.Info,
        Text = $"Someone asked to reset the password of {email}{On(linkBase)}. If it was you, choose a new one: " +
               $"the link works once, for {Hours(Domain.Accounts.AccountLink.ResetLifetime)}. " +
               "If it wasn't, ignore this email. Your password stays as it is.",
        LinkUrl = $"{linkBase}/set-password?token={token}",
        LinkLabel = "Choose a new password"
    };

    public static ChannelMessage PasswordChanged(ChangedBy by, string email, string linkBase, bool resetAvailable) => new()
    {
        Title = "Your Bitween password was changed",
        Tone = ChannelMessageTone.Warning,
        Text = by switch
        {
            ChangedBy.Administrator =>
                $"An administrator set a new password for {email}{On(linkBase)}. You've been signed out everywhere. " +
                "If you didn't expect this, tell your administrator.",
            ChangedBy.ResetLink =>
                $"The password of {email}{On(linkBase)} was changed with a reset link. You've been signed out everywhere. " +
                "If it wasn't you, tell your administrator.",
            _ =>
                $"The password of {email}{On(linkBase)} was just changed. If it wasn't you, " +
                (resetAvailable ? "reset it now and tell your administrator." : "tell your administrator now.")
        },
        Facts = [new("When", When(DateTime.UtcNow))],
        LinkUrl = string.IsNullOrEmpty(linkBase) ? null : resetAvailable ? $"{linkBase}/forgot-password" : $"{linkBase}/login",
        LinkLabel = resetAvailable ? "Reset your password" : "Sign in"
    };

    public static ChannelMessage Locked(string email, string linkBase, int attempts, DateTime lockedUntil) => new()
    {
        Title = "Your Bitween account was locked",
        Tone = ChannelMessageTone.Warning,
        Text = $"After {attempts} wrong passwords for {email}{On(linkBase)}, signing in with a password is locked " +
               "for a while. If this wasn't you, someone may be guessing your password: tell your administrator.",
        Facts = [new("Locked until", When(lockedUntil))]
    };

    static string On(string linkBase) =>
        Uri.TryCreate(linkBase, UriKind.Absolute, out var uri) ? $" at {uri.Host}" : "";

    static string Hours(TimeSpan lifetime) =>
        lifetime.TotalHours == 1 ? "1 hour" : $"{lifetime.TotalHours:0} hours";

    static string When(DateTime utc) =>
        utc.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
