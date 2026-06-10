using FactGrid.AspNet.Data;
using FactGrid.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FactGrid.Tests;

[TestFixture]
public class McpEndpointTests
{
    static SqliteConnection _connection = null!;
    static WebApplicationFactory<Program> _factory = null!;
    static HttpClient _client = null!;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Auth:ApiKey", "test-api-key");

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
        _client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        db.Worklogs.AddRange(
            new Worklog { ResourceName = "Alice", Project = "Alpha", WorkDate = new DateOnly(2024, 1, 15), Hours = 8m, ApprovalStatus = "Approved" },
            new Worklog { ResourceName = "Bob", Project = "Beta", WorkDate = new DateOnly(2024, 2, 20), Hours = 6.5m, ApprovalStatus = "Pending" },
            new Worklog { ResourceName = "Charlie", Project = "Gamma", WorkDate = new DateOnly(2024, 3, 10), Hours = 7.25m, ApprovalStatus = "Approved" }
        );
        db.Expenses.AddRange(
            new Expense { ResourceName = "Alice", Category = "Travel", Amount = 450m, ExpenseDate = new DateOnly(2025, 3, 15), ApprovalStatus = "Approved" },
            new Expense { ResourceName = "Bob", Category = "Meals", Amount = 35.50m, ExpenseDate = new DateOnly(2025, 4, 1), ApprovalStatus = "Pending" }
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

    static async Task<(HttpResponseMessage Response, JsonElement Json)> PostJsonAsync(string path, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = content,
            Headers = { { "Accept", "application/json, text/event-stream" } }
        };
        var response = await _client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            var jsonRpcPart = ParseSseJson(responseBody);
            if (jsonRpcPart is not null)
                return (response, JsonSerializer.Deserialize<JsonElement>(jsonRpcPart));
        }

