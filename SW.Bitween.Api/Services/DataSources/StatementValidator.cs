using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain.DataSources;
using SW.Serverless.Resident;

namespace SW.Bitween.Services.DataSources;

/// <summary>
/// Asks the database whether a statement is valid, at the moment it is saved.
///
/// The alternative is what we had: store anything, and find out at the connection test - or, if
/// nobody ran one, on the first message, as a failed Xchange pointing at a syntax error made days
/// earlier by someone else. A typo belongs to whoever typed it, and the only time that is true is
/// while they are still looking at the form.
///
/// It PREPARES the SQL: parsed and planned by the engine, never executed, nothing stored. That is
/// the same check the connection test runs, deliberately - two checks that could disagree about
/// what is acceptable would be worse than one.
///
/// It is best-effort by construction. The adapter has to be running on THIS node to answer, and
/// for a relational source it is (a pool is held per node), but a source that is stopped, broken
/// or still starting cannot be asked. In that case the save proceeds unchecked rather than being
/// blocked by an unrelated fault - refusing to let someone fix a statement because the connection
/// they are fixing it for is down would be exactly backwards.
/// </summary>
public class StatementValidator(
    BitweenDbContext dbContext,
    IResidentAdapterHost adapters = null)
{
    public sealed record Result(bool Checked, bool Ok, string Error, string Note)
    {
        /// <summary>Nobody could answer, so nothing is known and nothing is refused.</summary>
        public static readonly Result NotChecked = new(false, true, null, null);
    }

    public async Task<Result> ValidateAsync(int dataSourceId, string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Result.NotChecked;
        if (adapters == null) return Result.NotChecked;

        var dataSource = await dbContext.Set<DataSource>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dataSourceId);

        // Only a relational source runs SQL at all; a broker has no opinion about it.
        if (dataSource == null || dataSource.Kind != DataSourceKind.Relational) return Result.NotChecked;

        var instance = adapters.Describe()
            .FirstOrDefault(h => h.InstanceKey == dataSourceId.ToString());

        if (instance == null) return Result.NotChecked;

        try
        {
            var live = adapters.Get(instance.AdapterId, instance.InstanceKey);
            if (live == null) return Result.NotChecked;

            var raw = await live.InvokeAsync<JObject>("ValidateStatement", new { sql },
                timeoutSeconds: 20);

            if (raw == null) return Result.NotChecked;

            return new Result(
                Checked: true,
                Ok: raw.Value<bool?>("ok") ?? true,
                Error: raw.Value<string>("error"),
                Note: raw.Value<string>("note"));
        }
        catch (Exception)
        {
            // The adapter could not be reached, or does not know the command - an older package,
            // or a provider that never implemented it. Either way this is a fact about the
            // adapter, not about the SQL, and it must not stop someone saving.
            return Result.NotChecked;
        }
    }
}
