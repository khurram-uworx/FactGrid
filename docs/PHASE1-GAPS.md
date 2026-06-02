# Phase 1 — Gaps & Issues

This document compares the [PHASE1.md](PHASE1.md) plan against what was actually implemented, and notes issues found during verification.

## Plan vs. Implementation

### 1. Project Structure

| Planned | Actual | Status |
|---------|--------|--------|
| `src/EfMcp.Mcp/` — shared class library | NOT CREATED — all code in `EfMcp.AspNet` | **Deviation** (per user request: "keep everything in EfMcp.AspNet for now") |
| `src/EfMcp.ServiceDefaults/` — optional | NOT CREATED | **Skipped** (marked optional in plan) |
| `WorklogsMcpTools.cs` in `EfMcp.Mcp/Tools/` | Created in `EfMcp.AspNet/Tools/` | **Deviation** |
| `QueryValidationService.cs` in `EfMcp.Mcp/Services/` | Created in `EfMcp.AspNet/Services/` | **Deviation** |
| `EfMcp.slnx` modified to add new projects | NOT MODIFIED (no new projects to add) | **Correct** |

**Implication:** Phase 2/3 extraction effort is needed to pull shared tool code into a class library for the dual-MCP architecture. Documented in PHASE2.md and PHASE3.md.

### 2. Task 4 — MCP Tool Namespace

| Planned | Actual |
|---------|--------|
| `using ModelContextProtocol.Attributes;` | `using ModelContextProtocol.Server;` |

The `[McpServerToolType]` and `[McpServerTool]` attributes live in `ModelContextProtocol.Server`, not `ModelContextProtocol.Attributes`. The plan's code snippets had incorrect imports.

### 3. Task 5 — Multi-Provider DB

| Planned | Actual |
|---------|--------|
| Add example connection strings for SQLite and PostgreSQL to `appsettings.json` | Only `Storage:Provider` key added, no example connection strings |

**Gap:** `appsettings.json` only shows the SQL Server connection string. A developer switching to SQLite/PostgreSQL needs to know the format. Example configs should be added.

### 4. Task 6 — Unit Tests

| Planned | Actual |
|---------|--------|
| `ExcelUploadServiceTests.cs` | ✅ Created (8 tests) |
| `QueryValidationTests.cs` | ✅ Created (14 tests) |
| `WorklogsMcpToolsTests.cs` — in-memory SQLite, test both tools | ❌ **NOT CREATED** |

**Gap:** No integration tests for the MCP tools (`describe`, `sql_query`). These require a database and are harder to unit test, but should exist.

### 5. Plan Documentation References

The plan references these non-existent paths (leftover from the original `EfMcp.Mcp` architecture):
- `src/EfMcp.Mcp/Tools/WorklogsMcpTools.cs`
- `src/EfMcp.Mcp/Services/QueryValidationService.cs`
- `EfMcp.Mcp.csproj`

These should be updated if the plan is ever re-used as a task checklist.

---

## Implementation Issues

### 1. Date Parsing is Culture-Sensitive

`ExcelUploadService.cs:49` uses `DateOnly.TryParse(workDateStr, out var workDate)`.

The Excel data format is `"25-Dec-2024"` (dd-MMM-yyyy). `DateOnly.TryParse` uses the current culture:
- **en-GB / en-AU / en-IN** — parses correctly (dd-MMM-yyyy is standard)
- **en-US** — **may fail** because en-US expects MMM-dd-yyyy

**Impact:** If the server runs under en-US culture, `DateOnly.TryParse("25-Dec-2024")` will return false and the row will be skipped as an error.

**Fix:** Use `DateOnly.TryParseExact(workDateStr, "d-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate)` to be culture-independent.

### 2. SQL Column Type Mismatch

| Property | Plan says | Actual migration generates |
|----------|-----------|--------------------------|
| `WorkDate` | `DATE` | `datetime2` (because C# type is `DateTime`) |

`DateOnly` maps to `DATE` in SQL Server, but `DateTime` maps to `datetime2`. The entity uses `DateTime` but the plan documents it as `DATE`. Not a functional issue but a documentation mismatch.

### 3. ExcelUploadService — No File Size Limit

The `WorklogsController.Upload` action has no file size validation. While ASP.NET Core has default request size limits, a very large Excel file could cause memory pressure since `ClosedXML` loads the entire workbook into memory.

### 4. WorklogsMcpTools.SqlQueryAsync — No SQL Injection Protection

The tool accepts arbitrary SELECT queries and passes them directly via ADO.NET. While SqlParserCS ensures the query is a single SELECT, it does not sanitize or parameterize it. This is by design (the tool's purpose is to run raw queries), but worth noting.

---

## Minor Issues

### 1. Missing `_ViewStart.cshtml` for Worklogs Views

`Views/Worklogs/Index.cshtml` uses the Identity layout implicitly from the `_ViewStart.cshtml` in the parent `Views/` folder. If a `_ViewStart.cshtml` were placed in `Views/Worklogs/`, it would override the parent. Currently none exists, so it inherits correctly — but this could be surprising if someone adds one later.

### 2. appsettings.Development.json Not Updated

The `appsettings.Development.json` still only has logging config. If a developer wants development-specific provider settings, they'd need to add them here.

---

## Summary

| Category | Count |
|----------|-------|
| Plan deviations | 3 (project structure, namespace, missing test) |
| Implementation issues | 2 (date parsing, date type mismatch) |
| Missing features | 2 (example connection strings, MCP tool integration tests) |
| Minor items | 2 |