        var doc = string.IsNullOrEmpty(responseBody)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(responseBody);
        return (response, doc);
    }

    static string? ParseSseJson(string sse)
    {
        string? json = null;
        foreach (var line in sse.Split('\n'))
        {
            if (line.StartsWith("data: "))
            {
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;
                json = data;
            }
        }
        return json;
    }

    static JsonElement GetResult(JsonElement doc) => doc.GetProperty("result");

    static string GetContentText(JsonElement result)
    {
        var content = result.GetProperty("content");
        return content[0].GetProperty("text").GetString()!;
    }

    static string GetResourceText(JsonElement result)
    {
        var contents = result.GetProperty("contents");
        return contents[0].GetProperty("text").GetString()!;
    }

    static string GetPromptText(JsonElement result)
    {
        var messages = result.GetProperty("messages");
        return messages[0].GetProperty("content").GetProperty("text").GetString()!;
    }

    static async Task<string> CallToolAsync(string path, string toolName, object? args = null)
    {
        var (response, doc) = await PostJsonAsync(path, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = toolName, arguments = args }
        });
        response.EnsureSuccessStatusCode();
        return GetContentText(GetResult(doc));
    }

    static async Task<string> CallSqlQueryAsync(string path, string query, int maxResults = 10)
        => await CallToolAsync(path, "sql_query", new { query, maxResults });

    static async Task<string> CallDescribeAsync(string path)
        => await CallToolAsync(path, "describe");

    static async Task<string> CallResourceReadAsync(string path, string uri)
    {
        var (response, doc) = await PostJsonAsync(path, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "resources/read",
            @params = new { uri }
        });
        response.EnsureSuccessStatusCode();
        return GetResourceText(GetResult(doc));
    }

    static async Task<string> CallPromptGetAsync(string path, string name)
    {
        var (response, doc) = await PostJsonAsync(path, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "prompts/get",
            @params = new { name }
        });
        response.EnsureSuccessStatusCode();
        return GetPromptText(GetResult(doc));
    }

    [Test]
    public async Task Global_ToolsCall_SqlQuery_ReturnsResults()
    {
        var result = await CallSqlQueryAsync("/api/mcp",
            "SELECT ResourceName, Hours FROM ResourceHours ORDER BY ResourceName", 10);

        Assert.That(result, Does.StartWith("|"));
        Assert.That(result, Does.Contain("Alice"));
        Assert.That(result, Does.Contain("Bob"));
        Assert.That(result, Does.Contain("Charlie"));
    }

    [Test]
    public async Task Global_ToolsCall_Describe_ReturnsAllEntities()
    {
        var result = await CallDescribeAsync("/api/mcp");

        Assert.That(result, Does.Contain("# Worklogs"));
        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Contain("# Expenses"));
        Assert.That(result, Does.Contain("Expenses"));
    }

    [Test]
    public async Task Global_ResourcesRead_ReturnsAllEntities()
    {
        var result = await CallResourceReadAsync("/api/mcp", "entities://list");

        Assert.That(result, Does.Contain("worklogs"));
        Assert.That(result, Does.Contain("expenses"));
        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Contain("Expenses"));
    }

    [Test]
    public async Task Global_PromptsGet_ReturnsOverview()
    {
        var result = await CallPromptGetAsync("/api/mcp", "entities-guide");

        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Contain("Expenses"));
        Assert.That(result, Does.Contain("global scope"));
        Assert.That(result, Does.Contain("sql_query"));
    }

    [Test]
    public async Task Global_SqlQuery_RejectsUnregisteredTable()
    {
        var result = await CallSqlQueryAsync("/api/mcp", "SELECT * FROM AspNetUsers", 10);

        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("AspNetUsers"));
    }

    [Test]
    public async Task Scoped_Worklogs_SqlQuery_ReturnsResults()
    {
        var result = await CallSqlQueryAsync("/api/mcp/worklogs",
            "SELECT ResourceName, Hours FROM ResourceHours ORDER BY ResourceName", 10);

        Assert.That(result, Does.StartWith("|"));
        Assert.That(result, Does.Contain("Alice"));
        Assert.That(result, Does.Contain("Bob"));
    }

    [Test]
    public async Task Scoped_Worklogs_Describe_ReturnsWorklogOnly()
    {
        var result = await CallDescribeAsync("/api/mcp/worklogs");

        Assert.That(result, Does.Contain("# Worklogs"));
        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Not.Contain("# Expense Reports"));
        Assert.That(result, Does.Not.Contain("Expenses"));
    }

    [Test]
    public async Task Scoped_Expenses_SqlQuery_ReturnsResults()
    {
        var result = await CallSqlQueryAsync("/api/mcp/expenses",
            "SELECT ResourceName, Amount FROM Expenses ORDER BY ResourceName", 10);

        Assert.That(result, Does.StartWith("|"));
        Assert.That(result, Does.Contain("Alice"));
        Assert.That(result, Does.Contain("Bob"));
    }

    [Test]
    public async Task Scoped_SqlQuery_RejectsCrossEntity()
    {
        var result = await CallSqlQueryAsync("/api/mcp/worklogs",
            "SELECT * FROM Expenses", 10);

        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("Expenses"));
        Assert.That(result, Does.Contain("ResourceHours"));
    }

    [Test]
    public async Task Scoped_ResourcesRead_ReturnsOneEntity()
    {
        var result = await CallResourceReadAsync("/api/mcp/worklogs", "entities://list");

        Assert.That(result, Does.Contain("worklogs"));
        Assert.That(result, Does.Not.Contain("expenses"));
    }

    [Test]
    public async Task Scoped_PromptsGet_ReturnsEntityGuide()
    {
        var result = await CallPromptGetAsync("/api/mcp/worklogs", "entity-guide");

        Assert.That(result, Does.Contain("Worklogs"));
        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Contain("ResourceName"));
        Assert.That(result, Does.Contain("Query Guidance"));
    }

    [Test]
    public async Task UnknownEntity_Returns404()
    {
        var (response, _) = await PostJsonAsync("/api/mcp/nonexistent", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "describe" }
        });

        Assert.That((int)response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task ScopedThenGlobal_NoContextLeakage()
    {
        var scoped = await CallDescribeAsync("/api/mcp/worklogs");
        Assert.That(scoped, Does.Contain("# Worklogs"));
        Assert.That(scoped, Does.Not.Contain("# Expenses"));

        var global = await CallDescribeAsync("/api/mcp");
        Assert.That(global, Does.Contain("# Worklogs"));
        Assert.That(global, Does.Contain("# Expenses"));
    }

    [Test]
    public async Task Mcp_WithoutAuth_Returns401()
    {
        using var noAuthClient = _factory.CreateClient();

        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "describe" }
        }, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = content,
            Headers = { { "Accept", "application/json, text/event-stream" } }
        };

        var response = await noAuthClient.SendAsync(request);
        Assert.That((int)response.StatusCode, Is.EqualTo(401));
    }
}
