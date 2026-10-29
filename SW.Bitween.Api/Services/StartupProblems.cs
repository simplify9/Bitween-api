using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Services;

/// <summary>
/// A reason Bitween must not start, worded as what to change. A check throws it and
/// <see cref="StartupProblems"/> collects it, so the checks after it still run.
/// </summary>
public class StartupProblemException(string message) : InvalidOperationException(message);

/// <summary>
/// Every reason this start is refused. They are logged together, so whoever deploys fixes all of
/// them in one redeploy rather than meeting them one restart at a time.
/// </summary>
public class StartupProblems
{
    private readonly List<string> _all = [];

    public bool Any => _all.Count > 0;

    public void Add(string problem) => _all.Add(problem);

    /// <summary>Runs <paramref name="check"/>, recording the problem it reports instead of stopping.</summary>
    public void Check(Action check)
    {
        try
        {
            check();
        }
        catch (StartupProblemException ex)
        {
            Add(ex.Message);
        }
    }

    public override string ToString() =>
        "Bitween can't start until these are fixed:" +
        string.Concat(_all.Select((problem, i) => $"{Environment.NewLine}  {i + 1}. {problem}"));
}
