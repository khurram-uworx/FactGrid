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
dotnet test tests/FactGrid.Tests
dotnet ef migrations add <Name> --project src/FactGrid.AspNet
dotnet ef database update --project src/FactGrid.AspNet
```

---

## Project Structure

```
FactGrid/
├── src/FactGrid.AspNet/           # ASP.NET MVC web app (net10.0)
│   ├── Controllers/             # MVC controllers
│   ├── Data/                    # DbContext + EF migrations
│   ├── Models/                  # EF entity classes with [Description]
│   ├── Services/                # Business logic (Excel parsing, query validation)
│   ├── Tools/                   # MCP tool classes ([McpServerToolType])
│   ├── Views/                   # Razor views
│   └── Program.cs               # Startup — DI, MCP server, middleware
├── tests/FactGrid.Tests/           # NUnit tests
├── docs/                        # Phase plans, ADRs, task templates
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
- Entity name can differ from table name (e.g., `Worklogs` → `ResourceHours`)

### MCP Tools

- Tools are classes decorated with `[McpServerToolType]`
- Methods are decorated with `[McpServerTool]` and `[Description]` (from `System.ComponentModel`)
- Parameters use `[Description]` for LLM-facing docs
- Registered via `WithToolsFromAssembly(typeof(Tool).Assembly)` in `Program.cs`
- Route mapped with `app.MapMcp("/api/mcp/{name}").AllowAnonymous()`
- Tool names are auto-derived: `SqlQueryAsync` → `sql_query`, `DescribeAsync` → `describe`
- Tools return `Task<string>` for markdown or `Task<IDictionary<string, object?>>` for JSON
- Use `IServiceProvider.CreateScope()` to resolve scoped services (DbContext) from singleton tools

**Reference code:** `E:\khurram-uworx\CodeMemory\src\CodeMemory.AspNet\Tools\AspNetSqlQueryTool.cs`

### SqlParserCS (SELECT-only gate)

```csharp
var statements = new SqlQueryParser().Parse(query.AsSpan(), new GenericDialect());
if (statements.Count != 1) fail;
if (statements[0] is not Statement.Select) fail;
```

**Reference code:** `CodeMemory.AspNet/Tools/AspNetSqlQueryTool.cs:290-308`

### Excel Upload (ClosedXML)

- First sheet only, header row skipped (row 1), fixed 1-based column positions
- Returns `(List<T> Records, List<string> Errors)` — never throws on bad data
- Each caller uses `AddRange` + `SaveChangesAsync` for bulk insert

### Multi-Provider DB

- Configured via `Storage:Provider` in `appsettings.json`
- Supported: `"sqlserver"`, `"sqlite"`, `"postgresql"`
- Provider switch in `Program.cs` using `UseSqlServer` / `UseSqlite` / `UseNpgsql`

---

## NuGet Packages (key ones)

| Package | Version | Used For |
|---------|---------|----------|
| `ClosedXML` | 0.105.0 | Excel file parsing |
| `SqlParserCS` | 0.6.5 | SELECT-only SQL validation |
| `ModelContextProtocol` | 1.3.0 | MCP tool attributes |
| `ModelContextProtocol.AspNetCore` | 1.3.0 | Streamable HTTP transport |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.8 | SQLite provider |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.0 | PostgreSQL provider |

---

## Phase Plans

- `docs/PHASE1.md` — Core: Worklogs entity (complete)
- `docs/PHASE2.md` — Multi-entity support (future)
- `docs/PHASE3.md` — Two-MCP architecture: local stdio + remote web (future)

---

## ADR Style

ADRs should describe **what** the system does at an architectural level and **why**, without pinning down implementation method names, parameter types, or class signatures that can drift during implementation. Include intent, constraints, and tradeoffs; leave concrete API surface to the code. If an ADR contradicts the implementation, update the ADR toward the abstract intent — the implementation is the source of truth for specifics.

---

## Task Format

Use `docs/TASKS-TEMPLATE.md` for new task breakdowns. Each task must include: Priority, Goal, Scope, Acceptance Criteria, Files Likely Involved.
