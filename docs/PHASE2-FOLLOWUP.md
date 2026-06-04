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
│  entities://list     │  │  entities://list │  │  entities://list │
│  entities://schema   │  │  entities://schema│  │  entities://schema│
│                      │  │                  │  │                  │
│ Prompts:             │  │ Prompts:         │  │ Prompts:         │
│  entities-guide      │  │  entities-guide  │  │  entities-guide  │
│                      │  │  entity-guide    │  │  entity-guide    │
│ Tools:               │  │                  │  │                  │
│  sql_query (any      │  │ Tools:           │  │ Tools:           │
│   registered table)  │  │  sql_query       │  │  sql_query       │
│  describe (all)      │  │   (entity only)  │  │   (entity only)  │
│                      │  │  describe        │  │  describe        │
│                      │  │   (entity only)  │  │   (entity only)  │
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

### 2. Context-Aware Resources

Resources (discoverable data sources that agents can read) expose entity schemas via MCP's resource protocol. Both resource URIs are fixed (non-template) and context-aware:

| URI | Name | Global Mode (`/api/mcp`) | Scoped Mode (`/api/mcp/{entityName}`) |
|---|---|---|---|
| `entities://list` | Entity List | JSON array of **all** registered entities | JSON array of **only** the scoped entity |
| `entities://schema` | Entity Schema | JSON array of **all** entity column schemas | JSON array of **only** the scoped entity's schema |

Resource methods check `IEntityContextAccessor.CurrentEntity`:
- If null (global): return data for all registered entities
- If set (scoped): return only the current entity's data

No template URIs or per-entity resource routes are used — the same two fixed URIs adapt based on session context.

**Reference:** CodeMemory resource pattern — `[McpServerResourceType]` class with `[McpServerResource(UriTemplate = "...", Name = "...", MimeType = "application/json")]` methods returning `Task<string>`.

### 3. Entity-Aware Prompts

