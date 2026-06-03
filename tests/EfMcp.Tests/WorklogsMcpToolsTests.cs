using System.Data.Common;
using EfMcp.AspNet.Data;
using EfMcp.AspNet.Models;
using EfMcp.AspNet.Services;
using EfMcp.AspNet.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        return r;
    }

    static IEntityContextAccessor CreateAccessor(EntityRegistry registry)
    {
        var a = new EntityContextAccessor();
        a.CurrentEntity = registry.Get("worklogs");
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

    sealed record EntityFixture(GenericSqlQueryTool Tools, ApplicationDbContext Db, DbConnection Connection)
    {
        public void Deconstruct(out GenericSqlQueryTool tools, out ApplicationDbContext db, out DbConnection connection)
        {
            tools = Tools;
            db = Db;
            connection = Connection;
        }
    }
}
