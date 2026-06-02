# Phase 1 — Core: Worklogs Entity

## Goal

Build the initial EfMcp web app with a single entity (Worklogs), Excel upload, web UI for data management, and a Streamable HTTP MCP endpoint for querying.

## Entity: Worklogs

| Property       | SQL Column       | Type            | Excel Position | Excel Header              |
|----------------|------------------|-----------------|----------------|---------------------------|
| Id             | Id               | INT (PK, auto)  | —              | —                         |
| ResourceName   | ResourceName     | NVARCHAR(200)   | 1              | Resource Display Name     |
| Project        | Project          | NVARCHAR(300)   | 2              | Project                   |
| Description    | Description      | NVARCHAR(500)   | 3              | Description               |
| WorkDate       | WorkDate         | DATE            | 4              | Work Date                 |
| Hours          | Hours            | DECIMAL(10,2)   | 5              | Person Hours              |
| ApprovalStatus | ApprovalStatus   | NVARCHAR(50)    | 6              | Approval Workflow Status  |

- **C# entity name:** `Worklogs` (mapped to table `ResourceHours` via `[Table("ResourceHours")]`)
- **Excel file:** ProjecorExtract_*.xlsx, first sheet only, header row skipped, fixed column positions
- **Library:** ClosedXML for Excel parsing

## Project Structure

```
EfMcp/
├── src/
│   ├── EfMcp.AspNet/                    # MVC web app
│   │   ├── Controllers/
│   │   │   └── WorklogsController.cs    [NEW]
│   │   ├── Data/
│   │   │   └── ApplicationDbContext.cs  [MODIFY]
│   │   ├── Models/
│   │   │   └── Worklogs.cs             [NEW]
│   │   ├── Services/
│   │   │   └── ExcelUploadService.cs    [NEW]
│   │   ├── Views/Worklogs/
│   │   │   └── Index.cshtml            [NEW]
│   │   ├── Tools/
│   │   │   └── WorklogsMcpTools.cs     [NEW — thin wrapper if needed]
│   │   └── Program.cs                  [MODIFY]
│   │
│   ├── EfMcp.Mcp/                      [NEW — shared MCP class library]
│   │   ├── Tools/
│   │   │   └── WorklogsMcpTools.cs     [NEW — tool class + describe]
│   │   ├── Services/
│   │   │   └── QueryValidationService.cs [NEW — SqlParserCS wrapper]
│   │   └── EfMcp.Mcp.csproj
│   │
│   └── EfMcp.ServiceDefaults/          [NEW — shared telemetry/config; optional]
│
├── tests/
│   └── EfMcp.Tests/
│       ├── ExcelUploadServiceTests.cs  [NEW]
│       └── QueryValidationTests.cs     [NEW]
│
├── docs/
│   ├── PHASE1.md      ← you are here
│   ├── PHASE2.md
│   └── PHASE3.md
│
├── EfMcp.slnx                          [MODIFY — add new projects]
└── README.md
```

## Tasks

---

### Task 1: Create Worklogs entity model + migration

**Priority:** High

**Goal:** Define `Worklogs` entity class with `[Description]` attributes, mapped to `dbo.ResourceHours`, added to `ApplicationDbContext`.

**Scope:**
- Create `src/EfMcp.AspNet/Models/Worklogs.cs` with:
  - `[Table("ResourceHours")]` on the class
  - `[Description("...")]` on each property
  - Properties: `Id` (int, PK), `ResourceName` (string), `Project` (string), `Description` (string?), `WorkDate` (DateTime), `Hours` (decimal), `ApprovalStatus` (string)
- Add `DbSet<Worklogs> Worklogs { get; set; }` to `ApplicationDbContext`
- Run `dotnet ef migrations add AddWorklogsEntity`
- Apply migration: `dotnet ef database update`

```csharp
// Models/Worklogs.cs — sketch
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace EfMcp.AspNet.Models;

[Table("ResourceHours")]
public class Worklogs
{
    public int Id { get; set; }

    [Description("The name of the resource or person who performed the work")]
    public string ResourceName { get; set; } = string.Empty;

    [Description("The project, cost center, or activity code")]
    public string Project { get; set; } = string.Empty;

    [Description("Description of the work performed")]
    public string? Description { get; set; }

    [Description("Date the work was performed")]
    public DateTime WorkDate { get; set; }

    [Description("Number of hours worked")]
    public decimal Hours { get; set; }

    [Description("Approval status (e.g. Approved, Pending, Rejected)")]
    public string ApprovalStatus { get; set; } = string.Empty;
}
```

**CodeMemory refs:**
- EF entity pattern: `CodeMemory.AspNet/Storage/Models/SymbolEntity.cs` — simple POCO with data annotations
- DbContext + Fluent API: `CodeMemory.AspNet/Storage/CodeMemoryDbContext.cs` — `ToTable()`, `HasKey()`, `HasColumnName()` pattern

