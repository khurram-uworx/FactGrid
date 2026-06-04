# Phase 3 — Dual-MCP Architecture: Data Entry + Reporting

## Goal

Two MCP servers with distinct responsibilities:

| Side | Server | Transport | Purpose |
|------|--------|-----------|---------|
| **Left / Input** | `FactGrid.Mcp` | STDIO | Data entry — generate Excel templates, validate files, upload data |
| **Right / Output** | `FactGrid.AspNet` | HTTP | Reporting — query data, describe schemas, serve web UI |

The shared `FactGrid` library is the single source of truth for both sides. It owns entity models (`Worklog`, `Expense`), `[ExcelColumn]` metadata, metadata-driven parsers, entity-specific validation, template generation, and the shared entity catalog. Hosts own only transport- and persistence-specific behavior.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                   Data Entry Agent                           │
│          (opencode / Cline / coding agent)                   │
│                                                              │
│  "Generate a template Excel for expenses"                    │
│  "Validate my expenses.xlsx and upload it"                   │
└──────────────────────────┬───────────────────────────────────┘
                           │ STDIO
                           ▼
┌──────────────────────────────────────────────────────────────┐
│              FactGrid.Mcp  (STDIO / Console)                  │
│                                                              │
│  Tools:                                                      │
│    - generate_template(entityName, outputPath) → saved path  │
│    - validate_excel(entityName, filePath) → errors/preview   │
│    - upload_excel(entityName, filePath) → POST to HTTP server│
│    - list_entities() → available entity types + column meta  │
│                                                              │
│  References FactGrid (models, parsers, registry)             │
│  No local database — reads filesystem, pushes via HTTP       │
│  Configured via FACTGRID_SERVER_URL env var                  │
└──────────────────────────┬───────────────────────────────────┘
                           │ HTTPS (machine-facing file upload:
                           │  POST /api/ingestion/{entityName}/upload)
                           ▼
┌──────────────────────────────────────────────────────────────┐
│              FactGrid.AspNet  (HTTP / Web Server)              │
│                                                              │
│  ┌──────────────────┐   ┌──────────────────────────────┐    │
│  │  Web UI           │   │  HTTP MCP (Phase 2)         │    │
│  │  /Entity/{name}   │   │  /api/mcp/{entityName}      │    │
│  │  upload + manage  │   │                              │    │
│  └──────────────────┘   │  Tools:                       │    │
│                          │    - sql_query(SELECT)       │    │
│  ┌──────────────────┐   │    - describe()              │    │
│  │  Database         │   │  Resources:                  │    │
│  │  (SQL Server /    │   │    - entities://list         │    │
│  │   SQLite / PG)    │   │    - entities://schema       │    │
│  └──────────────────┘   │  Prompts:                     │    │
│                          │    - entities-guide          │    │
│                          │    - entity-guide            │    │
│                          └──────────────────────────────┘    │
│                                                              │
│  References FactGrid (models, parsers, registry)             │
└──────────────────────────────────────────────────────────────┘
                           ▲
                           │
