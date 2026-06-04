# FactGrid— AGENTS.md

## Purpose

Engineering constraints and implementation guidance for AI coding agents contributing to FactGrid.

**First read [README.md](README.md)** for the project overview, quick start, and tool reference.

---

## GitHub

- **Repo:** `khurram-uworx/FactGrid`
- All `gh` commands require `--repo khurram-uworx/FactGrid`

---

## Build / Run / Test

```bash
dotnet build src/FactGrid.AspNet
dotnet run --project src/FactGrid.AspNet
dotnet run --project src/FactGrid.Mcp
dotnet test tests/FactGrid.Tests
dotnet ef migrations add <Name> --project src/FactGrid.AspNet
dotnet ef database update --project src/FactGrid.AspNet
```

---

## Project Structure

```
FactGrid/
├── src/FactGrid/                # Shared library (net10.0)
│   ├── Models/                # EF entity classes, [ExcelColumn] attribute, IngestionResult contract
│   ├── Services/              # Parsers, ExcelColumnMetadata, ExcelDateHelper, ExcelTemplateGenerator, EntityRegistry, EntitySchemaHelper, ServiceCollectionExtensions
│   └── FactGridEntityCatalog.cs  # Static catalog of supported entity definitions
├── src/FactGrid.AspNet/           # ASP.NET MVC web app (net10.0)
│   ├── Controllers/             # MVC + API controllers
│   ├── Data/                    # DbContext + EF migrations
│   ├── Services/                # Persistence-specific services (IEntityTableService, etc.)
│   ├── Tools/                   # HTTP MCP tool classes ([McpServerToolType])
│   ├── Views/                   # Razor views
│   └── Program.cs               # Startup — DI, MCP server, middleware, provider config
├── src/FactGrid.Mcp/             # Local STDIO MCP console app (net10.0)
│   ├── Tools/DataEntryTools.cs   # 4 STDIO MCP tools (generate_template, validate_excel, upload_excel, list_entities)
│   └── Program.cs               # Startup — DI, STDIO MCP server
├── tests/FactGrid.Tests/           # NUnit tests (199+ tests)
├── docs/                        # Phase plans, getting-started guide, task templates
└── AGENTS.md                    # ← you are here
```

---

## Domain Boundaries

Do not reinvent infrastructure. Prefer existing .NET and ecosystem primitives over custom solutions.

---

## Key Patterns

### Entity Model

- `[Table("PhysicalTableName")]` on the class
- `[Description("...")]` on each property for MCP schema discovery
- `[MaxLength(n)]` for string length constraints
- `[Column(TypeName = "decimal(10,2)")]` for precision
- `[ExcelColumn(position, "Title", Required = ..., Example = ...)]` for Excel metadata
- Entity name can differ from table name (e.g., `Worklogs` → `ResourceHours`)

### Shared FactGrid Library (`src/FactGrid/`)

- **Models**: Entity classes with both EF and `[ExcelColumn]` metadata; `IngestionResult` response contract
- **ExcelColumnAttribute**: `Position`, `Title`, `Required`, `Example`, `Format`
- **ExcelColumnMetadata**: Cache-backed reader for `GetColumns()`, `GetColumnIndex()`, `Validate()`, `ValidateRequired()`
- **Parsers** (`WorklogsExcelParser`, `ExpensesExcelParser`): Implement `IExcelParser<T>`. Use `ExcelColumnMetadata.GetColumnIndex()` for metadata-driven column position lookup. Accept typed DateTime cells and text dates (`M/d/yyyy h:mm:ss tt`, `yyyy-MM-dd`). Never throw on bad data — return errors list.
- **ExcelDateHelper**: `ParseDateText()` accepts both date formats above
- **ExcelTemplateGenerator**: Generates `.xlsx` with typed example values, `yyyy-mm-dd` for date cells, `0.00` for decimal cells
- **EntityRegistry**: Singleton runtime registry. Populated by `AddFactGridEntities()` in `ServiceCollectionExtensions`
- **FactGridEntityCatalog**: Static class listing all supported entity definitions (names, types, parser types, table names)
- **EntitySchemaHelper**: Builds structured schema metadata from entity models
- **ServiceCollectionExtensions**: `AddFactGridEntities()` — registers registry, template generator, and all parsers. `IEntityTableService<>` open-generic is registered by the ASP.NET host in `Program.cs`.

