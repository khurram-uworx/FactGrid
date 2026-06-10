using FactGrid.AspNet.Data;
using FactGrid.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FactGrid.Tests;

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
            new Worklog { ResourceName = "Alice", Project = "Alpha", WorkDate = new DateOnly(2024, 1, 15), Hours = 8m, ApprovalStatus = "Approved" },
            new Worklog { ResourceName = "Bob", Project = "Beta", WorkDate = new DateOnly(2024, 2, 20), Hours = 6.5m, ApprovalStatus = "Pending" }
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
            var entityType = db.Model.FindEntityType(typeof(Worklog))!;
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
            var entityType = db.Model.FindEntityType(typeof(Expense))!;
            var tableName = entityType.GetTableName();
            Assert.That(tableName, Is.EqualTo("Expenses"));
        }
    }

    [Test]
    public async Task ExecuteDelete_ClearsAllRows()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            await db.Set<Worklog>().ExecuteDeleteAsync();

            var remaining = await db.Set<Worklog>().LongCountAsync();
            Assert.That(remaining, Is.Zero);
        }
    }

    [Test]
    public async Task Count_AfterDelete_ReturnsZero()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            Assert.That(await db.Set<Worklog>().LongCountAsync(), Is.EqualTo(2));

            await db.Set<Worklog>().ExecuteDeleteAsync();

            Assert.That(await db.Set<Worklog>().LongCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task EfMetadata_TableNameMatchesRegistration()
    {
        var (db, conn) = await CreateFixture();
        await using (conn)
        {
            var entityType = db.Model.FindEntityType(typeof(Worklog))!;
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
            var entityType = db.Model.FindEntityType(typeof(Expense))!;
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
            db.Expenses.AddRange(
                new Expense { ResourceName = "Charlie", Category = "Travel", Amount = 200m, ExpenseDate = new DateOnly(2025, 1, 1), ApprovalStatus = "Approved" }
            );
            await db.SaveChangesAsync();

            Assert.That(await db.Set<Worklog>().LongCountAsync(), Is.EqualTo(2));
            Assert.That(await db.Set<Expense>().LongCountAsync(), Is.EqualTo(1));

            await db.Set<Expense>().ExecuteDeleteAsync();

            Assert.That(await db.Set<Worklog>().LongCountAsync(), Is.EqualTo(2));
            Assert.That(await db.Set<Expense>().LongCountAsync(), Is.Zero);
        }
    }
}

[TestFixture]
public class EntityPendingModelTests
{
    [Test]
    public async Task Database_HasNoPendingModelChanges()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (connection)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .UseOpenIddict()
                .Options;

            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();

            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
        }
    }
}

[TestFixture]
public class EntityControllerIntegrationTests
{
    static SqliteConnection _connection = null!;
    static WebApplicationFactory<Program> _factory = null!;
    static HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptorsToRemove = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                                    || d.ServiceType == typeof(ApplicationDbContext))
                        .ToList();

                    foreach (var d in descriptorsToRemove)
                        services.Remove(d);

                    services.AddDbContext<ApplicationDbContext>(options =>
                    {
                        options.UseSqlite(_connection);
                        options.UseOpenIddict();
                    });
                });
            });

        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
    }

    void SeedData()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Worklogs.ExecuteDelete();
        db.Expenses.ExecuteDelete();
        db.Worklogs.AddRange(
            new Worklog { ResourceName = "Alice", Project = "Alpha", WorkDate = new DateOnly(2024, 1, 15), Hours = 8m, ApprovalStatus = "Approved" },
            new Worklog { ResourceName = "Bob", Project = "Beta", WorkDate = new DateOnly(2024, 2, 20), Hours = 6.5m, ApprovalStatus = "Pending" }
        );
        db.Expenses.AddRange(
            new Expense { ResourceName = "Charlie", Category = "Travel", Amount = 200m, ExpenseDate = new DateOnly(2025, 1, 1), ApprovalStatus = "Approved" }
        );
        db.SaveChanges();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Test]
    public async Task Detail_ShowsRecordCount()
    {
        SeedData();
        var response = await _client.GetAsync("/Entity/worklogs");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(body, Does.Contain("2"));
        Assert.That(body, Does.Contain("record(s)"));
        Assert.That(body, Does.Contain("ResourceHours"));
    }

    [Test]
    public async Task Detail_UnknownEntity_Returns404()
    {
        var response = await _client.GetAsync("/Entity/nonexistent");

        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task DeleteAll_RemovesOnlyTargetEntityRows()
    {
        SeedData();
        var response = await _client.PostAsync("/Entity/worklogs/DeleteAll", null);

        Assert.That(response.IsSuccessStatusCode, Is.True);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("All records deleted."));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(await db.Set<Worklog>().LongCountAsync(), Is.Zero);
        Assert.That(await db.Set<Expense>().LongCountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteAll_Expenses_DoesNotAffectWorklogs()
    {
        SeedData();
        var response = await _client.PostAsync("/Entity/expenses/DeleteAll", null);

        Assert.That(response.IsSuccessStatusCode, Is.True);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.That(await db.Set<Expense>().LongCountAsync(), Is.Zero);
        Assert.That(await db.Set<Worklog>().LongCountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task DeleteAll_UnknownEntity_Returns404()
    {
        var response = await _client.PostAsync("/Entity/nonexistent/DeleteAll", null);

        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }
}
