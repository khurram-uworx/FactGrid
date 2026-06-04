# Phase 3 — Two-MCP Architecture: Local + Remote

## Goal

Enable two separate MCP hosts sharing the same tool classes from `FactGrid.Mcp` — a **local/stdio MCP** for coding agents (opencode, Copilot, etc.) to read Excel files and push data, and a **web/remote MCP** for web AI (ChatGPT, etc.) to query reports and uploaded data.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                   Coding Agent                              │
│          (opencode / Copilot / codex)                       │
│                                                             │
│  "Read this Excel and push to the server"                   │
│  "Upload my worklogs file"                                  │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│              Local/stdio MCP (FactGrid.Mcp.Host.Console)       │
│                                                              │
│  Tools from FactGrid.Mcp:                                       │
│    - read_excel(filePath) → parse + preview                  │
│    - push_data(entityName, rows) → HTTP POST to org server   │
│    - list_entities() → available entity types                │
│                                                              │
│  No local database — reads filesystem, pushes via HTTP       │
└──────────────────────────┬───────────────────────────────────┘
                           │  HTTPS
                           ▼
┌──────────────────────────────────────────────────────────────┐
│              Organization Server (FactGrid.AspNet)              │
│                                                              │
│  ┌──────────────────┐   ┌──────────────────────────────┐    │
│  │  Web UI           │   │  Web/remote MCP             │    │
│  │  /Worklogs        │   │  /api/mcp/{entityName}      │    │
│  │  upload + manage  │   │                              │    │
│  └──────────────────┘   │  Tools from FactGrid.Mcp:       │    │
│                          │    - sql_query(SELECT)       │    │
│  ┌──────────────────┐   │    - describe()              │    │
│  │  Database         │   │    - list_entities()         │    │
│  │  (SQL Server /    │   │    - get_report(entity, id) │    │
│  │   SQLite / PG)    │   └──────────────────────────────┘    │
│  └──────────────────┘                                       │
└──────────────────────────────────────────────────────────────┘
                           ▲
                           │
┌──────────────────────────────────────────────────────────────┐
│                   Web AI / Chat Agent                        │
│                                                              │
│  "Show me the worklog report for last month"                 │
│  "Who has the most hours in Q4?"                             │
└──────────────────────────────────────────────────────────────┘
```

## Components

### 1. `FactGrid.Mcp` — Shared Tool Library (already exists after Phase 1)

Contains:
- `QueryValidationService` — SqlParserCS SELECT-only gate
- Tool classes with `[McpServerToolType]` / `[McpServerTool]` attributes
- Interfaces for data access (abstracted so both hosts can inject their own)

**Key design principle:** Tools depend only on abstractions, not on ASP.NET or console intrinsics. Both hosts inject their own service implementations.

### 2. `FactGrid.Mcp.Host.Console` — Local/stdio MCP [NEW]

A .NET console app that:
- Uses `ModelContextProtocol.Protocol.Transport.StdioServerTransport` for stdio transport
- Loads the same tool assembly from `FactGrid.Mcp`
- Injects a **file-system-backed** data service:
  - `read_excel(filePath)` — reads local `.xlsx`, parses with the registered `IExcelParser<T>`, returns preview as markdown
  - `push_data(entityName)` — sends parsed data to the organization server via HTTP
  - No database — the console host is a thin bridge between local files and the remote server

**CodeMemory ref:**
- `CodeMemory.Mcp/Program.cs` — stdio MCP server setup with `WithToolsFromAssembly()`
- `CodeMemory.Mcp/Tools/McpTools.cs` — Ping tool pattern (simple tools that don't need DB)

### 3. `FactGrid.AspNet` — Organization Server (exists)

Already hosts:
- Web UI for upload + data management
- Web/remote MCP at `/api/mcp/{entityName}`
- Database (SQL Server / SQLite / PostgreSQL)

New additions:
- `POST /api/data/{entityName}` — HTTP endpoint for the local MCP to push data (internal, or with API key)
- Report tools: `get_report(entity, filters)` — pre-built report queries formatted as markdown tables

### 4. Workflow

```
Coding Agent:
  1. "Read my worklogs.xlsx and push it"
  2. Local MCP reads file, parses with ClosedXML, returns preview
  3. Agent confirms → Local MCP POSTs data to org server
  4. Org server stores in DB

Web AI / Chat:
  1. "Show me the worklog report for December"
  2. Remote MCP runs SELECT query, returns markdown table
  3. Agent renders the table to the user
```

## Tool Inventory

### Local MCP Tools

| Tool | Description |
|------|-------------|
| `read_excel(filePath)` | Reads and parses an Excel file, returns preview (markdown table + row count) |
| `push_data(entityName, data)` | Pushes parsed rows to the organization server |
| `list_entities()` | Returns available entity types and their expected Excel formats |
| `ping()` | Health check |

### Remote MCP Tools (additions to Phase 2)

| Tool | Description |
|------|-------------|
| `sql_query(query)` | SELECT-only SQL execution (from Phase 1) |
| `describe()` | Entity schema with `[Description]` attribtues (from Phase 1) |
| `list_entities()` | Returns all registered entities |
| `get_report(entity, filters)` | Pre-built parameterized reports |

## Migration Path from Phase 2

1. Create `FactGrid.Mcp.Host.Console` project with stdio transport
2. Extract EF Core data access behind an interface in `FactGrid.Mcp`:
   - Both ASP.NET host and console host implement the interface
3. Add `read_excel` and `push_data` tools to `FactGrid.Mcp`
4. Add data ingestion endpoint to the ASP.NET server
5. Add report tools

## Key Decisions for Phase 1/2

To keep the Phase 3 door open:

1. **Keep tools dependency-free** — no direct references to ASP.NET, `HttpContext`, or `IWebHostEnvironment` in tool classes
2. **Use constructor injection** for all dependencies — both hosts can wire up their own DI
3. **Decouple query execution** — put ADO.NET/EF Core execution behind an interface so the console host can use HTTP instead
4. **Register tools by assembly** — `WithToolsFromAssembly(typeof(AnyToolInMcpLib).Assembly)` works identically for both hosts

**CodeMemory ref — dual-host pattern:**
- `CodeMemory.AspNet/Program.cs` — ASP.NET host, Streamable HTTP transport
- `CodeMemory.Mcp/Program.cs` — stdio host, `StdioServerTransport`
- Both reference `CodeMemory.Mcp` for shared tool classes
