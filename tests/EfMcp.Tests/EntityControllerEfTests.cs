using EfMcp.AspNet.Data;
using EfMcp.AspNet.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EfMcp.Tests;

public class EntityControllerEfTests
{
    static async Task<(ApplicationDbContext Db, SqliteConnection Connection)> CreateFixture()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Worklogs.AddRange(
            new Worklogs { ResourceName = "Alice", Project = "Alpha", WorkDate = new DateOnly(2024, 1, 15), Hours = 8m, ApprovalStatus = "Approved" },
            new Worklogs { ResourceName = "Bob", Project = "Beta", WorkDate = new DateOnly(2024, 2, 20), Hours = 6.5m, ApprovalStatus = "Pending" }
        );
        await db.SaveChangesAsync();

        return (db, connection);
    }

    [Test]
    public async Task EfMetadata_ResolvesMappedTableName()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            var entityType = db.Model.FindEntityType(typeof(Worklogs))!;
            var tableName = entityType.GetTableName();
            Assert.That(tableName, Is.EqualTo("ResourceHours"));
        }
    }

    [Test]
    public async Task ExpenseReport_ResolvesMappedTableName()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            var entityType = db.Model.FindEntityType(typeof(ExpenseReport))!;
            var tableName = entityType.GetTableName();
            Assert.That(tableName, Is.EqualTo("ExpenseReports"));
        }
    }

    [Test]
    public async Task ExecuteDelete_ClearsAllRows()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            await db.Set<Worklogs>().ExecuteDeleteAsync();

            var remaining = await db.Set<Worklogs>().LongCountAsync();
            Assert.That(remaining, Is.Zero);
        }
    }

    [Test]
    public async Task Count_AfterDelete_ReturnsZero()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            Assert.That(await db.Set<Worklogs>().LongCountAsync(), Is.EqualTo(2));

            await db.Set<Worklogs>().ExecuteDeleteAsync();

            Assert.That(await db.Set<Worklogs>().LongCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task EfMetadata_TableNameMatchesRegistration()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            var entityType = db.Model.FindEntityType(typeof(Worklogs))!;
            var tableName = entityType.GetTableName();
            var schema = entityType.GetSchema();

            Assert.That(tableName, Is.EqualTo("ResourceHours"));
            Assert.That(schema, Is.Null);
        }
    }

    [Test]
    public async Task ExpenseReport_HasDistinctColumns()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            var entityType = db.Model.FindEntityType(typeof(ExpenseReport))!;
            var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();

            Assert.That(props, Does.Contain("Id"));
            Assert.That(props, Does.Contain("ResourceName"));
            Assert.That(props, Does.Contain("Category"));
            Assert.That(props, Does.Contain("Description"));
            Assert.That(props, Does.Contain("Amount"));
            Assert.That(props, Does.Contain("ExpenseDate"));
            Assert.That(props, Does.Contain("ApprovalStatus"));
        }
    }

    [Test]
    public async Task ExpenseReport_ExecuteDelete_IndependentFromWorklogs()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            db.ExpenseReports.AddRange(
                new ExpenseReport { ResourceName = "Charlie", Category = "Travel", Amount = 200m, ExpenseDate = new DateOnly(2025, 1, 1), ApprovalStatus = "Approved" }
            );
            await db.SaveChangesAsync();

            Assert.That(await db.Set<Worklogs>().LongCountAsync(), Is.EqualTo(2));
            Assert.That(await db.Set<ExpenseReport>().LongCountAsync(), Is.EqualTo(1));

            await db.Set<ExpenseReport>().ExecuteDeleteAsync();

            Assert.That(await db.Set<Worklogs>().LongCountAsync(), Is.EqualTo(2));
            Assert.That(await db.Set<ExpenseReport>().LongCountAsync(), Is.Zero);
        }
    }
}
