using System;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Services;

public static class SeededAdministrator
{
    public const int Id = 9999;

    // The password the migrations give the administrator they seed, published in this repository.
    public const string PublishedPassword = "Mtm@dmin!2";

    /// <summary>
    /// Replaces the published password on the seeded administrator with the one configured in
    /// <c>Bitween:InitialAdminPassword</c>. Runs after <c>MigrateDatabase</c>; once the password is
    /// anything else this does nothing, so changing the configured value later has no effect.
    /// </summary>
    public static IHost SecureSeededAdministrator(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        Secure(
                scope.ServiceProvider.GetRequiredService<BitweenDbContext>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>()["Bitween:InitialAdminPassword"],
                scope.ServiceProvider.GetRequiredService<ILogger<BitweenDbContext>>())
            .GetAwaiter().GetResult();
        return host;
    }

    /// <remarks>
    /// Every installation is seeded with this administrator, and while it holds the published
    /// password whoever signs in first can choose a new one and own the instance. When no password
    /// is configured, an installation with other accounts keeps today's behaviour (the password
    /// must be changed at sign-in); one where this is the only account — a new installation —
    /// refuses to start, since there is nobody else to set it up.
    /// </remarks>
    public static async Task Secure(BitweenDbContext dbContext, string initialPassword,
        ILogger logger)
    {
        var admin = await dbContext.Set<Account>().SingleOrDefaultAsync(a => a.Id == Id && !a.Deleted);

        if (admin is null || !HoldsPublishedPassword(admin))
            return;

        if (string.IsNullOrWhiteSpace(initialPassword))
        {
            if (await dbContext.Set<Account>().AnyAsync(a => a.Id != Id && !a.Deleted))
                return;

            throw new InvalidOperationException(
                "This is a new installation and Bitween:InitialAdminPassword is not configured. Set it " +
                $"(env: Bitween__InitialAdminPassword) to the password for the administrator, {admin.Email}. " +
                "It is read on startup only while that account still has the default password.");
        }

        if (initialPassword == PublishedPassword)
            throw new InvalidOperationException(
                "Bitween:InitialAdminPassword is the default password, which is published in a public " +
                "repository. Choose a different one.");

        var policy = new InlineValidator<string>();
        policy.RuleFor(p => p).Password().OverridePropertyName("InitialAdminPassword");
        var result = policy.Validate(initialPassword);
        if (!result.IsValid)
            throw new InvalidOperationException(
                "Bitween:InitialAdminPassword does not meet the password policy: " +
                string.Join(" ", result.Errors.Select(e => e.ErrorMessage)));

        // Together or not at all: a new password stored without the deletion would never be retried,
        // since the next start no longer finds the published one. A caller's transaction is used
        // rather than nested.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync()
            : null;

        admin.SetPassword(initialPassword);
        await dbContext.SaveChangesAsync();

        // A session opened with the published password would otherwise be refreshed into a full
        // administrator now that the change-password requirement is cleared.
        await dbContext.Set<RefreshToken>()
            .Where(t => t.AccountId == admin.Id)
            .ExecuteDeleteAsync();

        if (transaction is not null)
            await transaction.CommitAsync();

        logger.LogInformation("Set the password of {Email} from Bitween:InitialAdminPassword.", admin.Email);
    }

    private static bool HoldsPublishedPassword(Account admin) =>
        admin.Password is not null &&
        SecurePasswordHasher.IsHashSupported(admin.Password) &&
        SecurePasswordHasher.Verify(PublishedPassword, admin.Password);
}
