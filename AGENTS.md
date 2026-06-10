# FactGrid— AGENTS.md

## Purpose

Engineering constraints and implementation guidance for AI coding agents contributing to FactGrid.

Read [README.md](README.md) for the product architecture and [GETTING-STARTED.md](GETTING-STARTED.md) for operating instructions.

---

## GitHub

- **Repo:** `khurram-uworx/FactGrid`
- All `gh` commands require `--repo khurram-uworx/FactGrid`

---

## Contributor Verification

```bash
dotnet build FactGrid.slnx
dotnet test tests/FactGrid.Tests
dotnet ef migrations has-pending-model-changes --project src/FactGrid.AspNet
git diff --check
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
├── docs/                        # Task templates and architecture decision records
├── GETTING-STARTED.md           # Installation, configuration, and operation
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
- **Parsers** (`WorklogsExcelParser`, `ExpensesExcelParser`): Implement `IExcelParser<T>`. Use `ExcelColumnMetadata.GetColumnIndex()` for metadata-driven column position lookup. Never throw on bad data — return errors list.
- **ExcelDateHelper**: Owns the accepted invariant text-date parsing rules
- **ExcelTemplateGenerator**: Generates typed `.xlsx` examples using entity metadata
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

- Keep local data-entry tools in the single `DataEntryTools` class.
- Registered in `Program.cs` via `WithToolsFromAssembly(typeof(DataEntryTools).Assembly)`
- Resolves parsers from scoped DI using `IServiceProvider.CreateScope()`
- Tool methods return controlled string results instead of throwing for user-controlled input.
- Derive entity and column information from the shared registry and metadata readers.

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
- Keep machine ingestion structured and atomic; validation failures must insert no records.

### OpenIddict OAuth (ASP.NET Core host)

- **OpenIddict 7.x+** does NOT require implementing `IOpenIddictDbContext` or declaring `DbSet<>` properties for OpenIddict entities. All entity registration is handled by `options.UseOpenIddict()` on the `DbContextOptionsBuilder` — this is called once in `Program.cs`.
- Server config (`AddServer`): call `UseAspNetCore()` with `EnableAuthorizationEndpointPassthrough()`, `EnableTokenEndpointPassthrough()`, `EnableEndSessionEndpointPassthrough()`.
- Validation config (`AddValidation`): call `UseLocalServer()` + `UseAspNetCore()`.
- Use `Scopes.OpenId` / `Scopes.Email` / `Scopes.Profile` / `Scopes.OfflineAccess` constants from `OpenIddictConstants` (import `using static OpenIddict.Abstractions.OpenIddictConstants`) rather than string literals.
- Client seed: use `IOpenIddictApplicationManager.CreateAsync(new OpenIddictApplicationDescriptor { ... })` with `ConsentTypes.Explicit`, `Permissions.Endpoints.*`, `Permissions.GrantTypes.*`, `Permissions.Scopes.*`, `Permissions.Prefixes.Scope + "custom_scope"`, and `Requirements.Features.ProofKeyForCodeExchange`.
- `ConfigureTestServices()` is NOT available on `IWebHostBuilder` in this .NET 10 / Microsoft.AspNetCore.Mvc.Testing 10.0.8 setup. Use `ConfigureServices` with `services.Configure<AuthenticationOptions>(o => { ... })` to override defaults in integration tests instead.
- NuGet packages needed: `OpenIddict`, `OpenIddict.EntityFrameworkCore`, `OpenIddict.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`.

### Multi-Provider DB

- Keep provider selection centralized in the ASP.NET host's `Program.cs`.
- Do not introduce provider-specific behavior into the shared library.

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
| `OpenIddict` | OAuth 2.0 / OpenID Connect server (AspNet only) |
| `OpenIddict.EntityFrameworkCore` | OpenIddict EF Core stores (AspNet only) |
| `OpenIddict.AspNetCore` | OpenIddict ASP.NET Core integration (AspNet only) |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT Bearer token validation (AspNet only) |
| `ModelContextProtocol.AspNetCore` | Streamable HTTP transport (AspNet only) |
| `Microsoft.EntityFrameworkCore.Sqlite` | SQLite provider (AspNet only) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL provider (AspNet only) |

---

## ADR Style

ADRs should describe **what** the system does at an architectural level and **why**, without pinning down implementation method names, parameter types, or class signatures that can drift during implementation. Include intent, constraints, and tradeoffs; leave concrete API surface to the code. If an ADR contradicts the implementation, update the ADR toward the abstract intent — the implementation is the source of truth for specifics.

---

## Task Format

Use `docs/TASKS-TEMPLATE.md` for new task breakdowns. Each task must include: Priority, Goal, Scope, Acceptance Criteria, Files Likely Involved.
