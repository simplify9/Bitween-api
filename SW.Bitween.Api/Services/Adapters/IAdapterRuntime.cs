using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// What an adapter is being asked to be. The id alone does not say: a mapper and a handler are
/// both invoked as <c>Handle</c>, and only the role distinguishes which one to resolve.
/// </summary>
public enum AdapterRole
{
    Handler,
    Mapper,
    Validator,
    Receiver,
}

/// <summary>
/// A run of calls against ONE adapter instance.
///
/// A receiver is why this is a session rather than a single call: Initialize, ListFiles, a GetFile
/// and DeleteFile per item, then Finalize - all of which have to reach the same instance, or
/// Initialize runs somewhere the listing never sees.
/// </summary>
public interface IAdapterSession : IAsyncDisposable
{
    Task<TResult> InvokeAsync<TResult>(string method, object argument = null);

    /// <summary>For a method that returns nothing - Initialize, DeleteFile, Finalize.</summary>
    Task InvokeAsync(string method, object argument = null);
}

/// <summary>
/// One way of running an adapter. There are three, and they are genuinely different runtimes
/// rather than variations: in-process for a native adapter, a spawned process speaking
/// stdin/stdout for a classic one, and a pooled long-lived process on a socket for a resident one.
///
/// Registered in order; the first runtime that claims an id runs it.
/// </summary>
public interface IAdapterRuntime
{
    /// <summary>
    /// Whether this runtime is the one for that adapter. Async because deciding can mean reading
    /// the adapter's published metadata - cached, but not free.
    /// </summary>
    Task<bool> CanRunAsync(string adapterId);

    Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role,
        IDictionary<string, string> properties, string correlationId);
}

/// <summary>
/// The one thing the pipeline calls to run an adapter. Everything that needs a mapper, handler,
/// validator or receiver goes through here, so the choice of runtime is made once and in one
/// place - and so a test can replace the lot.
/// </summary>
public interface IAdapterInvoker
{
    Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role,
        IDictionary<string, string> properties = null, string correlationId = null);

    /// <summary>A session of exactly one call, which is what a mapper, handler or validator is.</summary>
    Task<TResult> InvokeAsync<TResult>(string adapterId, AdapterRole role, string method,
        object argument, IDictionary<string, string> properties = null, string correlationId = null);
}