**Acceptance criteria:**
- `dotnet ef migrations add AddWorklogsEntity` creates migration with correct table/columns
- `dotnet ef database update` succeeds against SQL Server
- App starts without migration errors

**Files:**
- `src/EfMcp.AspNet/Models/Worklogs.cs` — new
- `src/EfMcp.AspNet/Data/ApplicationDbContext.cs` — modify (add DbSet)
- `src/EfMcp.AspNet/Data/Migrations/` — generated

---

### Task 2: Add ClosedXML and build Excel upload service

**Priority:** High

**Goal:** Parse the first sheet of the ProjecorExtract Excel file, mapping fixed column positions to `Worklogs` properties.

**Scope:**
- Add `ClosedXML` NuGet to `EfMcp.AspNet`
- Create `src/EfMcp.AspNet/Services/ExcelUploadService.cs`:
  - Load first worksheet from the uploaded file stream
  - Skip row 1 (header row — cells contain newlines/spaces, ignore entirely)
  - Map by ordinal (1-based) position:
    - Col 1 → `ResourceName`
    - Col 2 → `Project`
    - Col 3 → `Description` (nullable)
    - Col 4 → `WorkDate` (parse as `DateTime`)
    - Col 5 → `Hours` (parse as `decimal`)
    - Col 6 → `ApprovalStatus`
  - Validate: non-null required fields, parseable date/decimal
  - Collect per-row errors instead of throwing
  - Return `(List<Worklogs> Records, List<string> Errors)`
- Register in DI in `Program.cs`

**Handling edge cases:**
- Empty rows → skip silently
- Malformed date → add to Errors, skip row
- Non-numeric hours → add to Errors, skip row
- Blank required field → add to Errors, skip row

**CodeMemory refs:** None directly relevant — CodeMemory has no Excel parsing.

**Acceptance criteria:**
- Given a valid ProjecorExtract Excel file, returns correctly mapped `Worklogs` entities
- Malformed rows are accumulated in `Errors`, execution continues
- Header row with multiline text is properly skipped
- Empty worksheet returns empty result, no crash

**Files:**
- `src/EfMcp.AspNet/Services/ExcelUploadService.cs` — new
- `src/EfMcp.AspNet/EfMcp.AspNet.csproj` — add ClosedXML
- `src/EfMcp.AspNet/Program.cs` — register service

---

### Task 3: Build web UI for upload and data management

**Priority:** High

**Goal:** Web page at `/Worklogs` where users can see row count, upload Excel files, and delete all data.

**Scope:**
- Create `src/EfMcp.AspNet/Controllers/WorklogsController.cs`:
  - `GET /Worklogs` — query `DbContext.Worklogs.CountAsync()`, pass count to view
  - `POST /Worklogs/Upload` — accept `IFormFile`, call `ExcelUploadService`, bulk insert with `AddRange()` + `SaveChangesAsync()`, show result message
  - `POST /Worklogs/DeleteAll` — bulk delete with `ExecuteDeleteAsync()` (EF Core 10) or `RemoveRange(await db.Worklogs.ToListAsync())` + `SaveChangesAsync()`, redirect back
- Create `src/EfMcp.AspNet/Views/Worklogs/Index.cshtml`:
  - Show current row count prominently
  - File upload form (`enctype="multipart/form-data"`)
  - "Delete All" button with JavaScript confirmation dialog
  - Success/error message area using `TempData`
  - Use existing Identity `_Layout.cshtml` for consistent look
- No authorization checks (site has Identity, but page is open)
- Route follows default `{controller=Home}/{action=Index}/{id?}` pattern

```csharp
// WorklogsController.cs — structure
public class WorklogsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ExcelUploadService _excel;

    public async Task<IActionResult> Index()
    {
        var count = await _db.Worklogs.CountAsync();
        return View(count);
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        // validate file
        // parse via ExcelUploadService
        // AddRange + SaveChangesAsync
        // TempData message
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAll()
    {
        await _db.Worklogs.ExecuteDeleteAsync();
        TempData["Message"] = "All records deleted.";
        return RedirectToAction(nameof(Index));
    }
}
```

**Acceptance criteria:**
- `/Worklogs` shows "0 records" when table is empty
- Uploading a valid Excel file inserts rows, shows updated count + success message
- Uploading an invalid/non-Excel file shows error message, no rows inserted
- Delete All removes all rows, count resets to 0, confirmation shown
- Page renders with the Identity site layout

**Files:**
- `src/EfMcp.AspNet/Controllers/WorklogsController.cs` — new
- `src/EfMcp.AspNet/Views/Worklogs/Index.cshtml` — new

---

### Task 4: Create EfMcp.Mcp class library with SqlParserCS + MCP tools

