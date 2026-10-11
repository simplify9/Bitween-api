using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// Adapters that ship inside Bitween and run in-process. No packaging, no process, no protocol -
/// the only runtime where "invoke" is a method call.
/// </summary>
public class NativeAdapterRuntime(NativeAdapterDiscoveryService discovery) : IAdapterRuntime
{
    public Task<bool> CanRunAsync(string adapterId) =>
        Task.FromResult(adapterId != null && adapterId.StartsWith(
            NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase));

    public Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role,
        IDictionary<string, string> properties, string correlationId)
    {
        var settings = properties as Dictionary<string, string>
                       ?? new Dictionary<string, string>(properties ?? new Dictionary<string, string>());

        // The role is what disambiguates: a mapper and a handler are both invoked as Handle, and
        // are resolved from different registrations.
        object adapter = role switch
        {
            AdapterRole.Mapper => discovery.GetNativeMapper(adapterId, settings),
            AdapterRole.Validator => discovery.GetNativeValidator(adapterId, settings),
            AdapterRole.Receiver => discovery.GetNativeReceiver(adapterId, settings),
            _ => discovery.GetNativeHandler(adapterId, settings),
        };

        return Task.FromResult<IAdapterSession>(new NativeAdapterSession(adapter));
    }

    /// <summary>
    /// Dispatches by method name onto the resolved adapter, so the pipeline can treat a native
    /// adapter exactly like a packaged one.
    /// </summary>
    private sealed class NativeAdapterSession(object adapter) : IAdapterSession
    {
        public async Task<TResult> InvokeAsync<TResult>(string method, object argument = null) =>
            (TResult)await Dispatch(method, argument);

        public Task InvokeAsync(string method, object argument = null) => Dispatch(method, argument);

        private async Task<object> Dispatch(string method, object argument)
        {
            switch (adapter, method)
            {
                case (INativeInfolinkHandler h, nameof(IInfolinkHandler.Handle)):
                    return await h.Handle((XchangeFile)argument);

                case (INativeInfolinkMapper m, nameof(IInfolinkHandler.Handle)):
                    return await m.Handle((XchangeFile)argument);

                case (INativeInfolinkValidator v, nameof(IInfolinkValidator.Validate)):
                    return await v.Validate((XchangeFile)argument);

                case (INativeInfolinkReceiver r, nameof(IInfolinkReceiver.Initialize)):
                    await r.Initialize();
                    return null;

                case (INativeInfolinkReceiver r, nameof(IInfolinkReceiver.ListFiles)):
                    return (await r.ListFiles()).ToList();

                case (INativeInfolinkReceiver r, nameof(IInfolinkReceiver.GetFile)):
                    return await r.GetFile((string)argument);

                case (INativeInfolinkReceiver r, nameof(IInfolinkReceiver.DeleteFile)):
                    await r.DeleteFile((string)argument);
                    return null;

                case (INativeInfolinkReceiver r, nameof(IInfolinkReceiver.Finalize)):
                    await r.Finalize();
                    return null;

                default:
                    throw new BitweenException(
                        $"Native adapter '{adapter.GetType().Name}' has nothing called '{method}'.");
            }
        }

        // Nothing to release: a native adapter is an object, not a process or a lease.
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
