using System.Collections.Generic;
using System.Linq;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Rejects retry groups that could never fire. The evaluator skips such groups silently
/// (matchers must support the result type being evaluated — see
/// <see cref="Matcher.Supports"/>), which reads as "retries just don't work", so the
/// misconfiguration is caught at write time instead.
/// </summary>
public static class RetryGroupValidation
{
    public static void EnsureCanFire(IEnumerable<RetryGroup> groups)
    {
        foreach (var group in groups ?? [])
        {
            if ((group.AppliesTo?.Count ?? 0) == 0)
                throw new SWValidationException("RETRY_GROUP_NO_RESULT_TYPE",
                    $"Group '{group.Name}' applies to no result type, so it would never be evaluated. " +
                    "Select Error, Bad result, or both.");

            if ((group.Matchers?.Count ?? 0) == 0)
                throw new SWValidationException("RETRY_GROUP_NO_MATCHERS",
                    $"Group '{group.Name}' has no matchers, so it would never match a failure. " +
                    "Add at least one matcher.");

            foreach (var resultType in group.AppliesTo)
                if (!group.Matchers.Any(m => m.Supports(resultType)))
                    throw new SWValidationException("RETRY_GROUP_INCOMPATIBLE_MATCHERS",
                        $"Group '{group.Name}' applies to {resultType} but none of its matchers can be " +
                        $"evaluated against {resultType} content. {SupportedMatchersFor(resultType)}");
        }
    }

    private static string SupportedMatchersFor(XchangeResultType resultType) => resultType switch
    {
        XchangeResultType.Error => "Error supports Contains, Regex and Exception type matchers.",
        XchangeResultType.BadResult => "Bad result supports Contains, Regex and JSON path matchers.",
        _ => "Successful results are never retried."
    };
}
