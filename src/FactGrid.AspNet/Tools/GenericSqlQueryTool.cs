using FactGrid.AspNet.Data;
using FactGrid.AspNet.Services;
using FactGrid.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace FactGrid.AspNet.Tools;

[McpServerToolType]
public class GenericSqlQueryTool
{
    readonly QueryValidationService _validator;
    readonly IServiceProvider _serviceProvider;
    readonly IEntityContextAccessor _entityContext;
    readonly EntityRegistry _registry;

    public GenericSqlQueryTool(QueryValidationService validator, IServiceProvider serviceProvider, IEntityContextAccessor entityContext, EntityRegistry registry)
    {
        _validator = validator;
        _serviceProvider = serviceProvider;
        _entityContext = entityContext;
        _registry = registry;
    }

    [McpServerTool, Description(@"
Execute SELECT-only SQL queries against the current entity's table.

Returns results as a markdown table. Only single-statement SELECT queries are allowed.
Use the DescribeAsync tool to see the entity schema.")]
    public async Task<string> SqlQueryAsync(
        [Description("SQL query string (e.g. SELECT * FROM ResourceHours WHERE ResourceName LIKE '%John%'")] string query,
        [Description("Maximum number of rows to return (default 100, max 10000)")] int maxResults = 100)
    {
        var entity = _entityContext.CurrentEntity;

        var (isValid, error) = _validator.Validate(query);
        if (!isValid) return $"Error: {error}";

        if (entity is not null)
        {
            var (isScoped, scopeError) = _validator.ValidateScoped(query, entity.TableName);
            if (!isScoped) return $"Error: {scopeError}";
        }
        else
        {
            var allowedTables = _registry.GetAll().Select(e => e.TableName);
            var (isAllowed, allowError) = _validator.ValidateTables(query, allowedTables);
            if (!isAllowed) return $"Error: {allowError}";
        }

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

    [McpServerTool, Description("Describe entity schema(s). In global mode, shows all entities. In scoped mode, shows only the current entity's schema.")]
    public Task<string> DescribeAsync()
    {
        var entity = _entityContext.CurrentEntity;

        var entities = entity is not null
            ? (IEnumerable<EntityRegistration>)[entity]
            : _registry.GetAll();

        var sb = new StringBuilder();
        foreach (var ent in entities)
        {
            sb.AppendLine($"# {ent.DisplayName} ({ent.TableName})");
            sb.AppendLine();
            sb.AppendLine(ent.Description);
            sb.AppendLine();
            sb.AppendLine("| Column | Type | Description |");
            sb.AppendLine("|--------|------|-------------|");

            foreach (var col in EntitySchemaHelper.GetColumns(ent.ModelType))
                sb.AppendLine($"| {col.Name} | {col.Type} | {col.Description} |");

            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }
}
