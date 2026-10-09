using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters;

// The .NET form of the Bitween adapter contract (Contract/bitween-adapter-contract.v1.json). Bitween
// calls an adapter by method name, so these names — Handle, Validate, Initialize, ListFiles,
// GetFile, DeleteFile, Finalize — are the contract, and must never change. Adapters written against
// SW.PrimitiveTypes' IInfolinkHandler, IInfolinkValidator and IInfolinkReceiver use the same names
// and are called the same way.

/// <summary>Delivers a message: given the file to send, returns the partner's response.</summary>
public interface IBitweenHandler
{
    /// <summary>Delivers <paramref name="file"/>. A rejection by the partner is returned with <see cref="ExchangeFile.BadData"/> set, not thrown.</summary>
    Task<ExchangeFile> Handle(ExchangeFile file);
}

/// <summary>Transforms a message: given a file, returns it mapped into the shape the next step expects.</summary>
public interface IBitweenMapper
{
    /// <summary>Returns <paramref name="file"/> mapped into the shape the next step expects.</summary>
    Task<ExchangeFile> Handle(ExchangeFile file);
}

/// <summary>Checks a message before it is accepted, and says what is wrong with it.</summary>
public interface IBitweenValidator
{
    /// <summary>Checks <paramref name="file"/>; an empty result means it passed.</summary>
    Task<ValidationResult> Validate(ExchangeFile file);
}

/// <summary>
/// Fetches files from a source on a schedule. Bitween calls, on one session and in this order:
/// <see cref="Initialize"/>, <see cref="ListFiles"/>, then for each file <see cref="GetFile"/> and,
/// once it is safely taken in, <see cref="DeleteFile"/>, and finally <see cref="Finalize"/> —
/// also after a failure.
/// </summary>
public interface IBitweenReceiver
{
    /// <summary>Opens the session: connects, signs in.</summary>
    Task Initialize();

    /// <summary>The ids of the files ready to take in this run.</summary>
    Task<IEnumerable<string>> ListFiles();

    /// <summary>The file with an id <see cref="ListFiles"/> gave.</summary>
    Task<ExchangeFile> GetFile(string fileId);

    /// <summary>Removes, or moves aside, a file Bitween has safely taken in.</summary>
    Task DeleteFile(string fileId);

    /// <summary>Closes the session. Called after a failure too.</summary>
    Task Finalize();
}