┌──────────────────────────────────────────────────────────────┐
│                   Web AI / Chat Agent                         │
│                                                              │
│  "Show me the worklog report for last month"                 │
│  "How many hours did Khurram log in Q1?"                     │
└──────────────────────────────────────────────────────────────┘
```

## Components

### `FactGrid` — Shared Library [NEW]

A class library (`src/FactGrid/FactGrid.csproj`) containing everything both MCPs need:

- **Models** (`Models/Entities.cs`) — `Worklog`, `Expense` (moved from `FactGrid.AspNet`)
- **`[ExcelColumn]` attribute** (`Models/ExcelColumnAttribute.cs`) — [NEW] decorates entity properties with:
  - `Position` (1-based column index in Excel)
  - `Title` (column header text in generated template)
  - `Required` (whether the field must have a value)
  - `Example` (dummy value for template generation)
  - Optional format metadata where a type needs an explicit workbook display format
- **Excel metadata reader and template generator** — reads `[ExcelColumn]` metadata and generates typed/formatted `.xlsx` templates using ClosedXML
- **Excel parsers** (`Services/WorklogsExcelParser.cs`, `Services/ExpensesExcelParser.cs`) — moved from `FactGrid.AspNet` and updated to resolve columns from `[ExcelColumn.Position]` rather than hard-coded positions
- **Shared parsing rules** — handle required fields, common type conversion, typed Excel date cells, and documented fallback text formats
- **Entity-specific parser rules** — retain validation that cannot be expressed by common metadata
- **`EntityRegistry`** (`Services/EntityRegistry.cs`) — moved from `FactGrid.AspNet`
- **`FactGridEntityCatalog`** (`Services/FactGridEntityCatalog.cs`) — [NEW] creates and populates the registry once for both hosts
- **`IExcelParser<T>` / `IExcelParser`** (`Services/IExcelParser.cs`) — moved from `FactGrid.AspNet`

**Key principle:** The shared library defines the complete entity contract. Adding a new entity means creating its model and parser, annotating the model with `[ExcelColumn]`, and registering it once in `FactGridEntityCatalog`. Both hosts consume that same catalog and therefore discover the entity automatically.

### Shared Entity Contract

The shared entity contract has distinct responsibilities:

- `[ExcelColumn]` defines Excel column position, title, required status, example value, and any common formatting metadata needed for template generation and parsing.
- Shared parsing code uses the same metadata used by template generation; column positions must never be duplicated as parser constants.
- Entity-specific parsers apply domain validation and construct entity records after common conversion.
- `[Description]`, EF mapping attributes, and entity property types continue to drive reporting schema and database mapping.
- `FactGridEntityCatalog` is the only place where supported entities and parser types are registered.

This does not mean every concern is implemented solely through attributes. Attributes define the common schema; parsers remain authoritative for entity-specific validation and conversion.

### `FactGrid.Mcp` — STDIO MCP Console App [NEW]

A .NET console app (`src/FactGrid.Mcp/FactGrid.Mcp.csproj`) for data entry operators:

- **Target:** `net10.0`, **OutputType:** `Exe`
- **NuGet deps:** `ModelContextProtocol` (1.3.0), `Microsoft.Extensions.Hosting` (10.0.8)
- **References:** `FactGrid` (shared library)
- **Transport:** `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`
- **No database** — reads filesystem, POSTs .xlsx files to the HTTP server
- **Server URL** configured via `FACTGRID_SERVER_URL` environment variable
- **Tool registration:** `WithToolsFromAssembly(typeof(DataEntryTools).Assembly)`
- **Template output** — writes `.xlsx` files locally using ClosedXML; binary workbook data is not returned as base64

### `FactGrid.AspNet` — HTTP Web Server [MODIFIED]

Existing web app with these changes:

- Removes entity models, parser implementations, `EntityRegistry` (now in `FactGrid`)
- Adds project reference to `FactGrid`
- HTTP MCP tools, web UI, existing MVC upload action, and `ApplicationDbContext` remain available
- Adds a dedicated machine-facing `POST /api/ingestion/{entityName}/upload` endpoint for `FactGrid.Mcp`

### Ingestion API Contract

The dedicated ingestion endpoint is intentionally separate from the MVC upload action:

- Accepts a multipart `.xlsx` file.
- Resolves the entity and parser through the shared `FactGridEntityCatalog`.
- Re-parses and validates the complete file on the server.
- Is atomic: if any validation error exists, inserts zero records.
- Returns structured JSON with `success`, `insertedCount`, and `errors`; it never redirects to an HTML page.
- Uses normal HTTP status semantics while preserving the same response shape: success for inserted data, client error for invalid files or unknown entities, and server error for unexpected failures.
- Authentication and authorization are explicitly deferred from Phase 3. The separate endpoint establishes a boundary where security can be added later without changing the STDIO tool contract.

Example successful response:

```json
{
  "success": true,
  "insertedCount": 42,
  "errors": []
}
```

Example validation response:

```json
{
  "success": false,
  "insertedCount": 0,
  "errors": [
    "Row 7: Amount is required"
  ]
}
```

## Tool Inventory

### STDIO MCP Tools (`FactGrid.Mcp`)

| Tool | Description | Returns |
|------|-------------|---------|
| `generate_template(entityName, outputPath)` | Generates and saves `.xlsx` with headers from `[ExcelColumn]` attributes + one typed/formatted dummy row from `Example` values | Generated path + summary |
| `validate_excel(entityName, filePath)` | Parses the file using the registered `IExcelParser<T>`, returns row count + validation errors | Markdown table + error list |
| `upload_excel(entityName, filePath)` | POSTs the `.xlsx` file to `FactGrid.AspNet`'s `/api/ingestion/{entityName}/upload` endpoint | Structured success/error result |
| `list_entities()` | Returns all registered entities with column metadata and descriptions | Markdown |

### HTTP MCP Tools (`FactGrid.AspNet` — unchanged from Phase 2)

| Tool | Description |
|------|-------------|
| `sql_query(query)` | SELECT-only SQL execution (validated by `QueryValidationService`) |
| `describe()` | Entity schema from `[Description]` attributes |
| Resources: `entities://list`, `entities://schema` | Available entities and column metadata |
| Prompts: `entities-guide`, `entity-guide` | Entity usage guides |

