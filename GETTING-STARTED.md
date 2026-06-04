# Getting Started with FactGrid

## Prerequisites

- .NET 10 SDK
- SQL Server (LocalDB, Docker, or remote instance) — or use SQLite for development

## Quick Start

```bash
git clone https://github.com/khurram-uworx/FactGrid
cd FactGrid
dotnet run --project src/FactGrid.AspNet
```

The app starts on `https://localhost:5001` (or the port shown in the console).

## Configuration

### Connection Strings

Edit `src/FactGrid.AspNet/appsettings.json`:

```json
{
  "Storage:Provider": "sqlserver",
  "ConnectionStrings": {
    "SqlServer": "Server=(localdb)\\mssqllocaldb;Database=FactGrid;Trusted_Connection=True;MultipleActiveResultSets=true",
    "Sqlite": "Data Source=App_Data/factgrid.db",
    "PostgreSql": "Host=localhost;Database=factgrid;Username=postgres;Password=..."
  }
}
```

### Provider Options

| `Storage:Provider` | Database | Notes |
|---|---|---|
| `sqlserver` (default) | SQL Server | Requires running SQL Server instance |
| `sqlite` | SQLite | File stored in `App_Data/factgrid.db`, auto-created |
| `postgresql` | PostgreSQL | Requires running PostgreSQL instance |

## Database Setup

On first run, Entity Framework applies pending migrations automatically (`db.Database.MigrateAsync()`).

For SQL Server, create the database first or let EF create it:

```bash
dotnet ef database update --project src/FactGrid.AspNet
```

## Run the Local STDIO MCP (FactGrid.Mcp)

The local STDIO MCP (`FactGrid.Mcp`) is a console app that communicates with your coding agent over standard input/output. It does not start a web server.

Configure it in your MCP client's `opencode.json` or equivalent:

```jsonc
{
  "servers": {
    "factgrid-mcp": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "src/FactGrid.Mcp/FactGrid.Mcp.csproj"
      ],
      "env": {
        "FACTGRID_SERVER_URL": "http://localhost:5000"
      }
    }
  }
}
```

The `FACTGRID_SERVER_URL` environment variable is **required** for the `upload_excel` tool. Set it to the base URL of your running FactGrid organization server.

## Local Data Entry Tools (FactGrid.Mcp STDIO)

These tools are available when your coding agent connects to the local STDIO MCP:

| Tool | Description |
|---|---|
| `generate_template(entityName, outputPath)` | Generates a typed `.xlsx` template with headers and an example row. `outputPath` must end with `.xlsx`. |
| `validate_excel(entityName, filePath)` | Parses the workbook and returns a preview (up to 20 records) plus validation errors. Does not modify any data. |
| `upload_excel(entityName, filePath)` | Validates the workbook locally, then uploads it to `FACTGRID_SERVER_URL/api/ingestion/{entityName}/upload`. Returns the server's structured result. |
| `list_entities()` | Lists all registered entities with their descriptions and Excel column metadata. |

### Generated Templates

Templates have:

- **Column headers** derived from `[ExcelColumn]` metadata, ordered by position number
- **Example data row** (row 2) with typed values from `[ExcelColumn(Example = ...)]`
- **Number formatting**: `yyyy-mm-dd` for date columns, `0.00` for decimal/numeric columns
- **Validation**: required fields are marked in the column metadata; parsers enforce them

### Accepted Date Formats

When parsing workbook cells, both `WorklogsExcelParser` and `ExpensesExcelParser` accept:

- **Typed DateTime cells** — cells with `XLDataType.DateTime` set by Excel date pickers
- **Text dates** in two formats:
  - `M/d/yyyy h:mm:ss tt` (e.g., `12/25/2024 12:00:00 AM`)
  - `yyyy-MM-dd` (e.g., `2024-12-25`)

## Ingestion API (FactGrid.AspNet)

`POST /api/ingestion/{entityName}/upload` accepts a multipart `.xlsx` file.

### Response Status Codes

| Status | Meaning |
|---|---|
| `200 OK` | All records inserted. Response includes `success: true` and `insertedCount`. |
| `400 Bad Request` | No file provided, wrong extension, or malformed workbook. |
| `422 Unprocessable Entity` | Workbook parsed but contained validation errors (missing required fields, bad dates, bad numbers). No records inserted. |
| `404 Not Found` | Unknown entity name. |
| `500 Internal Server Error` | Unexpected server failure. No records inserted. |

### Response Shape

All responses share the same JSON contract (`IngestionResult`):

```json
{
  "success": false,
  "insertedCount": 0,
  "errors": ["Could not read workbook: ..."]
}
```

## Verify the App

### Browse Web UI

Open `https://localhost:5001/Entity` to see registered entities.

### Central HTTP MCP Endpoints

FactGrid exposes two MCP routes for reporting:

| Route | Scope | Description |
|---|---|---|
| `/api/mcp` | Global | Can query any registered entity's table |
| `/api/mcp/{entityName}` | Scoped | Restricted to one entity's table |

FactGrid uses the **Streamable HTTP** transport for MCP. All requests use JSON-RPC 2.0 format, and the `Accept` header **must** include both `application/json` and `text/event-stream`.

Test the global endpoint — call `describe`:

```bash
curl -X POST https://localhost:5001/api/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": { "name": "describe" }
  }'
```

Test a scoped endpoint — call `sql_query`:

```bash
curl -X POST https://localhost:5001/api/mcp/worklogs \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "sql_query",
      "arguments": { "query": "SELECT * FROM ResourceHours" }
    }
  }'
```

### MCP Resources

| URI | Description |
|---|---|
| `entities://list` | List of all registered entities (all in global mode, single in scoped mode) |
| `entities://schema` | Column schemas (all entities in global mode, scoped entity in scoped mode) |

Read a resource via `resources/read`:

```bash
curl -X POST https://localhost:5001/api/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "resources/read",
    "params": { "uri": "entities://list" }
  }'
```

### MCP Prompts

| Name | Description |
|---|---|
| `entities-guide` | Overview of entities and how to query them (works in both global and scoped modes) |
| `entity-guide` | Detailed guide for the current scoped entity (requires entity context) |

Retrieve a prompt via `prompts/get`:

```bash
curl -X POST https://localhost:5001/api/mcp/worklogs \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "prompts/get",
    "params": { "name": "entity-guide" }
  }'
```

## Uploading Excel Files (Web UI)

1. Navigate to `https://localhost:5001/Entity`
2. Select an entity (e.g., Worklogs)
3. Click **Choose File** and select an `.xlsx` file
4. Click **Upload**

Expected columns: header row (row 1) is skipped, data starts at row 2. Column positions are determined by `[ExcelColumn]` metadata.

## Contributing

See [AGENTS.md](AGENTS.md) for repository structure, implementation constraints, and contributor verification.
