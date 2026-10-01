using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// The storage bucket's deletion rules, which decide how long exchange files are kept. Read through
/// <see cref="ICloudFilesLifecycle"/> and held for a few minutes: the settings page, the retention
/// preview and every "why is this file gone" answer ask for them, and the storage service is slow and
/// rate-limited compared to that.
/// </summary>
public class StorageRetention(IServiceProvider serviceProvider, ILogger<StorageRetention> logger)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeepProblemFor = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private StorageRules _rules;
    private DateTime _readOn;

    public async Task<StorageRules> GetAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && Fresh()) return _rules;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && Fresh()) return _rules;

            _rules = await Read(cancellationToken);
            _readOn = DateTime.UtcNow;
            return _rules;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool Fresh() =>
        _rules != null && DateTime.UtcNow - _readOn < (_rules.Problem == null ? KeepFor : KeepProblemFor);

    private async Task<StorageRules> Read(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var lifecycle = scope.ServiceProvider.GetService<ICloudFilesLifecycle>();
        if (lifecycle == null)
            return new StorageRules(null, "This storage provider doesn't report its deletion rules.");

        try
        {
            var read = await lifecycle.GetLifecycleAsync(cancellationToken);
            return new StorageRules(read, read.Unavailable);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the storage bucket's lifecycle rules.");
            return new StorageRules(null, $"The storage service didn't return its deletion rules: {ex.Message}");
        }
    }
}

/// <summary>
/// What the bucket says about deleting files. <see cref="Problem"/> is set when the rules couldn't be
/// read, in which case nothing can be said about when files go.
/// </summary>
public record StorageRules(CloudFilesLifecycle Lifecycle, string Problem)
{
    /// <summary>The rule that deletes the file at <paramref name="key"/>, or <c>null</c> when none does (or none is known).</summary>
    public CloudFilesLifecycleRule RuleFor(string key) => Problem == null ? Lifecycle?.RuleFor(key) : null;

    /// <summary>The rule for files written under <paramref name="prefix"/>.</summary>
    public CloudFilesLifecycleRule RuleForPrefix(string prefix) =>
        string.IsNullOrEmpty(prefix) ? null : RuleFor(prefix.TrimEnd('/') + "/");
}

/// <summary>Recognises "that file isn't there" across the storage providers, which each throw their own exception for it.</summary>
public static class StorageErrors
{
    /// <summary>
    /// S3, Oracle and Google raise an exception carrying an HTTP status code, Azure one carrying the
    /// status as a number, and the local provider a plain file-not-found. Read by name so this project
    /// needs none of the providers' SDKs.
    /// </summary>
    public static bool IsNotFound(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is FileNotFoundException or DirectoryNotFoundException) return true;

            var type = current.GetType();
            var status = (type.GetProperty("StatusCode") ?? type.GetProperty("HttpStatusCode") ??
                          type.GetProperty("Status"))?.GetValue(current);
            if (status is HttpStatusCode.NotFound or 404) return true;
        }

        return false;
    }
}
