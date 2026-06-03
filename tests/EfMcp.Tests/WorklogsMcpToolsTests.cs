using EfMcp.AspNet.Data;
using EfMcp.AspNet.Models;
using EfMcp.AspNet.Services;
using EfMcp.AspNet.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace EfMcp.Tests;

public class GenericSqlQueryToolTests
{
    static EntityRegistry CreateRegistry()
    {
        var r = new EntityRegistry();
        r.Register<Worklogs>(new EntityRegistration(
            EntityName: "worklogs",
            DisplayName: "Worklogs",
            ModelType: typeof(Worklogs),
            ExcelParserType: typeof(WorklogsExcelParser),
            TableName: "ResourceHours",
            Description: "Employee worklog entries"
        ));
        r.Register<ExpenseReport>(new EntityRegistration(
            EntityName: "expenses",
            DisplayName: "Expense Reports",
            ModelType: typeof(ExpenseReport),
            ExcelParserType: typeof(ExpenseReportExcelParser),
            TableName: "ExpenseReports",
            Description: "Employee expense report entries"
        ));
        return r;
    }

    static IEntityContextAccessor CreateAccessor(EntityRegistry registry, string entityName = "worklogs")
    {
        var a = new EntityContextAccessor();
        a.CurrentEntity = registry.Get(entityName);
        return a;
    }

    static EntityFixture CreateFixture()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<QueryValidationService>();

        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        db.Worklogs.AddRange(
            new Worklogs { ResourceName = "Alice", Project = "Alpha", WorkDate = new DateOnly(2024, 1, 15), Hours = 8m, ApprovalStatus = "Approved" },
            new Worklogs { ResourceName = "Bob", Project = "Beta", WorkDate = new DateOnly(2024, 2, 20), Hours = 6.5m, ApprovalStatus = "Pending" },
            new Worklogs { ResourceName = "Charlie", Project = "Gamma", WorkDate = new DateOnly(2024, 3, 10), Hours = 7.25m, ApprovalStatus = "Approved" }
        );
        db.SaveChanges();

        var registry = CreateRegistry();
        var accessor = CreateAccessor(registry);