## Workflow

```
Data Entry (STDIO MCP):
  1. Agent: "Generate an expense template"
  2. FactGrid.Mcp: reads entity model + [ExcelColumn] attributes →
     generates .xlsx with headers + typed/formatted dummy row → saves locally
  3. Tool returns generated path → human opens, fills in data, saves
  4. Agent: "Validate my expenses.xlsx"
  5. FactGrid.Mcp: reads file, parses with ExpensesExcelParser →
     returns preview + error count
  6. Agent: "Upload it"
  7. FactGrid.Mcp: HTTP POST to FACTGRID_SERVER_URL/api/ingestion/expenses/upload
  8. FactGrid.AspNet: server-side re-parses and validates the complete file
  9. FactGrid.AspNet: atomically stores all records, or stores none if errors exist

Reporting (HTTP MCP):
  1. Agent: "Show me worklog hours for March"
  2. FactGrid.AspNet: runs SELECT query → returns markdown table
  3. Agent renders the table
```

## Project Structure Changes

```
src/
├── FactGrid/                          [NEW] class library
│   ├── FactGrid.csproj
│   ├── Models/
│   │   ├── Entities.cs                ← moved from FactGrid.AspNet
│   │   └── ExcelColumnAttribute.cs    [NEW]
│   └── Services/
│       ├── IExcelParser.cs            ← moved from FactGrid.AspNet
│       ├── ExcelTemplateGenerator.cs  [NEW] metadata-driven ClosedXML output
│       ├── WorklogsExcelParser.cs     ← moved from FactGrid.AspNet
│       ├── ExpensesExcelParser.cs     ← moved from FactGrid.AspNet
│       ├── EntityRegistry.cs          ← moved from FactGrid.AspNet
│       ├── FactGridEntityCatalog.cs   [NEW] one shared entity catalog
│       └── ServiceCollectionExtensions.cs [NEW] shared DI registration
│
├── FactGrid.Mcp/                      [NEW] console app (STDIO)
│   ├── FactGrid.Mcp.csproj
│   ├── Program.cs
│   ├── Tools/
│   │   └── DataEntryTools.cs          generate_template, validate_excel,
│   │                                       upload_excel, list_entities
│   └── server.json                    (optional, for NuGet MCP registry)
│
├── FactGrid.AspNet/                   [MODIFIED]
│   ├── FactGrid.AspNet.csproj         + reference FactGrid; remove internals
│   ├── Program.cs                     use shared entity/DI registration
│   ├── Controllers/
│   │   └── IngestionController.cs     [NEW] machine-facing atomic upload API
│   ├── Data/                          unchanged (DbContext + migrations)
│   ├── Services/                      keep: EntityContextAccessor,
│   │                                    EntityServiceFactory,
│   │                                    EntityTableService,
│   │                                    QueryValidationService
│   └── Tools/                         keep: GenericSqlQueryTool,
│                                          EntityPrompts, EntityResources
└── tests/FactGrid.Tests/              [MODIFIED] update project references
```

