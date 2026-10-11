using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Services.DataSources;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The seam that let SQL statements become their own entity without changing the adapter contract.
///
/// The adapter has always received name-to-SQL as JSON and has never known whether that came from
/// a field somebody typed into or from rows with their own permissions. These tests pin the three
/// properties that make the substitution safe.
/// </summary>
[TestClass]
public class StatementComposerTests
{
    static DataSourceStatement Statement(int dataSourceId, string name, string sql,
        bool inactive = false) => new()
    {
        DataSourceId = dataSourceId,
        Name = name,
        Sql = sql,
        Inactive = inactive
    };

    [TestMethod]
    public void Composes_the_statements_of_one_data_source()
    {
        var composed = StatementComposer.Compose(new[]
        {
            Statement(1, "getOrder", "select * from orders where id = :id"),
            Statement(1, "insertOrder", "insert into orders (id) values (:id)"),

            // Another connection's statement, which must not leak into this one's allow-list.
            Statement(2, "dropEverything", "drop table orders")
        }, dataSourceId: 1);

        var parsed = JObject.Parse(composed);

        Assert.AreEqual(2, parsed.Count);
        Assert.AreEqual("select * from orders where id = :id", parsed.Value<string>("getOrder"));
        Assert.IsNull(parsed["dropEverything"], "a statement belongs to exactly one data source");
    }

    /// <summary>Retiring a statement takes it out of the allow-list without deleting the row.</summary>
    [TestMethod]
    public void An_inactive_statement_is_left_out()
    {
        var composed = StatementComposer.Compose(new[]
        {
            Statement(1, "live", "select 1"),
            Statement(1, "retired", "select 2", inactive: true)
        }, dataSourceId: 1);

        var parsed = JObject.Parse(composed);

        Assert.AreEqual(1, parsed.Count);
        Assert.IsNull(parsed["retired"]);
    }

    /// <summary>
    /// Null rather than "{}", so a data source with no statements leaves the setting absent
    /// instead of handing the adapter an empty allow-list it would report as configured.
    /// </summary>
    [TestMethod]
    public void No_statements_composes_to_nothing()
    {
        Assert.IsNull(StatementComposer.Compose(new List<DataSourceStatement>(), dataSourceId: 1));
        Assert.IsNull(StatementComposer.Compose(new[]
        {
            Statement(1, "retired", "select 1", inactive: true)
        }, dataSourceId: 1));
    }

    /// <summary>
    /// SQL is full of quotes, backslashes and newlines. Hand-built JSON breaks on the first
    /// statement anyone writes across two lines, and the failure would be a parse error inside the
    /// adapter at startup, a long way from the person who typed it.
    /// </summary>
    [TestMethod]
    public void Sql_with_quotes_and_newlines_survives_the_round_trip()
    {
        const string awkward = "select *\nfrom orders\nwhere note = 'it''s \"fine\"' and path = 'c:\\tmp'";

        var composed = StatementComposer.Compose(new[]
        {
            Statement(1, "awkward", awkward)
        }, dataSourceId: 1);

        Assert.AreEqual(awkward, JObject.Parse(composed).Value<string>("awkward"));
    }

    /// <summary>
    /// Stable ordering. The supervisor fingerprints startup values to decide whether an adapter
    /// needs restarting, so an order that varied between reconciles would recycle a healthy
    /// database connection every thirty seconds - and nothing would say why.
    /// </summary>
    [TestMethod]
    public void Composition_is_stable_whatever_order_the_rows_arrive_in()
    {
        var one = StatementComposer.Compose(new[]
        {
            Statement(1, "alpha", "select 1"),
            Statement(1, "beta", "select 2"),
            Statement(1, "gamma", "select 3")
        }, dataSourceId: 1);

        var other = StatementComposer.Compose(new[]
        {
            Statement(1, "gamma", "select 3"),
            Statement(1, "alpha", "select 1"),
            Statement(1, "beta", "select 2")
        }, dataSourceId: 1);

        Assert.AreEqual(one, other);
    }

    /// <summary>
    /// A statement nothing polls composes to exactly the string it always did.
    ///
    /// This is what makes the richer shape safe to introduce: upgrading the host ahead of the
    /// adapters cannot change the value handed to an adapter for any statement it already runs,
    /// and the supervisor's fingerprint of that value does not move either - so no healthy
    /// connection is recycled by the deployment.
    /// </summary>
    [TestMethod]
    public void An_ordinary_statement_still_composes_to_a_bare_string()
    {
        var composed = StatementComposer.Compose(new[] { Statement(1, "getOrder", "select 1") },
            dataSourceId: 1);

        Assert.AreEqual("{\"getOrder\":\"select 1\"}", composed);
    }

    /// <summary>
    /// A polled statement carries the shape of its rows with it, because the cursor and key
    /// columns describe what the query returns rather than a choice its reader makes.
    /// </summary>
    [TestMethod]
    public void A_polled_statement_carries_its_cursor_and_key_columns()
    {
        var polled = Statement(1, "outbox", "select * from outbox where id > @cursor order by id");
        polled.CursorColumn = "id";
        polled.KeyColumn = "id";

        var composed = StatementComposer.Compose(new[] { polled, Statement(1, "getOrder", "select 1") },
            dataSourceId: 1);

        var parsed = Newtonsoft.Json.Linq.JObject.Parse(composed);

        // Side by side in one payload: the ordinary one a string, the polled one an object. An
        // adapter reads both, and only a receiver ever looks at the second form.
        Assert.AreEqual(Newtonsoft.Json.Linq.JTokenType.String, parsed["getOrder"].Type);
        Assert.AreEqual("id", parsed["outbox"].Value<string>("cursorColumn"));
        Assert.AreEqual("id", parsed["outbox"].Value<string>("keyColumn"));
        StringAssert.Contains(parsed["outbox"].Value<string>("sql"), "from outbox");
    }
}