Prompts (pre-written instructions that guide the agent's behavior) help agents understand the available entities and how to interact with them.

#### Global Prompt (`/api/mcp`) — `entities-guide`

Returns an overview of all registered entities with their display names, table names, and descriptions. Guides the agent to use `sql_query` targeting the appropriate table, or to call `describe` to see entity schemas. When accessed from scoped mode, returns only the scoped entity's info — the prompt adapts to context.

#### Scoped Prompt (`/api/mcp/{entityName}`) — `entity-guide`

Returns a prompt specific to the scoped entity:
- Entity display name and description
- Physical table name
- Schema summary (from `[Description]` attributes)
- Example SQL queries scoped to this entity's table
- Guidance that queries can only reference this entity's table

When accessed from global mode (`/api/mcp`), returns an error message indicating no entity scope is set.

**Reference:** CodeMemory prompt pattern — `[McpServerPromptType]` class with constructor DI (non-static) and `[McpServerPrompt(Name = "...", Title = "...")]` methods returning `GetPromptResult`.

### 4. SQL Query Scope Validation

In scoped mode, `sql_query` rejects queries that reference tables outside the scoped entity. In global mode, it rejects queries that reference any table not registered in the `EntityRegistry`. Both use `SqlParserCS` to parse the query, then walk the AST to collect table references.

The `QueryValidationService` provides three validation methods:

| Method | Purpose |
|---|---|
| `Validate(query)` | SELECT-only gate — rejects non-SELECT, multi-statement, and unparseable queries |
| `ValidateScoped(query, allowedTableName)` | Delegates to `ValidateTables` with a single allowed table |
| `ValidateTables(query, allowedTableNames)` | SELECT-only gate + table reference allow-list validation |

#### Validation Flow (GenericSqlQueryTool)

```csharp
var (isValid, error) = _validator.Validate(query);
if (!isValid) return $"Error: {error}";

if (entity is not null)
{
    // Scoped mode — only the entity's table is allowed
    var (isScoped, scopeError) = _validator.ValidateScoped(query, entity.TableName);
    if (!isScoped) return $"Error: {scopeError}";
}
else
{
    // Global mode — only registered entity tables are allowed
    var allowedTables = _registry.GetAll().Select(e => e.TableName);
    var (isAllowed, allowError) = _validator.ValidateTables(query, allowedTables);
    if (!isAllowed) return $"Error: {allowError}";
}
```

#### Table Reference Extraction

The `CollectTableReferences` method walks the parsed AST to find all table references. It traverses SELECT, set operations (UNION/INTERSECT/EXCEPT), JOINs, CTEs, subqueries (EXISTS, IN subquery, scalar subqueries), derived tables, and nested joins. CTE names are collected and excluded from the disallowed set since they are query-local aliases.

The key pattern for extracting a table name from a `TableFactor`:

```csharp
case TableFactor.Table tbl:
    var name = tbl.Name.Values.Last().Value;
    tables.Add(name);
    break;
```

Error messages follow the format:
```
Query references non-registered tables: {list}. Allowed tables: {list}
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

### Resources Class (actual implementation)

Both resource URIs are fixed (`entities://list` and `entities://schema`) — no template parameters. Context awareness is achieved via `IEntityContextAccessor.CurrentEntity`:

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

    [McpServerResource(UriTemplate = "entities://list", Name = "Entity List",
        MimeType = "application/json")]
    [Description("Lists all registered entities. Scoped mode returns only the scoped entity.")]
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

    [McpServerResource(UriTemplate = "entities://schema", Name = "Entity Schema",
        MimeType = "application/json")]
    [Description("Returns column schemas. Global mode returns all entities; scoped returns one.")]
    public Task<string> GetEntitySchemaAsync(CancellationToken ct = default)
    {
        var entities = _context.CurrentEntity is { } scoped
            ? (IEnumerable<EntityRegistration>)[scoped]
            : _registry.GetAll();

        var schemas = entities.Select(entity =>
        {
            var props = entity.ModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != "Id")
                .Select(p => new
                {
                    name = p.Name,
                    type = p.PropertyType switch
                    {
                        Type t when t == typeof(string) => "NVARCHAR",
                        Type t when t == typeof(int) => "INT",
                        Type t when t == typeof(decimal) => "DECIMAL(10,2)",
                        Type t when t == typeof(DateOnly) => "DATE",
                        Type t when t == typeof(DateTime) => "DATE",
                        Type t when t == typeof(bool) => "BIT",
                        _ => p.PropertyType.Name
                    },
                    description = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                    maxLength = p.GetCustomAttribute<MaxLengthAttribute>()?.Length,
                    isNullable = Nullable.GetUnderlyingType(p.PropertyType) is not null
                                  || (p.PropertyType == typeof(string) && !p.PropertyType.IsValueType)
                });

            return new
            {
                entityName = entity.EntityName,
                displayName = entity.DisplayName,
                table = entity.TableName,
                description = entity.Description,
                columns = props
            };
        });

        return Task.FromResult(JsonSerializer.Serialize(schemas));
    }
}
```

### Prompts Class (actual implementation)

The prompt class is non-static with constructor DI to access the entity registry and context:

```csharp
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
    [Description("Overview of all available entities and how to query them.")]
    public GetPromptResult GetEntitiesGuide()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You have access to the FactGrid entities database " +
                      "via the `sql_query` and `describe` tools.");

        var entities = _context.CurrentEntity is { } scoped
            ? (IEnumerable<EntityRegistration>)[scoped]
            : _registry.GetAll();

        foreach (var e in entities)
            sb.AppendLine($"- **{e.EntityName}** (table: `{e.TableName}`) — {e.Description}");

        sb.AppendLine();
        sb.AppendLine("Use `describe` to see the column schema for an entity.");
        sb.AppendLine("Use `sql_query` to run SELECT queries against entity tables.");

        if (_context.CurrentEntity is null)
        {
            sb.AppendLine("In this global scope, you may reference any registered table.");
            sb.AppendLine("Example: `SELECT * FROM ResourceHours WHERE ResourceName LIKE '%John%'`");
        }
        else
        {
            sb.AppendLine("In this scope, queries may only reference the current entity's table.");
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
    [Description("Detailed guide for the currently scoped entity.")]
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
                        Content = new TextContentBlock
                        {
                            Text = "No entity scope is set. Access this prompt from " +
                                   "a scoped endpoint like /api/mcp/{entityName}."
                        }
                    }
                ],
                Description = "Entity guide unavailable"
            };
        }

        var props = entity.ModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var sb = new StringBuilder();
        sb.AppendLine($"## {entity.DisplayName} ({entity.TableName})");
        sb.AppendLine();
        sb.AppendLine(entity.Description);
        sb.AppendLine();
        sb.AppendLine("### Columns");
        sb.AppendLine();
        sb.AppendLine("| Column | Type | Description |");
        sb.AppendLine("|--------|------|-------------|");

        foreach (var prop in props)
        {
            if (prop.Name == "Id") continue;
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

        sb.AppendLine();
        sb.AppendLine("### Query Guidance");
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
```

### Tool Changes (GenericSqlQueryTool)

- `SqlQueryAsync`: validates query via `Validate` (SELECT-only gate), then branches on context — scoped mode calls `ValidateScoped(query, entity.TableName)`, global mode calls `ValidateTables(query, registry.GetAll().Select(e => e.TableName))`
- `DescribeAsync`: if null entity context (global mode), iterates all entities from the registry and returns each schema; if scoped, returns just that entity's schema (same as Phase 2)

## Files Involved (Implementation)

| File | Change |
|---|---|
| `src/FactGrid.AspNet/Tools/GenericSqlQueryTool.cs` | Add scoped SQL validation via `ValidateScoped`, global mode via `ValidateTables`, context-aware `DescribeAsync` |
| `src/FactGrid.AspNet/Tools/EntityResources.cs` | **New** — MCP resource methods for `entities://list` and `entities://schema` |
| `src/FactGrid.AspNet/Tools/EntityPrompts.cs` | **New** — MCP prompt methods for `entities-guide` and `entity-guide` |
| `src/FactGrid.AspNet/Services/QueryValidationService.cs` | Add `ValidateTables(string, IEnumerable<string>)` method; refactor `ValidateScoped` to delegate |
| `src/FactGrid.AspNet/Program.cs` | Add `/api/mcp` route, register `WithResourcesFromAssembly` + `WithPromptsFromAssembly` |
| `tests/FactGrid.Tests/McpEndpointTests.cs` | **New** — 13 HTTP integration tests for global/scoped tools, resources, prompts |
| `tests/FactGrid.Tests/WorklogsMcpToolsTests.cs` | Update constructors, add global mode tests |
| `tests/FactGrid.Tests/QueryValidationTests.cs` | Add `ValidateTables` unit tests |

## Security Considerations

### Proof-of-Concept Phase

Both MCP routes are currently anonymous (`AllowAnonymous()`). There is no authentication or authorization on MCP endpoints. In a production deployment:

- Add authentication (e.g., API key, JWT, or Azure AD) to MCP routes
- Apply authorization policies to distinguish global vs. scoped access at the auth level
- Consider removing the global route entirely for multi-tenant scenarios

### Global Mode SQL Restriction

The global mode (`/api/mcp`) restricts `sql_query` to tables registered in the `EntityRegistry`. System tables (e.g., `AspNetUsers`, `__EFMigrationsHistory`) and any table not explicitly registered are rejected. This is enforced by `ValidateTables` using the registry's allow-list.

### SQL Injection Mitigation

- SqlParserCS gates non-SELECT statements at the parser level
- Table reference validation prevents cross-entity data access
- Queries are **not** parameterized — in production, connect to the database with a read-only user
- `maxResults` is clamped to 1–10,000 rows

### Context Leakage Prevention

A middleware in `Program.cs` clears `IEntityContextAccessor.CurrentEntity` at the start of every request to prevent cross-request context leakage. The global and scoped routes share the same MCP server but are differentiated by session configuration.

## Estimated Scope

| Work Item | Effort |
|---|---|---|
| Global `/api/mcp` route + global sql_query mode | Small |
| EntityResources class (list + schema resources) | Medium |
| EntityPrompts class (global + scoped prompts) | Small |
| Global describe mode (null entity context) | Small |
| Integration tests for global mode + resources + prompts | Medium |
| `GETTING-STARTED.md` — project onboarding guide | Small |

Total: **Medium** (leverages existing scoped routing, scoped SQL validation, and entity registry infrastructure).