## Migration Steps

1. **Create `src/FactGrid/` class library project**
   - `dotnet new classlib -n FactGrid -o src/FactGrid`
   - Target `net10.0`
   - Add `ClosedXML` and `Microsoft.Extensions.DependencyInjection.Abstractions` package references
   
2. **Move shared code into `FactGrid`**
   - Move `Models/Entities.cs` → `FactGrid/Models/`
   - Move `Services/IExcelParser.cs`, `WorklogsExcelParser.cs`, `ExpensesExcelParser.cs` → `FactGrid/Services/`
   - Move `Services/EntityRegistry.cs` → `FactGrid/Services/`
   - Update namespaces from `FactGrid.AspNet.Services` to `FactGrid.Services`

3. **Add the shared entity contract**
   - Create `Models/ExcelColumnAttribute.cs` in `FactGrid`
   - Annotate `Worklog` and `Expense` properties with `[ExcelColumn(Position, Title, Required, Example)]` and common formatting metadata where needed
   - Create metadata helpers used by both template generation and parsing
   - Update parsers to resolve columns from metadata instead of hard-coded positions
   - Parse typed Excel dates first, then documented invariant fallback text formats; do not depend solely on the machine locale
   - Create `ExcelTemplateGenerator` in `FactGrid` so templates use the same metadata and conversion expectations as validation

4. **Centralize entity discovery and DI registration**
   - Create `FactGridEntityCatalog` as the only entity registration list
   - Create `AddFactGridEntities()` in the shared library to register the catalog, registry, parsers, and shared Excel services
   - Call the same extension from `FactGrid.AspNet` and `FactGrid.Mcp`
   - Do not duplicate `RegisterWithParser` or parser DI registrations in either host

5. **Update `FactGrid.AspNet`**
   - Add `<ProjectReference Include="..\FactGrid\FactGrid.csproj" />`
   - Remove moved files
   - Update `using` statements (entities, parsers, registry now in `FactGrid` namespace)
   - Keep: `EntityContextAccessor`, `EntityServiceFactory`, `EntityTableService`, `QueryValidationService`, `GenericSqlQueryTool`, `EntityPrompts`, `EntityResources`
   - Replace host-local entity/parser registrations with `builder.Services.AddFactGridEntities()`
   - Add `POST /api/ingestion/{entityName}/upload`
   - Return structured JSON with `success`, `insertedCount`, and `errors`
   - Reject the complete upload and insert zero rows when any validation error exists
   - Keep the MVC upload endpoint separate for human-facing web UI behavior
   - Do not add authentication in Phase 3; preserve the dedicated API boundary for a later security phase

6. **Create `FactGrid.Mcp` console project**
   - `dotnet new console -n FactGrid.Mcp -o src/FactGrid.Mcp`
   - Add `ModelContextProtocol` and `Microsoft.Extensions.Hosting` package references
   - Add `<ProjectReference Include="..\FactGrid\FactGrid.csproj" />`

7. **Implement `DataEntryTools`** in `FactGrid.Mcp`
   - `generate_template(entityName, outputPath)` — invokes the shared template generator → saves `.xlsx` locally → returns generated path and summary
   - `validate_excel(entityName, filePath)` — reads file → invokes `IExcelParser<T>.Parse()` → returns markdown preview + error list
   - `upload_excel(entityName, filePath)` — reads file bytes → `HttpClient.PostAsync()` to `FACTGRID_SERVER_URL/api/ingestion/{entityName}/upload` (multipart form) → reads structured JSON result
   - `list_entities()` — reads `EntityRegistry.GetAll()` → formats as markdown table with column metadata

8. **Wire up `Program.cs` for `FactGrid.Mcp`**
   ```csharp
   var builder = Host.CreateApplicationBuilder(args);
   builder.Services.AddFactGridEntities();
   builder.Services.AddHttpClient(); // for upload_excel
   builder.Services.AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly(typeof(DataEntryTools).Assembly);
   await builder.Build().RunAsync();
   ```

