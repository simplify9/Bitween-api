namespace SW.Bitween.Domain;

/// <summary>
/// An exchange's outcome, kept on the exchange itself (<see cref="Xchange.Status"/>) so the views
/// that look for the few unusual ones don't have to walk every exchange to find them.
/// </summary>
/// <remarks>
/// There is no value for success: a blank status means success, which is what nearly every exchange
/// is. That keeps the column empty when it is added, which every database does without rewriting
/// the table, and leaves only the unusual exchanges to fill in. 0 is left unused.
/// </remarks>
public enum XchangeStatus : byte
{
    /// <summary>Started, with no result yet.</summary>
    Processing = 1,

    /// <summary>Delivered, but the target's response was marked bad.</summary>
    BadResponse = 2,

    /// <summary>The result is a failure.</summary>
    Failed = 3,
}
