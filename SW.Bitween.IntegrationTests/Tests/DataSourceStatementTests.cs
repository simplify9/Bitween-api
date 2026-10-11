using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// SQL statements as their own entity.
///
/// They have to live on the connection rather than on a subscription, because a subscription's
/// adapter properties have partner values templated into them before the adapter sees them - SQL
/// there would be an injection surface fed by ordinary partner data. But keeping them in a field on
/// the data source meant that writing a query needed the same right as changing the credentials.
///
/// So: their own entity, their own permission, their own audit trail, and a usage count. These
/// tests cover the four things that separation is supposed to buy - namespacing, safe deletion,
/// knowing what uses what, and the adapter contract staying exactly as it was.
/// </summary>
[Collection("Bitween")]
public class DataSourceStatementTests(BitweenFixture fixture)
{
    // ---------------------------------------------------------------- namespacing

    /// <summary>
    /// A collision is an error at save time, not a silent overwrite. Case-insensitively, because
    /// the adapter resolves names that way - allowing getOrder and GetOrder to coexist would make
    /// which one runs a matter of dictionary ordering.
    /// </summary>
    [Fact]
    public async Task A_duplicate_name_is_refused_whatever_its_case()
    {
        var dataSourceId = await CreateRelationalAsync();
        await CreateStatementAsync(dataSourceId, "getOrder", "select 1 from dual");

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateStatementAsync(dataSourceId, "GETORDER", "select 2 from dual"));