9. **Update and expand tests**
   - Add project reference to `FactGrid` where needed
   - Update namespaces in test files that reference entity models or parsers
   - Verify generated templates successfully round-trip through their registered parser
   - Verify parsers and templates honor changes to `[ExcelColumn.Position]`
   - Verify typed Excel dates and documented text fallback formats
   - Verify both hosts receive the same registry entries from `AddFactGridEntities()`
   - Verify invalid ingestion responses are structured and insert zero rows
   - Verify valid ingestion inserts all parsed rows and reports the inserted count
   - Verify unknown entities and invalid/non-`.xlsx` files return structured errors

10. **Update solution** (`FactGrid.slnx`)
   - Add `src/FactGrid/FactGrid.csproj`
   - Add `src/FactGrid.Mcp/FactGrid.Mcp.csproj`

## Phase 3 Acceptance Criteria

- `FactGrid.AspNet` and `FactGrid.Mcp` build using the same shared entity catalog and parser registrations.
- No host contains a duplicate list of supported entities or parser DI registrations.
- No entity parser duplicates Excel column positions outside `[ExcelColumn]` metadata.
- A template generated for every registered entity can be parsed successfully without manual structural edits.
- `validate_excel` returns a record preview and validation errors without modifying the database.
- `upload_excel` calls only the dedicated ingestion API and correctly reads its structured response.
- The ingestion API inserts all valid rows from a valid file.
- The ingestion API inserts zero rows when any row fails validation.
- Typed Excel date cells and documented text date formats are both accepted.
- Existing HTTP reporting MCP tools, resources, prompts, and MVC upload behavior continue to pass their tests.
- Authentication for the ingestion API is documented as deferred and is not mixed into the Phase 3 implementation.

## Key Design Decisions

1. **The shared `FactGrid` library owns the entity contract** — Models, `[ExcelColumn]` metadata, metadata-driven parsing, entity-specific validation, template generation, and `FactGridEntityCatalog` live together. Both hosts call the same `AddFactGridEntities()` extension. Adding or changing an entity must not require duplicate host registrations or duplicate column-position definitions.

2. **STDIO MCP has no database** — It reads the filesystem and delegates persistence to the HTTP server via file upload.

3. **Template generation writes locally** — `generate_template` uses the shared ClosedXML-based generator to save a workbook to the requested local path. It returns the path and summary rather than workbook bytes or base64.

4. **Server-side validation is authoritative and ingestion is atomic** — `FactGrid.Mcp` validates for fast feedback, but the ingestion API re-parses the complete upload using the same shared parser. If any error exists, the server inserts zero rows.

5. **Machine ingestion has a dedicated endpoint** — `POST /api/ingestion/{entityName}/upload` is separate from the MVC upload action and returns structured JSON. Authentication is deferred from Phase 3, but future security belongs at this API boundary.

6. **Excel dates do not rely solely on local machine locale** — The STDIO server and generated files will often share a machine and locale, but workbooks can still contain typed dates or text from other sources. Parsers accept typed Excel dates first and only then documented text fallback formats.

7. **HTTP MCP reporting tools remain unchanged** — No new reporting tools beyond what Phase 2 delivered. Existing tools, resources, and prompts keep working.

8. **Environment variable for server URL** — `FACTGRID_SERVER_URL` configures where `FactGrid.Mcp` uploads files. This maps cleanly to `.mcp.json` `"env"` blocks. Example:
   ```json
   {
     "servers": {
       "factgrid-mcp": {
         "type": "stdio",
         "command": "dotnet",
         "args": ["run", "--project", "src/FactGrid.Mcp/FactGrid.Mcp.csproj"],
         "env": {
           "FACTGRID_SERVER_URL": "http://localhost:5000"
         }
       }
     }
   }
   ```

## Reference: CodeMemory STDIO MCP Patterns

The following patterns are derived from `E:\khurram-uworx\CodeMemory` (the reference implementation). They are captured here so implementing agents do not need to consult that codebase.

### Pattern 1: Program.cs Setup (STDIO MCP)

From `CodeMemory.Mcp/Program.cs`:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Register all dependencies
builder.Services.AddSingleton<MyService>();
// ...

// MCP server with stdio transport
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(MyTool).Assembly);