**Priority:** High

**Goal:** A shared class library containing MCP tool classes and SqlParserCS query validation — reusable by both the ASP.NET host (Phase 1) and a future stdio console host (Phase 3).

**Scope:**

#### 4a. Create the project

- `src/EfMcp.Mcp/EfMcp.Mcp.csproj` targeting `net10.0`
- Add NuGet: `SqlParserCS` (version 0.6.5, matching CodeMemory)
- Add project reference from `EfMcp.AspNet` → `EfMcp.Mcp`
- Add to `EfMcp.slnx`

#### 4b. QueryValidationService

`src/EfMcp.Mcp/Services/QueryValidationService.cs`:

```csharp
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace EfMcp.Mcp.Services;

public class QueryValidationService
{
    private static readonly GenericDialect Dialect = new();
    private readonly SqlQueryParser _parser = new();

    public ValidationResult Validate(string query)
    {
        try
        {
            var statements = _parser.Parse(query.AsSpan(), Dialect);

            if (statements.Count != 1)
                return Fail("Only single-statement queries are supported");

            if (statements[0] is not Statement.Select)
                return Fail("Only SELECT statements are supported");

            return Pass();
        }
        catch (Exception ex)
        {
            return Fail($"Parse error: {ex.Message}");
        }
    }

    public record ValidationResult(bool IsValid, string? Error)
    {
        public void Deconstruct(out bool IsValid, out string? Error)
        {
            IsValid = this.IsValid;
            Error = this.Error;
        }
    }

    private static ValidationResult Pass() => new(true, null);
    private static ValidationResult Fail(string error) => new(false, error);
}
```

**CodeMemory ref — exact pattern to follow:**
- `CodeMemory.AspNet/Tools/AspNetSqlQueryTool.cs:290-308` — the `validateQuery()` method

#### 4c. WorklogsMcpTools

`src/EfMcp.Mcp/Tools/WorklogsMcpTools.cs`:

```csharp
using System.ComponentModel;
using System.Reflection;
using System.Text;
using EfMcp.Mcp.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Attributes;

namespace EfMcp.Mcp.Tools;

[McpServerToolType]
public class WorklogsMcpTools
{
    private readonly QueryValidationService _validator;

    // Resolved via factory — EF Core DbContext is scoped, tools are singletons
    private readonly IServiceProvider _serviceProvider;

    public WorklogsMcpTools(QueryValidationService validator, IServiceProvider serviceProvider)
    {
        _validator = validator;
        _serviceProvider = serviceProvider;
    }

    [McpServerTool, Description(@"
Execute SELECT-only SQL queries against the Worklogs (ResourceHours) entity.

Returns results as a markdown table. Only single-statement SELECT queries are allowed.
Use the DescribeAsync tool to see the entity schema.
")]
    public async Task<string> SqlQueryAsync(
        [Description("SQL query string (e.g. SELECT * FROM ResourceHours WHERE ResourceName LIKE '%John%'")] string query,
        [Description("Maximum number of rows to return (default 100, max 10000]")] int maxResults = 100)
    {
        // 1. Validate with SqlParserCS
        var (isValid, error) = _validator.Validate(query);
        if (!isValid) return $"Error: {error}";

        // 2. Cap maxResults
        maxResults = Math.Clamp(maxResults, 1, 10000);

        // 3. Execute query via ADO.NET through EF Core's connection
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = query;
        command.Parameters.Clear();

        await db.Database.OpenConnectionAsync();

        await using var reader = await command.ExecuteReaderAsync();

        // 4. Format as markdown table
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
        var type = typeof(Worklogs);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

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
                Type t when t == typeof(DateTime) => "DATE",
                Type t when t == typeof(bool) => "BIT",
                _ => prop.PropertyType.Name
            };
            sb.AppendLine($"| {prop.Name} | {colType} | {desc} |");
        }

        return Task.FromResult(sb.ToString());
    }
}
```

**CodeMemory refs:**
- SqlParserCS validation: `CodeMemory.AspNet/Tools/AspNetSqlQueryTool.cs:290-308`
- MCP tool attributes: `CodeMemory.Mcp/Tools/SqlQueryTool.cs` — `[McpServerToolType]`, `[McpServerTool]`, `[Description]` pattern
- ADO.NET execution: `CodeMemory.AspNet/Tools/AspNetSqlQueryTool.cs:168-198` — `CreateCommand()`, `ExecuteReaderAsync()`, markdown formatting

#### 4d. Register in Program.cs

```csharp
// Add MCP server
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly(typeof(WorklogsMcpTools).Assembly);

// …later, after app is built…
app.MapMcp("/api/mcp/Worklogs");
```

Add `[AllowAnonymous]` to the MCP endpoint — either on the controller level or by adding a metadata policy in `Program.cs`:

