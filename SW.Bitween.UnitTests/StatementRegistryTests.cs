using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Adapters.Db;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The allow-list that keeps SQL in configuration and message content out of it.
///
/// This is the security boundary of the whole database provider, and it is small enough to test
/// without a database: a mapper is a Scriban template evaluated over an inbound payload, so if it
/// can emit SQL text then anyone who can get a message into Bitween can steer a statement against
/// the customer's database. Everything here is about that one rule.
/// </summary>
[TestClass]
public class StatementRegistryTests
{
    const string Configured = @"{
        ""getOrder"":    ""select * from orders where id = :id"",
        ""insertOrder"": ""insert into orders (id) values (:id)""
    }";

    [TestMethod]
    public void A_named_statement_resolves_to_its_sql()
    {
        var registry = new StatementRegistry(Configured);

        Assert.AreEqual("select * from orders where id = :id",
            registry.Resolve("getOrder", null, allowAdHoc: false));
    }

    [TestMethod]
    public void Names_are_matched_case_insensitively()
    {
        var registry = new StatementRegistry(Configured);

        Assert.AreEqual("select * from orders where id = :id",
            registry.Resolve("GETORDER", null, allowAdHoc: false));
    }

    /// <summary>The refusal that matters. Ad-hoc SQL is off by default and stays off.</summary>
    [TestMethod]
    public void Raw_sql_is_refused_unless_the_data_source_allows_it()
    {
        var registry = new StatementRegistry(Configured);

        var error = Assert.ThrowsException<InvalidOperationException>(() =>
            registry.Resolve(null, "drop table orders", allowAdHoc: false));

        StringAssert.Contains(error.Message, "does not allow ad-hoc SQL");
    }

    [TestMethod]
    public void Raw_sql_runs_when_the_data_source_opts_in()
    {
        var registry = new StatementRegistry(Configured);

        Assert.AreEqual("select 1 from dual",
            registry.Resolve(null, "select 1 from dual", allowAdHoc: true));
    }

    /// <summary>
    /// A name always wins over sql, even with ad-hoc allowed - otherwise a request carrying both
    /// would run whichever the implementation happened to check first.
    /// </summary>
    [TestMethod]
    public void A_name_beats_sql_sent_alongside_it()
    {
        var registry = new StatementRegistry(Configured);

        Assert.AreEqual("select * from orders where id = :id",
            registry.Resolve("getOrder", "select * from something_else", allowAdHoc: true));
    }

    /// <summary>An unknown name lists what does exist: the fix is almost always a typo away.</summary>
    [TestMethod]
    public void An_unknown_name_names_the_ones_that_exist()
    {
        var registry = new StatementRegistry(Configured);

        var error = Assert.ThrowsException<InvalidOperationException>(() =>
            registry.Resolve("getOrders", null, allowAdHoc: true));

        StringAssert.Contains(error.Message, "getOrder");
        StringAssert.Contains(error.Message, "insertOrder");
    }

    [TestMethod]
    public void No_statements_configured_says_so_rather_than_listing_nothing()
    {
        var registry = new StatementRegistry(null);

        var error = Assert.ThrowsException<InvalidOperationException>(() =>
            registry.Resolve("anything", null, allowAdHoc: false));

        StringAssert.Contains(error.Message, "none");
        Assert.AreEqual(0, registry.Count);
    }

    /// <summary>
    /// Bad JSON fails at startup, where the message can point at the Statements setting - not on
    /// the first message, as "unknown statement", which sends whoever is debugging it looking at
    /// the subscription instead of the data source.
    /// </summary>
    [TestMethod]
    public void Malformed_json_fails_with_the_setting_named()
    {
        var error = Assert.ThrowsException<ArgumentException>(() =>
            new StatementRegistry("{ not json"));

        StringAssert.Contains(error.Message, "Statements setting");
    }

    /// <summary>
    /// The object form, which a statement a receiver polls with uses: the SQL plus the columns
    /// that say which is the cursor and which identifies a row. It used to be rejected outright,
    /// because there was only one shape.
    /// </summary>
    [TestMethod]
    public void A_statement_may_also_carry_the_shape_of_its_rows()
    {
        var registry = new StatementRegistry(
            @"{ ""outbox"": { ""sql"": ""select 1"", ""cursorColumn"": ""id"", ""keyColumn"": ""id"" } }");

        var statement = registry.Find("outbox");

        Assert.AreEqual("select 1", statement.Sql);
        Assert.AreEqual("id", statement.CursorColumn);
        Assert.AreEqual("id", statement.KeyColumn);

        // And it is still just SQL to everything that only wants SQL.
        Assert.AreEqual("select 1", registry.Resolve("outbox", null, allowAdHoc: false));
    }

    [TestMethod]
    public void An_object_without_sql_is_rejected()
    {
        // Named rather than quietly registered as an empty statement, which would fail later as
        // a database syntax error with nothing to connect it back to the configuration.
        var error = Assert.ThrowsException<ArgumentException>(() =>
            new StatementRegistry(@"{ ""getOrder"": { ""cursorColumn"": ""id"" } }"));

        StringAssert.Contains(error.Message, "getOrder");
    }

    [TestMethod]
    public void A_statement_that_is_neither_a_string_nor_an_object_is_rejected()
    {
        var error = Assert.ThrowsException<ArgumentException>(() =>
            new StatementRegistry(@"{ ""getOrder"": 42 }"));

        StringAssert.Contains(error.Message, "getOrder");
    }
}
