using FactGrid.AspNet.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace FactGrid.AspNet.Tools;

[McpServerPromptType]
public sealed class EntityPrompts
{
    readonly EntityRegistry _registry;
    readonly IEntityContextAccessor _context;

    public EntityPrompts(EntityRegistry registry, IEntityContextAccessor context)
    {
        _registry = registry;
        _context = context;
    }

    [McpServerPrompt(Name = "entities-guide", Title = "Entities Overview")]
    [Description("Overview of all available entities and how to query them using sql_query and describe.")]
    public GetPromptResult GetEntitiesGuide()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You have access to the FactGrid entities database via the `sql_query` and `describe` tools.");
        sb.AppendLine();
        sb.AppendLine("Available entities:");

        var entities = _context.CurrentEntity is { } scoped
            ? (IEnumerable<EntityRegistration>)[scoped]
            : _registry.GetAll();

        foreach (var e in entities)
        {
            sb.AppendLine($"- **{e.EntityName}** (table: `{e.TableName}`) — {e.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("Use `describe` to see the column schema for an entity.");
        sb.AppendLine("Use `sql_query` to run SELECT queries against entity tables.");

        if (_context.CurrentEntity is null)
        {
            sb.AppendLine("In this global scope, you may reference any registered table in your queries.");
            sb.AppendLine();
            sb.AppendLine("Example queries:");
            sb.AppendLine("- `SELECT * FROM ResourceHours WHERE ResourceName LIKE '%John%'`");
            sb.AppendLine("- `SELECT ResourceName, SUM(Hours) FROM ResourceHours GROUP BY ResourceName`");
            sb.AppendLine("- `SELECT * FROM Expenses WHERE Amount > 100`");
        }
        else
        {
            sb.AppendLine("In this scoped scope, queries may only reference the current entity's table.");
        }

        return new GetPromptResult
        {
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = sb.ToString() }
                }
            ],
            Description = "Entity query guide"
        };
    }

    [McpServerPrompt(Name = "entity-guide", Title = "Current Entity Guide")]
    [Description("Detailed guide for the currently scoped entity, including its schema and example queries.")]
    public GetPromptResult GetEntityGuide()
    {
        var entity = _context.CurrentEntity;
        if (entity is null)
        {
            return new GetPromptResult
            {
                Messages =
                [
                    new PromptMessage
                    {
                        Role = Role.User,
                        Content = new TextContentBlock { Text = "No entity scope is set. Access this prompt from a scoped endpoint like /api/mcp/{entityName}." }
                    }
                ],
                Description = "Entity guide unavailable"
            };
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## {entity.DisplayName} ({entity.TableName})");
        sb.AppendLine();
        sb.AppendLine(entity.Description);
        sb.AppendLine();
        sb.AppendLine("### Columns");
        sb.AppendLine();
        sb.AppendLine("| Column | Type | Description |");
        sb.AppendLine("|--------|------|-------------|");

        foreach (var col in EntitySchemaHelper.GetColumns(entity.ModelType))
            sb.AppendLine($"| {col.Name} | {col.Type} | {col.Description} |");

        sb.AppendLine();
        sb.AppendLine("### Query Guidance");
        sb.AppendLine();
        sb.AppendLine($"Queries may only reference table `{entity.TableName}`.");
        sb.AppendLine();
        sb.AppendLine("Example queries:");
        sb.AppendLine($"- `SELECT * FROM {entity.TableName}`");
        sb.AppendLine($"- `SELECT COUNT(*) FROM {entity.TableName}`");
        sb.AppendLine($"- `SELECT * FROM {entity.TableName} WHERE ...`");

        return new GetPromptResult
        {
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = sb.ToString() }
                }
            ],
            Description = $"{entity.EntityName} query guide"
        };
    }
}
