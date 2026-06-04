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

## Verify the App

### Browse Web UI

Open `https://localhost:5001/Entity` to see registered entities.

### MCP Endpoints

FactGrid exposes two MCP routes:

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

## Uploading Excel Files

1. Navigate to `https://localhost:5001/Entity`
2. Select an entity (e.g., Worklogs)
3. Click **Choose File** and select an `.xlsx` file
4. Click **Upload**

Expected columns: header row (row 1) is skipped, data starts at row 2. Column positions are fixed per parser.

## Running Tests

```bash
dotnet test tests/FactGrid.Tests
```

Uses in-memory SQLite — no database setup needed.

## Adding a New Entity

See [Adding an Entity](../README.md#adding-an-entity) in the README for the step-by-step process.

## Project Layout

See [Project Structure](../AGENTS.md#project-structure) in AGENTS.md for the directory layout.
