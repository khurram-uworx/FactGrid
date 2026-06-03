using System.Data.Common;
using EfMcp.AspNet.Data;
using EfMcp.AspNet.Models;
using EfMcp.AspNet.Services;
using EfMcp.AspNet.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EfMcp.Tests;

public class WorklogsMcpToolsTests
{
    private async Task<(WorklogsMcpTools Tools, ApplicationDbContext Db)> CreateFixture()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<QueryValidationService>();

        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();

        db.Worklogs.AddRange(
            new Worklogs { ResourceName = "Alice", Project = "Alpha", WorkDate = new DateOnly(2024, 1, 15), Hours = 8m, ApprovalStatus = "Approved" },
            new Worklogs { ResourceName = "Bob", Project = "Beta", WorkDate = new DateOnly(2024, 2, 20), Hours = 6.5m, ApprovalStatus = "Pending" },
            new Worklogs { ResourceName = "Charlie", Project = "Gamma", WorkDate = new DateOnly(2024, 3, 10), Hours = 7.25m, ApprovalStatus = "Approved" }
        );
        await db.SaveChangesAsync();

        var tools = new WorklogsMcpTools(
            provider.GetRequiredService<QueryValidationService>(),
            provider);

        return (tools, db);
    }

    [Test]
    public async Task SqlQueryAsync_ValidQuery_ReturnsMarkdownTable()
    {
        var (tools, _) = await CreateFixture();

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
        var (tools, _) = await CreateFixture();

        var result = await tools.SqlQueryAsync("SELECT * FROM ResourceHours", 1);

        var rows = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var dataRows = rows.Count(r => r.StartsWith('|') && !r.Contains("---"));
        Assert.That(dataRows - 1, Is.EqualTo(1));
    }

    [Test]
    public async Task SqlQueryAsync_InvalidQuery_ReturnsError()
    {
        var (tools, _) = await CreateFixture();

        var result = await tools.SqlQueryAsync("DELETE FROM ResourceHours", 10);

        Assert.That(result, Does.StartWith("Error:"));
    }

    [Test]
    public async Task SqlQueryAsync_NoResults_ReturnsNoResults()
    {
        var (tools, _) = await CreateFixture();

        var result = await tools.SqlQueryAsync("SELECT * FROM ResourceHours WHERE ResourceName = 'Nobody'", 10);

        Assert.That(result, Does.Contain("No results"));
    }

    [Test]
    public async Task DescribeAsync_ReturnsColumnInfo()
    {
        var (tools, _) = await CreateFixture();

        var result = await tools.DescribeAsync();

        Assert.That(result, Does.StartWith("| Column | Type | Description |"));
        Assert.That(result, Does.Contain("ResourceName"));
        Assert.That(result, Does.Contain("NVARCHAR"));
        Assert.That(result, Does.Contain("WorkDate"));
        Assert.That(result, Does.Contain("DATE"));
        Assert.That(result, Does.Contain("the resource or person"));
    }
}