```csharp
app.MapMcp("/api/mcp/Worklogs")
   .AllowAnonymous();  // or use RequireAuthorization() later
```

**Acceptance criteria:**
- `POST /api/mcp/Worklogs` with `{ "tool": "sql_query", "args": { "query": "SELECT * FROM ResourceHours" } }` returns markdown table
- `POST /api/mcp/Worklogs` with `{ "tool": "describe" }` returns column schema with descriptions
- Non-SELECT queries return error message
- Invalid SQL returns parse error message
- Endpoint is accessible without authentication
- Same tool classes can be referenced from a console app (no ASP.NET dependency)

**Files:**
- `src/EfMcp.Mcp/EfMcp.Mcp.csproj` — new
- `src/EfMcp.Mcp/Services/QueryValidationService.cs` — new
- `src/EfMcp.Mcp/Tools/WorklogsMcpTools.cs` — new
- `src/EfMcp.AspNet/EfMcp.AspNet.csproj` — add project reference
- `src/EfMcp.AspNet/Program.cs` — modify (register MCP)
- `EfMcp.slnx` — modify (add project)

---

### Task 5: Multi-provider database support

**Priority:** Low

**Goal:** Make SQLite and PostgreSQL configurable alternatives to SQL Server.

**Scope:**
- Add NuGet packages: `Microsoft.EntityFrameworkCore.Sqlite`, `Npgsql.EntityFrameworkCore.PostgreSQL`
- Update `Program.cs` to select provider based on config:
```csharp
var provider = builder.Configuration.GetValue<string>("Storage:Provider") ?? "sqlserver";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    switch (provider.ToLowerInvariant())
    {
        case "sqlite":
            options.UseSqlite(connectionString);
            break;
        case "postgresql":
            options.UseNpgsql(connectionString);
            break;
        case "sqlserver":
        default:
            options.UseSqlServer(connectionString);
            break;
    }
});
```
- Add example connection strings to `appsettings.json`

**CodeMemory refs:**
- Provider selection pattern: `CodeMemory.AspNet/Program.cs:233-287` — `Storage:Provider` config key, factory-based provider resolution
- Provider-specific schema setup: `CodeMemory.AspNet/Program.cs` — `ensureSqlServerSchemaExists()`, SQLite file path creation

**Acceptance criteria:**
- Setting `Storage:Provider` to `"sqlite"` with a file-based connection string works and creates the database
- Setting to `"postgresql"` with a valid PostgreSQL connection string works
- All existing functionality (upload, query, MCP) works with each provider

**Files:**
- `src/EfMcp.AspNet/EfMcp.AspNet.csproj` — add packages
- `src/EfMcp.AspNet/Program.cs` — modify (provider selection)
- `src/EfMcp.AspNet/appsettings.json` — modify (example configs)

---

### Task 6: Unit tests

**Priority:** Medium

**Goal:** Test Excel upload mapping and SqlParserCS query validation.

**Scope:**

- `tests/EfMcp.Tests/ExcelUploadServiceTests.cs`:
  - Create in-memory `.xlsx` using ClosedXML with known data
  - Test correct column mapping
  - Test header row skip
  - Test malformed values produce errors
  - Test empty sheet returns empty

- `tests/EfMcp.Tests/QueryValidationTests.cs`:
  - Valid SELECT passes
  - INSERT, UPDATE, DELETE, DROP, ALTER rejected
  - Multi-statement queries rejected
  - Malformed SQL returns parse error
  - SELECT with subqueries, CTEs, JOINs passes

- `tests/EfMcp.Tests/WorklogsMcpToolsTests.cs`:
  - Use in-memory SQLite provider
  - Seed test data
  - Test `SqlQueryAsync` returns markdown table
  - Test `DescribeAsync` returns column info with descriptions
  - Test invalid query returns error message

**Acceptance criteria:**
- `dotnet test` passes all tests

**Files:**
- `tests/EfMcp.Tests/ExcelUploadServiceTests.cs` — new
- `tests/EfMcp.Tests/QueryValidationTests.cs` — new
- `tests/EfMcp.Tests/WorklogsMcpToolsTests.cs` — new

---

## Execution Order

```
Task 1 ──┬── Task 2 ──┬── Task 3
          │            │
          └── Task 4 ──┤
                       │
                       ├── Task 6 (can start after Task 2 + 4)
                       │
                       └── Task 5 (anytime, independent)
```

**Dependencies:**
- **Task 1** — prerequisite for all other tasks
- **Task 2** — depends on Task 1 (entity model exists)
- **Task 3** — depends on Task 1 + 2 (needs entity + upload service)
- **Task 4** — depends on Task 1 (needs entity for describe + query); can run in parallel with Task 2
- **Task 6** — can start after Task 2 + 4 are stable
- **Task 5** — independent, can be done anytime
