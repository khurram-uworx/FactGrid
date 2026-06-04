using FactGrid.AspNet.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FactGrid.AspNet.Tools;

[McpServerResourceType]
public sealed class EntityResources
{
    readonly EntityRegistry _registry;
    readonly IEntityContextAccessor _context;

    public EntityResources(EntityRegistry registry, IEntityContextAccessor context)
    {
        _registry = registry;
        _context = context;
    }

    [McpServerResource(UriTemplate = "entities://list", Name = "Entity List", MimeType = "application/json")]
    [Description("Lists all registered entities with their display names, table names, and descriptions. When accessed from a scoped endpoint, returns only the scoped entity.")]
    public Task<string> GetEntityListAsync(CancellationToken ct = default)
    {
        var entities = _context.CurrentEntity is { } scoped
            ? (IEnumerable<EntityRegistration>)[scoped]
            : _registry.GetAll();

        var result = entities.Select(e => new
        {
            name = e.EntityName,
            displayName = e.DisplayName,
            table = e.TableName,
            description = e.Description
        });

        return Task.FromResult(JsonSerializer.Serialize(result));
    }

    [McpServerResource(UriTemplate = "entities://schema", Name = "Entity Schema", MimeType = "application/json")]
    [Description("Returns the column schema for all entities (global mode) or the current scoped entity. Includes column names, types, descriptions, and constraints.")]
    public Task<string> GetEntitySchemaAsync(CancellationToken ct = default)
    {
        var entities = _context.CurrentEntity is { } scoped
            ? (IEnumerable<EntityRegistration>)[scoped]
            : _registry.GetAll();

        var schemas = entities.Select(entity => new
        {
            entityName = entity.EntityName,
            displayName = entity.DisplayName,
            table = entity.TableName,
            description = entity.Description,
            columns = EntitySchemaHelper.GetColumns(entity.ModelType).Select(col => new
            {
                name = col.Name,
                type = col.Type,
                description = col.Description,
                maxLength = col.MaxLength,
                isNullable = col.IsNullable
            })
        });

        return Task.FromResult(JsonSerializer.Serialize(schemas));
    }
}
