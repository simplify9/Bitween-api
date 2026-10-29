using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Adapters;

/// <summary>
/// What a validator found. On the wire it is the JSON
/// <c>{"Success": bool, "Validations": [{"Key": code, "Value": message}]}</c> that
/// <c>SW.PrimitiveTypes.InfolinkValidatorResult</c> has always produced.
/// </summary>
public class ValidationResult
{
    private List<KeyValuePair<string, string>> _validations = new();

    /// <summary>A result with no failures.</summary>
    public ValidationResult()
    {
    }

    /// <summary>A result with these failures.</summary>
    public ValidationResult(IEnumerable<KeyValuePair<string, string>> validations)
    {
        _validations = new List<KeyValuePair<string, string>>(validations);
    }

    /// <summary>True when nothing failed.</summary>
    public bool Success => _validations.Count == 0;

    /// <summary>Each failure: a code (often the field it concerns) and a message.</summary>
    public List<KeyValuePair<string, string>> Validations
    {
        get => _validations;
        set => _validations = value ?? new List<KeyValuePair<string, string>>();
    }

    /// <summary>Records one failure.</summary>
    public ValidationResult AddError(string code, string message)
    {
        _validations.Add(new KeyValuePair<string, string>(
            code ?? throw new ArgumentNullException(nameof(code)),
            message ?? throw new ArgumentNullException(nameof(message))));
        return this;
    }

    /// <summary>Whether a failure with this code was recorded.</summary>
    public bool Has(string code) => _validations.Any(v => v.Key == code);
}