### MCP Tools — Central HTTP (`FactGrid.AspNet`)

- Classes decorated with `[McpServerToolType]`
- Methods: `[McpServerTool]` + `[Description]`
- Registered via `WithToolsFromAssembly(typeof(Tool).Assembly)` in `Program.cs`
- Route mapped with `app.MapMcp("/api/mcp/{name}").AllowAnonymous()`
- Tool names auto-derived: `SqlQueryAsync` → `sql_query`, `DescribeAsync` → `describe`
- Tools return `Task<string>` for markdown or `Task<IDictionary<string, object?>>` for JSON
- Use `IServiceProvider.CreateScope()` to resolve scoped services (DbContext) from singleton tools

**Reference code:** `E:\khurram-uworx\CodeMemory\src\CodeMemory.AspNet\Tools\AspNetSqlQueryTool.cs`

### MCP Tools — Local STDIO (`FactGrid.Mcp`)

- Single `DataEntryTools` class with 4 tools:
  - `generate_template(entityName, outputPath)` — requires `.xlsx` extension on outputPath
  - `validate_excel(entityName, filePath)` — up to 20-record preview; catches corrupt workbooks with error message
  - `upload_excel(entityName, filePath)` — requires `FACTGRID_SERVER_URL` env var; validates locally before HTTP upload; deserializes `IngestionResult` response
  - `list_entities()` — uses `ExcelColumnMetadata.GetColumns()` for ordered column display
- Registered in `Program.cs` via `WithToolsFromAssembly(typeof(DataEntryTools).Assembly)`
- Resolves parsers from scoped DI using `IServiceProvider.CreateScope()`
- All tools return string results (never throw on invalid input)

### SqlParserCS (SELECT-only gate)

```csharp
var statements = new SqlQueryParser().Parse(query.AsSpan(), new GenericDialect());
if (statements.Count != 1) fail;
if (statements[0] is not Statement.Select) fail;
```

**Reference code:** `CodeMemory.AspNet/Tools/AspNetSqlQueryTool.cs:290-308`

### Excel Upload & Parsing

- First sheet only, header row skipped (row 1), column positions from `[ExcelColumn]` metadata
- Parsers return `(IList Records, List<string> Errors)` — never throws on bad data
- Shared `ExcelColumnMetadata.ValidateRequired()` checks `[ExcelColumn(Required = true)]` fields before row-level parsing
- IngestionController uses inner try-catch for malformed workbooks (structured 400) and outer try-catch for unexpected failures (structured 500)
- UnprocessableEntity (422) returned when workbook parses but contains validation errors; no records inserted

### Multi-Provider DB

- Configured via `Storage:Provider` in `appsettings.json`
- Supported: `"sqlserver"`, `"sqlite"`, `"postgresql"`
- Provider switch in `Program.cs` using `UseSqlServer` / `UseSqlite` / `UseNpgsql`

### Entity Registry

- Singleton `EntityRegistry` maps entity names to model types, table names, Excel parser types, and descriptions
- Populated via `AddFactGridEntities()` extension method
- Each entity needs: model class, `DbSet<T>` in `ApplicationDbContext`, `IExcelParser<T>` implementation, entry in `FactGridEntityCatalog`
- Provides `Get(entityName)` for per-entity routing in both MCP and web UI controllers
- Both hosts use the same shared registry via `AddFactGridEntities()`

---

## NuGet Packages (key ones)

| Package | Used For |
|---------|----------|
| `ClosedXML` | Excel file parsing (shared FactGrid library) |
| `SqlParserCS` | SELECT-only SQL validation (AspNet only) |
| `ModelContextProtocol` | MCP tool attributes (both hosts) |
| `ModelContextProtocol.AspNetCore` | Streamable HTTP transport (AspNet only) |
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite provider (AspNet only) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL provider (AspNet only) |

---

## ADR Style

ADRs should describe **what** the system does at an architectural level and **why**, without pinning down implementation method names, parameter types, or class signatures that can drift during implementation. Include intent, constraints, and tradeoffs; leave concrete API surface to the code. If an ADR contradicts the implementation, update the ADR toward the abstract intent — the implementation is the source of truth for specifics.

---

## Task Format

Use `docs/TASKS-TEMPLATE.md` for new task breakdowns. Each task must include: Priority, Goal, Scope, Acceptance Criteria, Files Likely Involved.
