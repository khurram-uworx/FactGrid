# Phase 2 — Multi-Entity Support

## Goal

Extend the Phase 1 pattern to support N entities and Excel formats. Abstract the upload pipeline, entity registry, and MCP dispatcher so adding a new entity requires only defining a model and an Excel parser — no controller/tool boilerplate.

## High-Level Architecture

```
                  ┌─────────────────────────────┐
                  │      EntityRegistry          │
                  │  EntityName → {              │
                  │    ModelType,                │
                  │    ExcelParser,              │
                  │    DbContext config,         │
                  │    [Description] schema      │
                  │  }                           │
                  └──────────┬──────────────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
     ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
     │  Worklogs    │ │  EntityB     │ │  EntityN     │
     │  (existing)  │ │  (new)       │ │  (future)    │
     └──────────────┘ └──────────────┘ └──────────────┘
```

## Key Changes from Phase 1

### 1. Entity Registry

A registry that maps entity names (URL-safe, lowercase) to their metadata:

```csharp
public record EntityRegistration(
    string DisplayName,
    Type ModelType,
    Type ExcelParserType,      // implements IExcelParser<T>
    string TableName,
    string Description
);

public class EntityRegistry
{
    private readonly Dictionary<string, EntityRegistration> _entities = new();

    public void Register<T>(EntityRegistration registration) where T : class;
    public EntityRegistration? Get(string entityName);
    public IEnumerable<EntityRegistration> GetAll();
}
```

Registration in `Program.cs`:

```csharp
var registry = new EntityRegistry();
registry.Register<Worklogs>(new("Worklogs", typeof(Worklogs), typeof(WorklogsExcelParser), "ResourceHours", "Employee worklog entries"));
registry.Register<Expenses>(new("Expenses", typeof(Expenses), typeof(ExpensesExcelParser), "ExpenseReports", "Expense report items"));
builder.Services.AddSingleton(registry);
```

### 2. Abstracted Excel Parser Interface

```csharp
public interface IExcelParser<T> where T : class
{
    (List<T> Records, List<string> Errors) Parse(Stream excelStream);
}
```

Each entity gets its own parser implementation. The `WorklogsExcelParser` is the same logic from Phase 1's `ExcelUploadService`, refactored into this interface.

### 3. Generic MCP Route

Parameterized route: `/api/mcp/{entityName}`

Use the `ConfigureSessionOptions` callback to resolve the entity (same pattern as CodeMemory's `{repoName}` routing):

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly(typeof(GenericSqlQueryTool).Assembly)
    .WithSessionOptionsHandler((context, options, ct) =>
    {
        var entityName = context.Request.RouteValues["entityName"] as string;
        if (!string.IsNullOrEmpty(entityName))
        {
            var registry = context.RequestServices.GetRequiredService<EntityRegistry>();
            var entity = registry.Get(entityName);
            if (entity is null) throw new NotFoundException($"Entity '{entityName}' not found");

            var entityContext = context.RequestServices.GetRequiredService<IEntityContextAccessor>();
            entityContext.CurrentEntity = entity;
        }
        return Task.CompletedTask;
    });

app.MapMcp("/api/mcp/{entityName}");
```

Then `GenericSqlQueryTool` resolves the current entity from `IEntityContextAccessor` and uses it for:
- Schema description (reflection with `[Description]`)
- SQL query execution (resolves `DbSet` dynamically)

### 4. Generic Web UI

The `/Worklogs` page becomes `/Entity/{entityName}`:

- A landing page `/Entity` lists all registered entities (from registry)
- Each entity page shows row count, upload form, delete-all — same as Phase 1 but driven by the entity registry
- Upload calls the correct `IExcelParser<T>` from the registry
- Delete-all uses the `DbSet` resolved from the model type

**Consideration:** Dynamic `DbSet` access requires either:
- Reflection on `DbContext.Set<T>()`
- Or a method on `ApplicationDbContext` like `DbSet(Type entityType)`

### 5. Entity List Endpoint

`GET /api/mcp` — returns all registered entities with their display names and descriptions:

```json
[
  { "name": "worklogs", "displayName": "Worklogs", "description": "Employee worklog entries", "table": "ResourceHours" },
  { "name": "expenses", "displayName": "Expenses", "description": "Expense report items", "table": "ExpenseReports" }
]
```

**CodeMemory refs:**
- Parameterized MCP routing with `ConfigureSessionOptions` + `AsyncLocal` context: `CodeMemory.AspNet/Program.cs:143-162`
- `IRepoContextAccessor` pattern → use same `IEntityContextAccessor` pattern

### 6. Adding a New Entity (Developer Workflow)

1. Define model class with `[Description]` + `[Table("PhysicalName")]`
2. Implement `IExcelParser<T>` for the Excel format
3. Register in `EntityRegistry`
4. Done — web UI + MCP endpoint auto-wired

## Migration Path from Phase 1

1. Create `EntityRegistry` and `IExcelParser<T>` in `EfMcp.Mcp`
2. Create `IEntityContextAccessor` (AsyncLocal-backed, mirrors `IRepoContextAccessor`)
3. Refactor `WorklogsExcelUploadService` → `WorklogsExcelParser : IExcelParser<Worklogs>`
4. Register Worklogs in `EntityRegistry`
5. Change `/api/mcp/Worklogs` route → `/api/mcp/{entityName}`, add session handler
6. Make `GenericSqlQueryTool` entity-aware
7. Refactor `WorklogsController` → generic `EntityController`
8. Add `GET /api/mcp` entity list

No breaking changes to existing endpoints if both old and new routes coexist during transition.

## Estimated Scope

| Work Item | Effort |
|-----------|--------|
| EntityRegistry + IEntityContextAccessor | Small |
| IExcelParser<T> interface + refactor Worklogs parser | Medium |
| Generic MCP route + GenericSqlQueryTool | Medium |
| Generic web UI controller + views | Medium |
| Entity list endpoint | Small |
| Integration tests for second entity | Medium |
