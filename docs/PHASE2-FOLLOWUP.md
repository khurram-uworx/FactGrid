# Phase 2 Follow-up — Dual-MCP Routing: Global + Scoped Endpoints

## Goal

Extend the Phase 2 MCP architecture with two tiers of access — a **global endpoint** (`/api/mcp`) that can describe and query all entities, and **scoped endpoints** (`/api/mcp/{entityName}`) that are restricted to a single entity. Add MCP **resources** and **prompts** support so agents can discover entity schemas and receive guided instructions for each scope.

## High-Level Architecture

```
                    ┌──────────────────────────────────────────┐
                    │            EntityRegistry                │
                    │  EntityName → { ModelType, TableName,    │
                    │    ExcelParser, [Description] schema }   │
                    └─────────────┬────────────────────────────┘
                                  │
            ┌─────────────────────┼─────────────────────┐
            │                     │                     │
            ▼                     ▼                     ▼
┌──────────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│   /api/mcp (global)  │  │ /api/mcp/entity1 │  │ /api/mcp/entity2 │
│                      │  │                  │  │                  │
│ Resources:           │  │ Resources:       │  │ Resources:       │
│  entities://list     │  │  entity://schema │  │  entity://schema │
│  entities://{name}/  │  │                  │  │                  │
│    schema            │  │ Prompts:         │  │ Prompts:         │
│                      │  │  entity-guide    │  │  entity-guide    │
│ Prompts:             │  │                  │  │                  │
│  entities-guide      │  │ Tools:           │  │ Tools:           │
│                      │  │  sql_query       │  │  sql_query       │
│ Tools:               │  │   (entity only)  │  │   (entity only)  │
│  sql_query (any)     │  │  describe        │  │  describe        │
│  describe (all)      │  │   (entity only)  │  │   (entity only)  │
└──────────────────────┘  └──────────────────┘  └──────────────────┘
            │                     │                     │
            └─────────────────────┼─────────────────────┘
                                  │
                                  ▼
                    ┌──────────────────────────────┐
                    │     ApplicationDbContext     │
                    │  (SQL Server / SQLite / PG)  │
                    └──────────────────────────────┘
```

## Key Changes from Phase 2

### 1. Dual MCP Routes

Two routes mapped to the same MCP server, differentiated by session context:

```csharp
app.MapMcp("/api/mcp").AllowAnonymous();               // global — no entity scope
app.MapMcp("/api/mcp/{entityName}").AllowAnonymous();   // scoped — entity set in context
```

The `ConfigureSessionOptions` handler only sets `IEntityContextAccessor.CurrentEntity` when `entityName` is present in route values. When accessing `/api/mcp` (global), `CurrentEntity` stays null, signalling "global mode" to tools, resources, and prompts.

### 2. Entity-Scoped Resources

Resources (discoverable data sources that agents can read) expose entity schemas via MCP's resource protocol.

#### Global Resources (`/api/mcp`)

| URI Template | Name | Content |
|---|---|---|
| `entities://list` | Entity List | JSON array of all registered entities — name, displayName, table, description |
| `entities://{entityName}/schema` | Entity Schema | JSON describing the model properties for that entity (type, description, constraints) |

Resource methods check `IEntityContextAccessor.CurrentEntity`:
- If null (global): return the full list or schema for the requested entity name
- If set (scoped): return only the current entity's info (ignore the `entityName` in the URI)

#### Scoped Resources (`/api/mcp/{entityName}`)

| URI Template | Name | Content |
|---|---|---|
| `entity://schema` | Current Entity Schema | JSON describing the scoped entity's model properties |

When scoped, the agent can only discover the entity it's scoped to — no cross-entity visibility.

**Reference:** CodeMemory resource pattern — `[McpServerResourceType]` class with `[McpServerResource(UriTemplate = "...", Name = "...", MimeType = "application/json")]` methods returning `Task<string>`.

### 3. Entity-Aware Prompts

