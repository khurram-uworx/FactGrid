# EfMcp

Entity Framework + Streamable HTTP MCP

## Overview

EfMcp is an ASP.NET Core MVC application that bridges Entity Framework and [Streamable HTTP MCP](https://github.com/streamable/streamable-http-mcp). It allows you to upload Excel files, store each row in a corresponding EF table, and query that data via an MCP endpoint.

**Key features:**
- Upload Excel files via web UI; each row maps 1:1 to an Entity Framework entity
- View row count and delete all data for an entity before re-uploading
- MCP endpoint at `/api/mcp/{Entity}` exposing a `sql_query` tool (SELECT-only via SqlParserCS)
- Entity descriptions sourced from `[Description]` attributes on EF entity classes
- Supports SQL Server (default), SQLite, and PostgreSQL

## Status

Proof of concept. The first entity/Excel format is TBD and will be documented separately.

## Getting Started

### Prerequisites
- .NET 10 SDK
- SQL Server (LocalDB, Docker, or instance)

### Quick Start

```bash
git clone https://github.com/khurram-uworx/EfMcp
cd EfMcp
dotnet run --project src/EfMcp.AspNet
```

Set the connection string in `appsettings.json` or via `-ConnectionStrings:DefaultConnection`.

### Docker

```bash
docker build -f src/EfMcp.AspNet/Dockerfile -t efmcp .
docker run -p 8080:8080 efmcp
```

## Usage

1. **Navigate to the web app** — upload an Excel file matching the expected entity schema
2. **Review** — the UI shows how many rows currently exist for the entity
3. **Upload** — each row is inserted into the database
4. **Reset** — delete all rows if a partial/failed upload needs a fresh start
5. **Query via MCP** — send SQL queries (SELECT only) to `/api/mcp/{Entity}`

Example MCP request:

```
POST /api/mcp/Products
Content-Type: application/json

{
  "tool": "sql_query",
  "args": { "query": "SELECT * FROM Products WHERE Price > 100" }
}
```

## Architecture

```
Excel File → Web UI → EF Core → SQL Server
                                 ↓
                     MCP Endpoint (/api/mcp/{Entity})
                                 ↓
                     sql_query tool (SELECT-only via SqlParserCS)
```

- Entities are decorated with `[Description]` attributes; these are used to describe the schema in MCP responses
- Query parsing uses [SqlParserCS](https://github.com/TeddyDD/SqlParserCS) to reject non-SELECT statements
- **Security:** The `sql_query` tool accepts raw SELECT queries. SqlParserCS gates non-SELECT statements, but queries are not parameterized. In production, connect with a read-only database user to limit risk.
- No authentication/authorization on MCP endpoints (future consideration)
- Each entity gets its own `/api/mcp/{Entity}` route

## Adding an Entity

1. Define the model class with `[Description]` on properties
2. Add a `DbSet<T>` to `ApplicationDbContext`
3. Create a migration: `dotnet ef migrations add Add{Entity}`
4. The MCP endpoint and upload UI are auto-wired by convention

## Project Structure

```
EfMcp/
├── src/
│   └── EfMcp.AspNet/        # MVC web app
│       ├── Controllers/       # Home + MCP controllers
│       ├── Data/              # DbContext, migrations
│       ├── Models/            # EF entities + view models
│       ├── Views/             # Razor views
│       └── Program.cs         # App startup
├── tests/
│   └── EfMcp.Tests/          # Unit tests
├── docs/                     # ADRs and task templates
├── EfMcp.slnx                # Solution file
└── AGENTS.md                 # AI agent guidance
```

## License

MIT
