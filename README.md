# FactGrid

Entity Framework + Streamable HTTP MCP

## Overview

FactGrid is an ASP.NET Core MVC application that bridges Entity Framework and Streamable HTTP MCP. It allows you to upload Excel files, store each row in a corresponding EF table, and query that data via an MCP endpoint.

**Key features:**
- Upload Excel files via web UI; each row maps 1:1 to an Entity Framework entity
- View row count and delete all data for an entity before re-uploading
- MCP endpoint at `/api/mcp/{entityName}` exposing `sql_query` (SELECT-only via SqlParserCS) and `describe` tools
- Global MCP endpoint at `/api/mcp` (no entity scope) and scoped endpoints at `/api/mcp/{entityName}`
- MCP resources (`entities://list`, `entities://schema`) and prompts (`entities-guide`, `entity-guide`)
- Entity descriptions sourced from `[Description]` attributes on EF entity classes
- Supports SQL Server (default), SQLite, and PostgreSQL

## Status

Proof of concept. Currently supports **Worklogs** (`ResourceHours` table) and **Expenses** (`Expenses` table) entities. New entities can be added by defining a model, an Excel parser, and registering in the `EntityRegistry`.

## Getting Started

### Prerequisites
- .NET 10 SDK
- SQL Server (LocalDB, Docker, or instance)

### Quick Start

```bash
git clone https://github.com/khurram-uworx/FactGrid
cd FactGrid
dotnet run --project src/FactGrid.AspNet
```

Set the connection string in `appsettings.json` or via `-ConnectionStrings:DefaultConnection`.

### Docker

```bash
docker build -f src/FactGrid.AspNet/Dockerfile -t factgrid .
docker run -p 8080:8080 factgrid
```

## Usage

1. **Browse entities** — navigate to `/Entity` to see all registered entities
2. **Review** — select an entity to see how many rows currently exist
3. **Upload** — upload an `.xlsx` file matching the entity's expected schema; each row is inserted into the database
4. **Reset** — delete all rows if a partial/failed upload needs a fresh start
5. **Query via MCP** — send SQL queries (SELECT only) or schema requests to `/api/mcp/{entityName}`

All MCP requests use JSON-RPC 2.0 over the Streamable HTTP transport. The `Accept` header must include both `application/json` and `text/event-stream`.

Example: calling `sql_query` against the Expenses entity:

```
POST /api/mcp/expenses
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "sql_query",
    "arguments": {
      "query": "SELECT * FROM Expenses WHERE Amount > 100"
    }
  }
}
```

Example: calling `describe` (get entity schema):

```
POST /api/mcp/expenses
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "describe"
  }
}
```

### Resources & Prompts

Read the entity list resource via `resources/read`:

```
POST /api/mcp
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "resources/read",
  "params": {
    "uri": "entities://list"
  }
}
```

Get the entity schema resource:

```
POST /api/mcp/worklogs
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "resources/read",
  "params": {
    "uri": "entities://schema"
  }
}
```

Retrieve a prompt via `prompts/get`:

```
POST /api/mcp
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "prompts/get",
  "params": {
    "name": "entities-guide"
  }
}
```

Scoped prompt (requires entity context):

```
POST /api/mcp/worklogs
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "prompts/get",
  "params": {
    "name": "entity-guide"
  }
}
```

## Architecture

```
Excel File → Web UI → EntityController (per-entity routing via EntityRegistry)
                      ↓
                  EF Core → SQL Server
                             ↓
            MCP Endpoints:
              /api/mcp (global) — any entity table
              /api/mcp/{entityName} (scoped) — one entity table
                             ↓
            Tools: sql_query (SELECT-only via SqlParserCS)
                   describe (entity schema from [Description] attributes)
            Resources: entities://list, entities://schema
            Prompts: entities-guide (global), entity-guide (scoped)
```

- Entities are registered in a singleton `EntityRegistry` that maps entity names to model types, table names, Excel parser types, and descriptions
- Each entity gets its own `/api/mcp/{entityName}` route; the registry routes requests to the correct DbSet and parser
- The MCP server uses the **Streamable HTTP** transport — all requests use JSON-RPC 2.0 with `Content-Type: application/json` and `Accept: application/json, text/event-stream`
- Resources (`entities://list`, `entities://schema`) and prompts (`entities-guide`, `entity-guide`) are context-aware — they adapt their output based on whether they are accessed from the global or scoped route
- Entity schemas are sourced from `[Description]` attributes on EF entity classes and exposed via the `describe` MCP tool and `entities://schema` resource
- A query validator (`QueryValidationService`) ensures SQL is SELECT-only (`Validate`), restricts scoped queries to the entity's own table (`ValidateScoped`), and restricts global queries to registered entity tables (`ValidateTables`)
- Query parsing uses [SqlParserCS](https://github.com/TeddyDD/SqlParserCS) to reject non-SELECT statements; table references are extracted via AST traversal and validated against an allow-list
- **Security:** The `sql_query` tool accepts raw SELECT queries. SqlParserCS gates non-SELECT statements, but queries are not parameterized. Global mode (`/api/mcp`) is limited to registered entity tables only. In production, connect with a read-only database user to limit risk.
- Both MCP routes (`/api/mcp` and `/api/mcp/{entityName}`) are anonymous during this proof-of-concept phase. Global scope ("power user") describes entity scope, not an authenticated role — it means all registered entities are accessible.
- Web UI provides entity-specific upload (`.xlsx`), row count display, and delete-all via `Entity/{entityName}` routes

## Adding an Entity

1. **Define the model class** with `[Table("PhysicalTableName")]` and `[Description]` on properties
2. **Add a `DbSet<T>`** to `ApplicationDbContext`
3. **Create an Excel parser** — implement `IExcelParser<T>` to parse `.xlsx` rows into model instances (first sheet only, header row 1 skipped, fixed column positions)
4. **Register in the EntityRegistry** — call `registry.RegisterWithParser<TModel, TParser>(entityName, displayName, tableName, description)` in `Program.cs`
5. **Register DI services** — add `IExcelParser<T>` and `IEntityTableService<T>` scoped services in `Program.cs`
6. **Create a migration**: `dotnet ef migrations add Add{Entity}`
7. **Apply the migration**: `dotnet ef database update --project src/FactGrid.AspNet`

The MCP endpoints (global `/api/mcp` and scoped `/api/mcp/{entityName}`), resources (`entities://list`, `entities://schema`), prompts (`entities-guide`, `entity-guide`), web UI (`/Entity/{entityName}`), and entity discovery are auto-wired by the registry.

## License

MIT