var app = builder.Build();
await app.RunAsync();
```

Key details:
- Use `Host.CreateApplicationBuilder` (not `WebApplication.CreateBuilder`)
- No `MapMcp()` call needed — stdio transport replaces HTTP routing
- `.WithToolsFromAssembly()` scans for `[McpServerToolType]` classes

### Pattern 2: Project File (STDIO MCP)

From `CodeMemory.Mcp/CodeMemory.Mcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="1.3.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.8" />
    <!-- plus domain-specific packages -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\FactGrid\FactGrid.csproj" />
  </ItemGroup>
</Project>
```

Note: Use `Microsoft.NET.Sdk` (not `Microsoft.NET.Sdk.Web`) since it is a console app, not a web app.

### Pattern 3: Simple Tool (no dependencies)

From `CodeMemory/Mcp/McpTools.cs`:

```csharp
[McpServerToolType]
public sealed class McpTools
{
    [McpServerTool, Description("Ping the server. Returns indexing status.")]
    public PingResult Ping()
    {
        return new PingResult("ok", true, ...);
    }
}
```

### Pattern 4: Tool with Constructor Injection

From `CodeMemory.Mcp/Tools/InitTool.cs`:

```csharp
[McpServerToolType]
public sealed class InitTool
{
    readonly CodeMemoryInitService initService;

    public InitTool(CodeMemoryInitService initService)
    {
        this.initService = initService;
    }

    [McpServerTool, Description("Creates configuration file.")]
    public InitResult InitRepository(
        [Description("Repository root path")] string? repoRoot = null,
        CancellationToken ct = default)
    {
        return initService.Run(resolvedRoot);
    }
}
```

Key details:
- Register the service (`CodeMemoryInitService`) in DI before `AddMcpServer()`
- Tool methods can accept `CancellationToken` — the SDK injects it
- Optional parameters with `[Description]` let the LLM decide whether to pass them

### Pattern 5: Tool Returning JSON Dictionary

From `CodeMemory.Mcp/Tools/SqlQueryTool.cs`:

```csharp
[McpServerToolType]
public sealed class SqlQueryTool
{
    public SqlQueryTool(
        IStorageService storageService,
        SqlQueryService sqlQueryService,
        ILogger<SqlQueryTool> logger) { ... }

    [McpServerTool, Description("Long description with syntax docs...")]
    public async Task<IDictionary<string, object?>> SqlQueryAsync(
        [Description("SQL query string")] string query,
        [Description("Maximum results")] int maxResults = 100)
    {
        try
        {
            var result = await sqlQueryService.ExecuteAsync(...);
            return new Dictionary<string, object?>
            {
                ["success"] = result.Success,
                ["rowCount"] = result.RowCount,
                ["columns"] = result.Columns,
                ["rows"] = result.Rows,
                ["error"] = result.Error
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object?>
            {
                ["success"] = false,
                ["error"] = $"Query failed: {ex.Message}"
            };
        }
    }
}
```

Key details:
- Return `Task<IDictionary<string, object?>>` for structured JSON responses
- The LLM client receives a JSON object it can programmatically read
- Return `Task<string>` for markdown/text-only responses

### Pattern 6: Dual-Assembly Tool Registration

From `CodeMemory.Mcp/Program.cs`:

```csharp
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(CodeMemory.Mcp.McpTools).Assembly)    // shared tools
    .WithToolsFromAssembly(typeof(CodeMemory.Mcp.Tools.SqlQueryTool).Assembly); // host-specific
```

This pattern allows sharing tools across hosts. For FactGrid:
- Shared tools: none needed initially (entity-aware tools are in each host)
- But the pattern works if shared tools are added later
- Each host registers only its own tools from its own assembly

## Naming Convention Reference

From `AGENTS.md` (existing) — tool names are auto-derived:
- `SqlQueryAsync` → `sql_query`
- `DescribeAsync` → `describe`
- `GenerateTemplateAsync` → `generate_template`
- `ValidateExcelAsync` → `validate_excel`
- `UploadExcelAsync` → `upload_excel`
- `ListEntitiesAsync` → `list_entities`