        Assert.Contains("already has a statement called", error.Message);
    }

    /// <summary>The same name on a different connection is a different statement, and allowed.</summary>
    [Fact]
    public async Task The_same_name_on_another_data_source_is_fine()
    {
        var first = await CreateRelationalAsync();
        var second = await CreateRelationalAsync();

        await CreateStatementAsync(first, "getOrder", "select 1 from dual");
        var id = await CreateStatementAsync(second, "getOrder", "select 2 from dual");

        Assert.True(id > 0);
    }

    /// <summary>
    /// Statements only mean something to a database. Refused rather than stored on a broker,
    /// because configuration nothing will ever read is how people conclude a feature is broken.
    /// </summary>
    [Fact]
    public async Task A_statement_on_a_broker_data_source_is_refused()
    {
        var brokerId = await CreateDataSourceAsync(DataSourceKind.Broker);

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateStatementAsync(brokerId, "getOrder", "select 1"));

        Assert.Contains("statements only mean something to a Relational one", error.Message);
    }

    // ---------------------------------------------------------------- usage

    /// <summary>
    /// Which subscriptions name it, in which slot. This is the answer that decides whether a
    /// statement can be changed, and without it nobody ever dares.
    /// </summary>
    [Fact]
    public async Task Usage_reports_the_subscriptions_that_name_the_statement()
    {
        var dataSourceId = await CreateRelationalAsync();
        var statementId = await CreateStatementAsync(dataSourceId, "insertOrder",
            "insert into orders (id) values (:id)");

        var subscriptionId = await CreateSubscriptionAsync(dataSourceId,
            handlerProperties: new Dictionary<string, string>
            {
                ["Statement"] = "insertOrder",
                ["Operation"] = "execute"
            });

        var usage = await UsageAsync(statementId);

        var entry = Assert.Single(usage.UsedBy);
        Assert.Equal(subscriptionId, entry.SubscriptionId);
        Assert.Equal("Handler", entry.Role);
        Assert.Equal("execute", entry.Operation);
    }

    /// <summary>
    /// Zero usage is the interesting number - the only way to tell dead SQL from SQL that is
    /// merely quiet, which is what stopped anyone cleaning up the JSON blob this replaced.
    /// </summary>
    [Fact]
    public async Task An_unused_statement_reports_no_usage()
    {
        var dataSourceId = await CreateRelationalAsync();
        var statementId = await CreateStatementAsync(dataSourceId, "neverCalled", "select 1 from dual");

        var usage = await UsageAsync(statementId);

        Assert.Empty(usage.UsedBy);
    }

    /// <summary>
    /// A subscription bound to ANOTHER data source that happens to use the same statement name is
    /// not a user of this one - statements are scoped to their connection.
    /// </summary>
    [Fact]
    public async Task Usage_does_not_count_a_subscription_on_a_different_data_source()
    {
        var mine = await CreateRelationalAsync();
        var theirs = await CreateRelationalAsync();

        var statementId = await CreateStatementAsync(mine, "shared", "select 1 from dual");
        await CreateStatementAsync(theirs, "shared", "select 2 from dual");

        await CreateSubscriptionAsync(theirs,
            handlerProperties: new Dictionary<string, string> { ["Statement"] = "shared" });

        Assert.Empty((await UsageAsync(statementId)).UsedBy);
    }

    // ---------------------------------------------------------------- safe change

    /// <summary>
    /// Deleting out from under a live subscription does not fail at delete time - it fails on the
    /// next message, as "not a statement this data source defines", somewhere nobody is watching.
    /// So it is refused here, and the refusal names what is still using it.
    /// </summary>
    [Fact]
    public async Task Deleting_a_statement_in_use_is_refused_and_says_by_what()
    {
        var dataSourceId = await CreateRelationalAsync();
        var statementId = await CreateStatementAsync(dataSourceId, "inUse", "select 1 from dual");

        await CreateSubscriptionAsync(dataSourceId,
            handlerProperties: new Dictionary<string, string> { ["Statement"] = "inUse" },
            name: "the-one-using-it");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => DeleteAsync(statementId));

        Assert.Contains("is named by 1 subscription", error.Message);
        Assert.Contains("the-one-using-it", error.Message);
    }

    [Fact]
    public async Task Deleting_an_unused_statement_works()
    {
        var dataSourceId = await CreateRelationalAsync();
        var statementId = await CreateStatementAsync(dataSourceId, "unused", "select 1 from dual");

        await DeleteAsync(statementId);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<DataSourceStatement>().AnyAsync(s => s.Id == statementId));
    }

    /// <summary>
    /// A rename breaks every subscription naming the old one, and nothing here can fix that - the
    /// subscription's properties are its own. Changing the SQL is fine; changing the name is not.
    /// </summary>
    [Fact]
    public async Task Renaming_a_statement_in_use_is_refused_but_editing_its_sql_is_not()
    {
        var dataSourceId = await CreateRelationalAsync();
        var statementId = await CreateStatementAsync(dataSourceId, "keepThisName", "select 1 from dual");

        await CreateSubscriptionAsync(dataSourceId,
            handlerProperties: new Dictionary<string, string> { ["Statement"] = "keepThisName" });

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            UpdateAsync(statementId, new DataSourceStatementUpdate
            {
                Name = "aDifferentName",
                Sql = "select 1 from dual"
            }));

        Assert.Contains("cannot be renamed while", error.Message);

        // The SQL itself is editable, which is the common case - a column was added, a join fixed.
        await UpdateAsync(statementId, new DataSourceStatementUpdate
        {
            Name = "keepThisName",
            Sql = "select 2 from dual"
        });

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var stored = await db.Set<DataSourceStatement>().AsNoTracking()
            .FirstAsync(s => s.Id == statementId);

        Assert.Equal("select 2 from dual", stored.Sql);
    }

    // ---------------------------------------------------------------- audit

    /// <summary>
    /// An audit trail is half the reason this is an entity: a JSON field records that "someone
    /// changed the statements", which is not an answer to "who changed this query, and when".
    /// </summary>
    [Fact]
    public async Task A_statement_carries_who_created_it_and_when()
    {
        var dataSourceId = await CreateRelationalAsync();
        var statementId = await CreateStatementAsync(dataSourceId, "audited", "select 1 from dual");

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var stored = await db.Set<DataSourceStatement>().AsNoTracking()
            .FirstAsync(s => s.Id == statementId);

        Assert.NotEqual(default, stored.CreatedOn);
    }

    // ---------------------------------------------------------------- helpers

    async Task<int> CreateRelationalAsync() => await CreateDataSourceAsync(DataSourceKind.Relational);

    async Task<int> CreateDataSourceAsync(DataSourceKind kind)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var dataSource = new DataSource
        {
            Name = $"ds-{Guid.NewGuid():N}",
            AdapterId = kind == DataSourceKind.Relational ? "bitween.db.oracle" : BusAdapters.RabbitMq,
            Kind = kind,
            Inactive = true
        };

        db.Add(dataSource);
        await db.SaveChangesAsync();
        return dataSource.Id;
    }

    async Task<int> CreateStatementAsync(int dataSourceId, string name, string sql)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSourceStatements.Create>(
            scope.ServiceProvider);

        // The handler answers with { Id, Checked } - the id, and whether the database actually
        // looked at the SQL. Read through the property rather than cast, because the shape is an
        // anonymous type and a cast to int is what this used to do.
        var created = await handler.Handle(
            new DataSourceStatementCreate { DataSourceId = dataSourceId, Name = name, Sql = sql });

        return (int)created.GetType().GetProperty("Id")!.GetValue(created)!;
    }

    async Task UpdateAsync(int id, DataSourceStatementUpdate model)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSourceStatements.Update>(
            scope.ServiceProvider);

        await handler.Handle(id, model);
    }

    async Task DeleteAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSourceStatements.Delete>(
            scope.ServiceProvider);

        await handler.Handle(id);
    }

    async Task<DataSourceStatementUsage> UsageAsync(int id)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.DataSourceStatements.Usage>(
            scope.ServiceProvider);

        return (DataSourceStatementUsage)await handler.Handle(id, new DataSourceStatementUsageRequest());
    }

    async Task<int> CreateSubscriptionAsync(int dataSourceId,
        Dictionary<string, string> handlerProperties, string name = null)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        // Document names are capped at 100 and subscription names at 100, so a raw guid fits -
        // the earlier attempt to trim to 40 was slicing a 36-character string.
        var document = new Document(null, $"doc-{Guid.NewGuid():N}", DocumentFormat.Json);
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription(name ?? $"sub-{Guid.NewGuid():N}", document.Id)
        {
            DataSourceId = dataSourceId,
            HandlerId = "bitween.db.oracle"
        };

        subscription.SetDictionaries(handlerProperties, new Dictionary<string, string>(),
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, string>());

        db.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }
}