        return new EntityFixture(
            new GenericSqlQueryTool(
                provider.GetRequiredService<QueryValidationService>(),
                provider,
                accessor),
            db,
            connection
        );
    }

    [Test]
    public async Task SqlQueryAsync_ValidQuery_ReturnsMarkdownTable()
    {
        var (tools, _, _) = CreateFixture();

        var result = await tools.SqlQueryAsync("SELECT ResourceName, Hours FROM ResourceHours ORDER BY ResourceName", 10);

        Assert.That(result, Does.StartWith("|"));
        Assert.That(result, Does.Contain("ResourceName"));
        Assert.That(result, Does.Contain("Alice"));
        Assert.That(result, Does.Contain("Bob"));
        Assert.That(result, Does.Contain("Charlie"));
    }

    [Test]
    public async Task SqlQueryAsync_MaxResults_IsCapped()
    {
        var (tools, _, _) = CreateFixture();

        var result = await tools.SqlQueryAsync("SELECT * FROM ResourceHours", 1);

        var rows = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var dataRows = rows.Count(r => r.StartsWith('|') && !r.Contains("---"));
        Assert.That(dataRows - 1, Is.EqualTo(1));
    }

    [Test]
    public async Task SqlQueryAsync_InvalidQuery_ReturnsError()
    {
        var (tools, _, _) = CreateFixture();

        var result = await tools.SqlQueryAsync("DELETE FROM ResourceHours", 10);

        Assert.That(result, Does.StartWith("Error:"));
    }

    [Test]
    public async Task SqlQueryAsync_NoResults_ReturnsNoResults()
    {
        var (tools, _, _) = CreateFixture();

        var result = await tools.SqlQueryAsync("SELECT * FROM ResourceHours WHERE ResourceName = 'Nobody'", 10);

        Assert.That(result, Does.Contain("No results"));
    }

    [Test]
    public async Task SqlQueryAsync_NoEntityContext_ReturnsError()
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<QueryValidationService>();
        var provider = services.BuildServiceProvider();

        var tools = new GenericSqlQueryTool(
            provider.GetRequiredService<QueryValidationService>(),
            provider,
            new EntityContextAccessor());

        var result = await tools.SqlQueryAsync("SELECT * FROM ResourceHours", 10);

        Assert.That(result, Does.Contain("No entity context"));
    }

    [Test]
    public async Task SqlQueryAsync_NonScopedTable_ReturnsError()
    {
        var (tools, _, _) = CreateFixture();

        var result = await tools.SqlQueryAsync("SELECT * FROM OtherTable", 10);

        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("OtherTable"));
        Assert.That(result, Does.Contain("ResourceHours"));
    }

    [Test]
    public async Task SqlQueryAsync_ScopedSubqueryOtherTable_ReturnsError()
    {
        var (tools, _, _) = CreateFixture();

        var result = await tools.SqlQueryAsync("SELECT * FROM ResourceHours WHERE Id IN (SELECT Id FROM OtherTable)", 10);

        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("OtherTable"));
    }

    [Test]
    public async Task DescribeAsync_ReturnsColumnInfo()
    {
        var (tools, _, _) = CreateFixture();

        var result = await tools.DescribeAsync();

        Assert.That(result, Does.StartWith("# Worklogs"));
        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Contain("ResourceName"));
        Assert.That(result, Does.Contain("NVARCHAR"));
        Assert.That(result, Does.Contain("WorkDate"));
        Assert.That(result, Does.Contain("DATE"));
        Assert.That(result, Does.Contain("the resource or person"));
    }

    [Test]
    public async Task DescribeAsync_NoEntityContext_ReturnsError()
    {
        var services = new ServiceCollection();
        services.AddSingleton<QueryValidationService>();
        var provider = services.BuildServiceProvider();

        var tools = new GenericSqlQueryTool(
            provider.GetRequiredService<QueryValidationService>(),
            provider,
            new EntityContextAccessor());

        var result = await tools.DescribeAsync();

        Assert.That(result, Does.Contain("No entity context"));
    }

    static ExpenseFixture CreateExpenseFixture()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<QueryValidationService>();

        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        db.ExpenseReports.AddRange(
            new ExpenseReport { ResourceName = "Alice", Category = "Travel", Amount = 450m, ExpenseDate = new DateOnly(2025, 3, 15), ApprovalStatus = "Approved" },
            new ExpenseReport { ResourceName = "Bob", Category = "Meals", Amount = 35.50m, ExpenseDate = new DateOnly(2025, 4, 1), ApprovalStatus = "Pending" }
        );
        db.SaveChanges();

        var registry = CreateRegistry();
        var accessor = CreateAccessor(registry, "expenses");

        return new ExpenseFixture(
            new GenericSqlQueryTool(
                provider.GetRequiredService<QueryValidationService>(),
                provider,
                accessor),
            db,
            connection
        );
    }

    [Test]
    public async Task DescribeAsync_ExpensesEntity_ShowsExpenseColumns()
    {
        var (tools, _, _) = CreateExpenseFixture();

        var result = await tools.DescribeAsync();

        Assert.That(result, Does.StartWith("# Expense Reports"));
        Assert.That(result, Does.Contain("ExpenseReports"));
        Assert.That(result, Does.Contain("ResourceName"));
        Assert.That(result, Does.Contain("Category"));
        Assert.That(result, Does.Contain("Amount"));
        Assert.That(result, Does.Contain("ExpenseDate"));
        Assert.That(result, Does.Contain("the expense"));
    }

    [Test]
    public async Task SqlQueryAsync_ExpenseEntity_ReturnsResults()
    {
        var (tools, _, _) = CreateExpenseFixture();

        var result = await tools.SqlQueryAsync("SELECT ResourceName, Amount FROM ExpenseReports ORDER BY ResourceName", 10);

        Assert.That(result, Does.StartWith("|"));
        Assert.That(result, Does.Contain("ResourceName"));
        Assert.That(result, Does.Contain("Alice"));
        Assert.That(result, Does.Contain("Bob"));
    }

    [Test]
    public async Task SqlQueryAsync_ExpenseScoped_RejectsOtherTable()
    {
        var (tools, _, _) = CreateExpenseFixture();

        var result = await tools.SqlQueryAsync("SELECT * FROM ResourceHours", 10);

        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Contain("ExpenseReports"));
    }

    [Test]
    public async Task Counts_AreIndependent_BetweenEntities()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<QueryValidationService>();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        db.Worklogs.AddRange(
            new Worklogs { ResourceName = "W1", Project = "P1", WorkDate = new DateOnly(2024, 1, 1), Hours = 8m, ApprovalStatus = "A" },
            new Worklogs { ResourceName = "W2", Project = "P2", WorkDate = new DateOnly(2024, 2, 1), Hours = 6m, ApprovalStatus = "A" }
        );
        db.ExpenseReports.AddRange(
            new ExpenseReport { ResourceName = "E1", Category = "C1", Amount = 100m, ExpenseDate = new DateOnly(2025, 1, 1), ApprovalStatus = "A" }
        );
        db.SaveChanges();

        Assert.That(await db.Set<Worklogs>().LongCountAsync(), Is.EqualTo(2));
        Assert.That(await db.Set<ExpenseReport>().LongCountAsync(), Is.EqualTo(1));
    }

    sealed record EntityFixture(GenericSqlQueryTool Tools, ApplicationDbContext Db, DbConnection Connection)
    {
        public void Deconstruct(out GenericSqlQueryTool tools, out ApplicationDbContext db, out DbConnection connection)
        {
            tools = Tools;
            db = Db;
            connection = Connection;
        }
    }

    sealed record ExpenseFixture(GenericSqlQueryTool Tools, ApplicationDbContext Db, DbConnection Connection)
    {
        public void Deconstruct(out GenericSqlQueryTool tools, out ApplicationDbContext db, out DbConnection connection)
        {
            tools = Tools;
            db = Db;
            connection = Connection;
        }
    }
}