Prompts (pre-written instructions that guide the agent's behavior) help agents understand the available entities and how to interact with them.

#### Global Prompt (`/api/mcp`)

```csharp
[McpServerPrompt(Name = "entities-guide", Title = "Entities Overview")]
// Returns a prompt that lists all registered entities with their
// display names, table names, and descriptions. Guides the agent
// to use sql_query targeting the appropriate table, or to call
// describe to see entity schemas.
```

#### Scoped Prompt (`/api/mcp/{entityName}`)

```csharp
[McpServerPrompt(Name = "entity-guide", Title = "Current Entity Guide")]
// Returns a prompt specific to the scoped entity:
// - Entity display name and description
// - Physical table name
// - Schema summary (from [Description] attributes)
// - Example SQL queries scoped to this entity's table
// - Guidance that queries can only reference this entity's table
```

**Reference:** CodeMemory prompt pattern — `[McpServerPromptType]` static class with `[McpServerPrompt(Name = "...", Title = "...")]` methods returning `GetPromptResult`.

### 4. SQL Query Scope Validation

In scoped mode, `sql_query` must reject queries that reference tables outside the scoped entity. This uses the existing `SqlParserCS` library to extract table references from the parsed AST and validate them against the entity's registered table name.

```csharp
if (entity is not null)  // scoped mode — validate table references
{
    var tables = ExtractTableReferences(statement);
    if (tables.Count == 0 || tables.Any(t =>
        !t.Equals(entity.TableName, StringComparison.OrdinalIgnoreCase)))
    {
        return $"Error: Queries in this scope may only reference " +
               $"table '{entity.TableName}'.";
    }
}
```

In global mode (`entity is null`), all registered tables are allowed.

#### Table Reference Extraction

Traverse the parsed `Statement.Select` AST to find all `TableFactor.Table` references. SqlParserCS provides a rich AST — the `Select` statement contains a `From` clause with `TableWithJoins`, each having a `TableFactor` that can be pattern-matched:

```csharp
static List<string> ExtractTableReferences(Statement.Select select)
{
    var tables = new List<string>();
    foreach (var tableWithJoin in select.From)
    {
        if (tableWithJoin.Relation.TableFactor is TableFactor.Table table)
            tables.Add(table.Name.Values.Last().Value);
        // Also scan JOIN clauses for table references
    }
    return tables;
}
```

### 5. Describe Behavior

The `describe` tool adapts based on scope:

| Scope | Behavior |
|---|---|
| Global (`/api/mcp`) | Returns schema for ALL registered entities, each with its table name, description, and column list |
| Scoped (`/api/mcp/{entityName}`) | Returns schema for only that one entity (same as Phase 2) |

## Implementation Details

### Session Handler (Program.cs)

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(o =>
    {
        o.Stateless = true;
        o.PerSessionExecutionContext = true;
        o.ConfigureSessionOptions = (context, options, ct) =>
        {
            var entityName = context.Request.RouteValues["entityName"] as string;
            if (!string.IsNullOrEmpty(entityName))
            {
                var registry = context.RequestServices.GetRequiredService<EntityRegistry>();
                var accessor = context.RequestServices.GetRequiredService<IEntityContextAccessor>();
                accessor.CurrentEntity = registry.Get(entityName);
            }
            // else: global mode — CurrentEntity stays null
        };
    })
    .WithToolsFromAssembly(typeof(GenericSqlQueryTool).Assembly)
    .WithResourcesFromAssembly(typeof(EntityResources).Assembly)
    .WithPromptsFromAssembly(typeof(EntityPrompts).Assembly);

app.MapMcp("/api/mcp").AllowAnonymous();
app.MapMcp("/api/mcp/{entityName}").AllowAnonymous();
```

### Resources Class (sketch)

```csharp
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
    [Description("Lists all registered entities with their display names, table names, and descriptions.")]
    public Task<string> GetEntityListAsync(CancellationToken ct = default)
    {
        var entities = _context.CurrentEntity is { } scoped
            ? [_context.CurrentEntity]
            : _registry.GetAll().ToList();

        var result = entities.Select(e => new
        {
            name = e.EntityName,
            displayName = e.DisplayName,
            table = e.TableName,
            description = e.Description
        });

        return Task.FromResult(JsonSerializer.Serialize(result));
    }

    [McpServerResource(UriTemplate = "entities://{entityName}/schema", Name = "Entity Schema", MimeType = "application/json")]
    [Description("Returns the column schema for a specific entity.")]
    public Task<string> GetEntitySchemaAsync(CancellationToken ct = default)
    {
        var entity = _context.CurrentEntity ?? _registry.Get(entityName);
        if (entity is null)
            return Task.FromResult("{\"error\": \"Entity not found\"}");

        var props = entity.ModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "Id")
            .Select(p => new
            {
                name = p.Name,
                type = p.PropertyType.Name,
                description = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                maxLength = p.GetCustomAttribute<MaxLengthAttribute>()?.Length,
                isNullable = Nullable.GetUnderlyingType(p.PropertyType) is not null
                              || (p.PropertyType == typeof(string) && !p.PropertyType.IsValueType)
            });

        var result = new
        {
            entityName = entity.EntityName,
            displayName = entity.DisplayName,
            table = entity.TableName,
            description = entity.Description,
            columns = props
        };

        return Task.FromResult(JsonSerializer.Serialize(result));
    }
}
```

### Prompts Class (sketch)

```csharp
[McpServerPromptType]
public static class EntityPrompts
{
    [McpServerPrompt(Name = "entities-guide", Title = "Entities Overview")]
    [Description("Overview of all available entities and how to query them.")]
    public static GetPromptResult GetEntitiesGuide()
    {
        var text = BuildGuideText();
        return new GetPromptResult
        {
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = text }
                }
            ],
            Description = "Entity query guide"
        };
    }
}
```

### Tool Changes (GenericSqlQueryTool)

- `SqlQueryAsync`: in scoped mode, extract table references from the SQL AST and validate against the entity's table name
- `DescribeAsync`: if null entity context, iterate all entities from the registry and return each schema; if set, return just that entity (current behavior)

## Files Likely Involved

| File | Change |
|---|---|
| `src/EfMcp.AspNet/Tools/GenericSqlQueryTool.cs` | Add scoped SQL validation, global describe mode |
| `src/EfMcp.AspNet/Tools/EntityResources.cs` | **New** — MCP resource methods |
| `src/EfMcp.AspNet/Tools/EntityPrompts.cs` | **New** — MCP prompt methods |
| `src/EfMcp.AspNet/Program.cs` | Add `/api/mcp` route, register resources + prompts assemblies |
| `tests/EfMcp.Tests/GenericSqlQueryToolTests.cs` | Add tests for scoped validation, global describe |
| `docs/GETTING-STARTED.md` | **New** — Project onboarding guide (after implementation) |

## Estimated Scope

| Work Item | Effort |
|---|---|
| Dual route mapping (`/api/mcp` + `/{entityName}`) | Small |
| SQL table reference extraction + scoped validation | Medium |
| EntityResources class (list + schema resources) | Medium |
| EntityPrompts class (global + scoped prompts) | Small |
| Global describe mode (null entity context) | Small |
| Integration tests for scoped SQL validation | Medium |
| `GETTING-STARTED.md` — project onboarding guide | Small |

Total: **Medium** (smaller than Phase 2 — leverages existing Phase 2 infrastructure).
