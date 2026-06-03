using System.ComponentModel;
using System.Reflection;
using System.Text;
using EfMcp.AspNet.Data;
using EfMcp.AspNet.Models;
using EfMcp.AspNet.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace EfMcp.AspNet.Tools;

[McpServerToolType]
public class WorklogsMcpTools
{
    private readonly QueryValidationService _validator;
    private readonly IServiceProvider _serviceProvider;

    public WorklogsMcpTools(QueryValidationService validator, IServiceProvider serviceProvider)
    {
        _validator = validator;
        _serviceProvider = serviceProvider;
    }

    [McpServerTool, Description(@"
Execute SELECT-only SQL queries against the Worklogs (ResourceHours) entity.

Returns results as a markdown table. Only single-statement SELECT queries are allowed.
Use the DescribeAsync tool to see the entity schema.")]
    public async Task<string> SqlQueryAsync(
        [Description("SQL query string (e.g. SELECT * FROM ResourceHours WHERE ResourceName LIKE '%John%'")] string query,
        [Description("Maximum number of rows to return (default 100, max 10000)")] int maxResults = 100)
    {
        var (isValid, error) = _validator.Validate(query);
        if (!isValid) return $"Error: {error}";

        maxResults = Math.Clamp(maxResults, 1, 10000);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var conn = db.Database.GetDbConnection();
        await using var command = conn.CreateCommand();
        command.CommandText = query;

        await conn.OpenAsync();

        await using var reader = await command.ExecuteReaderAsync();

        var sb = new StringBuilder();
        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        sb.Append('|');
        sb.AppendJoin(" | ", columns);
        sb.AppendLine("|");
        sb.Append('|');
        foreach (var _ in columns) sb.Append("---|");
        sb.AppendLine();

        var rowCount = 0;
        while (await reader.ReadAsync() && rowCount < maxResults)
        {
            sb.Append('|');
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString();
                sb.Append(' ').Append(val?.Replace("|", "\\|") ?? "").Append(" |");
            }
            sb.AppendLine();
            rowCount++;
        }

        if (rowCount == 0) sb.AppendLine("*No results.*");

        return sb.ToString();
    }

    [McpServerTool, Description("Describe the Worklogs entity schema, including column names, types, and descriptions.")]
    public Task<string> DescribeAsync()
    {
        var props = typeof(Worklogs).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var sb = new StringBuilder();
        sb.AppendLine("| Column | Type | Description |");
        sb.AppendLine("|--------|------|-------------|");

        foreach (var prop in props)
        {
            var desc = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            var colType = prop.PropertyType switch
            {
                Type t when t == typeof(string) => "NVARCHAR",
                Type t when t == typeof(int) => "INT",
                Type t when t == typeof(decimal) => "DECIMAL(10,2)",
                Type t when t == typeof(DateOnly) => "DATE",
                Type t when t == typeof(DateTime) => "DATE",
                Type t when t == typeof(bool) => "BIT",
                _ => prop.PropertyType.Name
            };
            sb.AppendLine($"| {prop.Name} | {colType} | {desc} |");
        }

        return Task.FromResult(sb.ToString());
    }
}
