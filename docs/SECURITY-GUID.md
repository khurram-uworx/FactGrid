# GUID Security — API Key Authentication via ASP.NET Identity

## Purpose

Add GUID-based API key authentication to FactGrid's MCP and ingestion endpoints, backed by ASP.NET Identity. Protects entity data behind authenticated access while keeping the developer experience simple.

## Key Decisions

| Point | Decision |
|-------|----------|
| **Password for seeded admin** | Default Identity policy (len >= 6, digit, upper, lower, non-alnum). Use `Admin1!` as the dev seed password. Policy can be loosened later. |
| **Cache invalidation** | On regenerate: remove old key from `IMemoryCache`, generate new key, save to DB, add to cache. All in the facade. |
| **Key in URL** | `FACTGRID_SERVER_URL` has the key embedded (e.g. `http://localhost:5000/api/mcp/{guid}`). DataEntryTools parses the GUID from the URL. `upload_excel` also accepts an optional `apiKey` parameter for clients that cannot set env vars. |
| **UserAccountService facade** | Single class orchestrating user registration, API key generation/regeneration, cache management. Both the Register page and Admin/CreateUser go through it. |
| **Admin/CreateUser** | Protected by standard `[Authorize]` — only logged-in users can create new users. |
| **GUID casing** | Always store and compare as `.ToLowerInvariant()`. Case-insensitive matching. |

## Suggested Execution Order

1. Task 1: ApplicationUser model + DbContext + migration (prerequisite)
2. Task 2: UserAccountService facade (dependency for everything else)
3. Task 3: ApiKeyAuthenticationService + MCP route changes (core auth integration)
4. Task 4: Program.cs — seed + service registration + demo mode
5. Task 5: IngestionController + EntityController auth
6. Task 6: AdminController + Register override + ApiKeys page (parallel with Task 5)
7. Task 7: DataEntryTools — KEY from URL + optional apiKey param
8. Task 8: Test updates (depends on all above)
9. Task 9: Documentation — GETTING-STARTED.md, AGENTS.md, opencode.json

## Coordination Notes

- Tasks 1–2 are decision gates and prerequisites for everything else.
- Tasks 3–4 both modify `Program.cs` — they should be done sequentially to avoid merge conflicts.
- Tasks 5 and 6 touch different controllers and can run in parallel.
- Task 8 (tests) cannot start until Task 7 is done.
- Task 9 (docs) can start as soon as the route shapes are finalized (after Task 3).

---

## Task 1: ApplicationUser Model + DbContext + Migration

### Priority

High

### Goal

Create a custom `ApplicationUser` that extends `IdentityUser` with an `ApiKey` property, update `ApplicationDbContext`, and generate the EF migration.

### Why this exists

The current code uses the non-generic `IdentityDbContext`. To add an `ApiKey` column to `AspNetUsers`, we need a custom user type and a typed `IdentityDbContext<ApplicationUser>`.

### Scope

- Create `src/FactGrid.AspNet/Models/ApplicationUser.cs`
  - Class `ApplicationUser : IdentityUser`
  - Property `string ApiKey { get; set; }` (initialized to empty string, normalized to lowercase)
- Update `src/FactGrid.AspNet/Data/ApplicationDbContext.cs`
  - Change from `IdentityDbContext` to `IdentityDbContext<ApplicationUser>`
  - In `OnModelCreating`, add: `builder.Entity<ApplicationUser>().HasIndex(u => u.ApiKey).IsUnique();`
- Update `src/FactGrid.AspNet/Program.cs`
  - Change Identity registration from `IdentityUser` to `ApplicationUser`
  - Widen password policy for dev seed:
    ```csharp
    services.Configure<IdentityOptions>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
    });
    ```
- Run `dotnet ef migrations add AddApplicationUser --project src/FactGrid.AspNet`
- Update test factories to use `ApplicationUser`

### Acceptance criteria

- `ApplicationUser` exists with `ApiKey` property
- `ApplicationDbContext` compiles with `IdentityDbContext<ApplicationUser>`
- Migration adds `ApiKey` column (string, nullable=false) with unique index
- `dotnet build FactGrid.slnx` succeeds
- `dotnet test tests/FactGrid.Tests` passes after test updates for `ApplicationUser`

### Files likely involved

- `src/FactGrid.AspNet/Models/ApplicationUser.cs` (new)
- `src/FactGrid.AspNet/Data/ApplicationDbContext.cs`
- `src/FactGrid.AspNet/Program.cs`
- `src/FactGrid.AspNet/Data/Migrations/` (auto-generated)
- `tests/FactGrid.Tests/McpEndpointTests.cs`
- `tests/FactGrid.Tests/IngestionControllerTests.cs`

---

## Task 2: UserAccountService Facade

### Priority

High

### Goal

Create a single `UserAccountService` that encapsulates all operations around user registration, API key generation, regeneration, validation, and cache management. All user-facing flows (Register, Admin/CreateUser, ApiKeys/Regenerate) go through this class.

### Why this exists

Without a facade, API key logic would be scattered across the Register page model, Admin controller, and ApiKeys page. Centralising it prevents inconsistencies and makes the auth behaviour auditable.

### Scope

- Create `src/FactGrid.AspNet/Services/UserAccountService.cs`
  - Dependencies: `UserManager<ApplicationUser>`, `IMemoryCache`
  - Methods:
    - `Task<(ApplicationUser User, string ApiKey)> CreateUserAsync(string email, string password)` — creates user with apiKey, caches it, returns user and key
    - `Task<string> RegenerateApiKeyAsync(string userId)` — removes old key from cache, generates new Guid, saves, caches new key, returns it
    - `Task<string?> GetApiKeyAsync(string userId)` — retrieves current key from DB
    - `Task<bool> IsApiKeyValidAsync(string apiKey)` — checks cache first, then DB; normalises to lowercase
  - Key normalisation: always `.ToLowerInvariant()` on save and lookup
  - Cache key pattern: `$"apikey:{normalisedKey}"`
  - `CreateUserAsync` normalises email to lower, generates guid via `Guid.NewGuid().ToString("N").ToLowerInvariant()`
- Register as scoped in `Program.cs`: `builder.Services.AddScoped<UserAccountService>();`

### Acceptance criteria

- `CreateUserAsync` creates an `ApplicationUser` with a lowercase hex (no hyphens) `ApiKey`, caches it
- `RegenerateApiKeyAsync` replaces the key, cache has old removed and new added
- `IsApiKeyValidAsync` returns true for cached/known keys, false for unknown
- All keys stored and compared lowercase
- `dotnet build` succeeds

### Files likely involved

- `src/FactGrid.AspNet/Services/UserAccountService.cs` (new)
- `src/FactGrid.AspNet/Program.cs`

---

## Task 3: ApiKeyAuthenticationService + MCP Route Changes

### Priority

High

### Goal

Wire API key validation into the MCP HTTP transport pipeline. Change MCP routes to include `{apiKey}`, validate in `ConfigureSessionOptions`.

### Why this exists

Without this, the MCP endpoints are still `AllowAnonymous` and unprotected. The route structure must change to embed the API key.

### Scope

- Create `src/FactGrid.AspNet/Services/ApiKeyAuthenticationService.cs`
  - Thin wrapper over `UserAccountService.IsApiKeyValidAsync` (or inline it directly in middleware)
  - Registered as scoped (or just use `UserAccountService` directly)
- Update MCP routes in `Program.cs`:
  - `"/api/mcp/{apiKey}"` (was `"/api/mcp"`)
  - `"/api/mcp/{apiKey}/{entityName}"` (was `"/api/mcp/{entityName}"`)
- Update `ConfigureSessionOptions` callback:
  1. Extract `{apiKey}` from `context.Request.RouteValues`
  2. Resolve `UserAccountService`, call `IsApiKeyValidAsync(apiKey)`
  3. If invalid → `context.Response.StatusCode = 401; return`
  4. If valid → continue to existing entity-scoping logic
- Add `IMemoryCache` registration to `Program.cs` if not already there: `builder.Services.AddMemoryCache();`

### Acceptance criteria

- MCP routes accept `{apiKey}` segment
- Valid GUID → normal operation (entity scoping, sql_query, describe, etc.)
- Invalid GUID → HTTP 401
- Missing GUID → HTTP 401
- Built-in MCP error handling returns controlled message, not exception

### Files likely involved

- `src/FactGrid.AspNet/Services/ApiKeyAuthenticationService.cs` (new, or merged into Task 2)
- `src/FactGrid.AspNet/Program.cs`

---

## Task 4: Program.cs — Seed + Service Registration + Demo Mode

### Priority

High

### Goal

Add first-run seed (admin/Admin1!), register all new services, add demo mode toggle, wire configuration.

### Why this exists

- Seed ensures the app is usable out of the box
- Demo mode controls whether the Register page is open
- Service registrations are the wiring hub

### Scope

- Add `Auth` section to `appsettings.json`:
  ```json
  "Auth": {
    "DemoMode": true,
    "AdminUser": {
      "Email": "admin",
      "Password": "Admin1!"
    }
  }
  ```
- In `Program.cs` after `db.Database.MigrateAsync()`:
  - Check if `userManager.Users.Any()` — if not, seed admin user via `UserAccountService.CreateUserAsync`
  - Seed uses `Configuration.GetSection("Auth:AdminUser")` values
- Register all new services:
  - `builder.Services.AddMemoryCache();`
  - `builder.Services.AddScoped<UserAccountService>();`
- Ensure `IdentityOptions` password config is compatible with `Admin1!` (it is by default)
- No console print of the API key (user gets it from the ApiKeys management page)

### Acceptance criteria

- Fresh database: admin/Admin1! user created automatically
- Non-fresh database: no duplicate seed
- `Auth:DemoMode` config section exists
- All services registered without DI resolution errors
- `dotnet build` succeeds

### Files likely involved

- `src/FactGrid.AspNet/Program.cs`
- `src/FactGrid.AspNet/appsettings.json`

---

## Task 5: IngestionController + EntityController Auth

### Priority

High

### Goal

Protect the ingestion API with API key validation and the EntityController web UI with `[Authorize]`.

### Why this exists

These are the data-modifying endpoints. Ingestion needs API key auth, the web UI needs Identity auth.

### Scope

- **IngestionController.cs:**
  - Change route: `[Route("api/ingestion/{apiKey}")]`
  - Update action: `[HttpPost("{entityName}/upload")]`
  - In `Upload`: extract `apiKey` from route, resolve `UserAccountService`, call `IsApiKeyValidAsync`
  - If invalid → return `401 Unauthorized` with `IngestionResult`
- **EntityController.cs:**
  - Add `[Authorize]` attribute to the class

### Acceptance criteria

- `POST /api/ingestion/{validKey}/worklogs/upload` works as before
- `POST /api/ingestion/{invalidKey}/worklogs/upload` returns 401
- `GET /Entity` redirects to Login page when unauthenticated
- `GET /Entity` works when logged in
- All existing ingestion behaviour preserved (parse, validate, insert)

### Files likely involved

- `src/FactGrid.AspNet/Controllers/IngestionController.cs`
- `src/FactGrid.AspNet/Controllers/EntityController.cs`

---

## Task 6: AdminController + Register Override + ApiKeys Page

### Priority

High

### Goal

- Disable public registration when `DemoMode = false`
- Add admin user creation page
- Add API key management page for all authenticated users

### Scope

- **AdminController.cs** (new):
  - `[Authorize]` on class
  - Route `[Route("Admin")]`
  - `[HttpGet("CreateUser")]` — shows form (email, password, confirm password)
  - `[HttpPost("CreateUser")]` — calls `UserAccountService.CreateUserAsync`, shows the user's API key + per-entity MCP URLs
  - View at `Views/Admin/CreateUser.cshtml`

- **Register page override:**
  - Create `Areas/Identity/Pages/Account/Register.cshtml` + `.cshtml.cs`
  - In `OnGet`: check `IConfiguration.GetValue<bool>("Auth:DemoMode")`
  - If `DemoMode = false` → show static message "Registration is managed by administrators. Please contact your admin to get an account created." and return `Page()` without the form
  - If `DemoMode = true` → call `UserAccountService.CreateUserAsync`, proceed with normal registration + redirect to `/Identity/Account/Manage/ApiKeys` with success message

- **ApiKeys manage page:**
  - Create `Areas/Identity/Pages/Account/Manage/ApiKeys.cshtml` + `.cshtml.cs`
  - Requires `[Authorize]` (inherited from Manage area layout)
  - Shows:
    - "Your API Key: `{guid}`"
    - "Regenerate" button (POST)
    - Table of entities with MCP URLs:
      | Entity | MCP URL | Ingestion URL |
      | `/api/mcp/{guid}/{entity}` | `/api/ingestion/{guid}/{entity}/upload` |
  - Regenerate: calls `UserAccountService.RegenerateApiKeyAsync`, refreshes page with new key
  - Inject `EntityRegistry` to list entities

### Acceptance criteria

- `DemoMode = true`: Register page works, user gets API key on registration
- `DemoMode = false`: Register page shows "Contact admin" message, no form
- `GET /Admin/CreateUser` accessible only when logged in
- `POST /Admin/CreateUser` creates user, displays API key + MCP URLs
- `/Identity/Account/Manage/ApiKeys` shows current key, regenerate works
- `/Identity/Account/Manage/ApiKeys` shows per-entity MCP URLs

### Files likely involved

- `src/FactGrid.AspNet/Controllers/AdminController.cs` (new)
- `src/FactGrid.AspNet/Views/Admin/CreateUser.cshtml` (new)
- `src/FactGrid.AspNet/Areas/Identity/Pages/Account/Register.cshtml` (new)
- `src/FactGrid.AspNet/Areas/Identity/Pages/Account/Register.cshtml.cs` (new)
- `src/FactGrid.AspNet/Areas/Identity/Pages/Account/Manage/ApiKeys.cshtml` (new)
- `src/FactGrid.AspNet/Areas/Identity/Pages/Account/Manage/ApiKeys.cshtml.cs` (new)

---

## Task 7: DataEntryTools — Key from URL + Optional apiKey Param

### Priority

High

### Goal

Remove `FACTGRID_API_KEY` env var. Instead, extract the GUID from `FACTGRID_SERVER_URL`. Also accept an optional `apiKey` parameter for clients that cannot set env vars.

### Why this exists

Simplifies configuration. Some MCP clients (e.g. VS Code, Cursor) cannot set environment variables per tool call, but can pass parameters. The single env var approach with embedded key reduces setup friction.

### Scope

- In `DataEntryTools.cs`:
  - Parse `FACTGRID_SERVER_URL` to extract the GUID:
    - URL pattern: `{base}/api/mcp/{guid}[/{entityName}]`
    - Extract GUID from the path: split by `/` and find the segment after `api/mcp`
    - Validate it looks like a 32-char hex GUID
  - `UploadExcelAsync`:
    - Add optional `[Description("API key for authentication")] string? apiKey = null` parameter
    - If `apiKey` is provided, use it; otherwise extract from `FACTGRID_SERVER_URL`
    - Construct upload URL: `{baseUrl}/api/ingestion/{apiKey}/{entityName}/upload`
    - `baseUrl` is the scheme + host + port from `FACTGRID_SERVER_URL` (everything before `/api/mcp/`)
  - If neither source provides a key → return error
  
- Helper method `TryParseServerUrl(string url, out string baseUrl, out string apiKey)`:
  - Splits on `/api/mcp/` to get the base and the path remainder
  - From path remainder, extracts the first path segment as the GUID
  - Validates: 32 hex chars, no hyphens, lowercase

### Acceptance criteria

- `FACTGRID_SERVER_URL=http://localhost:5000/api/mcp/a1b2c3d4e5f6789012345678abcdef90` → extracts key `a1b2c3d4e5f6789012345678abcdef90`, base `http://localhost:5000`
- Upload URL: `http://localhost:5000/api/ingestion/a1b2c3d4e5f6789012345678abcdef90/worklogs/upload`
- Optional `apiKey` param overrides URL-extracted key
- Missing from both → clear error message
- Malformed URL → clear error message
- Other tools (generate_template, validate_excel, list_entities) unchanged

### Files likely involved

- `src/FactGrid.Mcp/Tools/DataEntryTools.cs`

---

## Task 8: Test Updates

### Priority

High

### Goal

Update all three test files for the new auth model: `ApplicationUser`, API key in paths, auth failure tests.

### Scope

**McpEndpointTests.cs:**
- Replace `IdentityUser` registration with `ApplicationUser`
- Seed an `ApplicationUser` with known `ApiKey`: `"testkey1234567890abcdef000000000000"` (32 hex chars)
- Register `IMemoryCache` and `UserAccountService` in test services
- Update all paths:
  - `/api/mcp` → `/api/mcp/{testKey}`
  - `/api/mcp/worklogs` → `/api/mcp/{testKey}/worklogs`
- Add tests:
  - `InvalidApiKey_Returns401` — POST to `/api/mcp/invalid-key` with tools/call, expect 401
  - `ValidKeyWithUnknownEntity_Returns404` — POST to `/api/mcp/{testKey}/nonexistent`, expect 404

**IngestionControllerTests.cs:**
- Same `ApplicationUser` and key setup
- Update upload paths: `/api/ingestion/{testKey}/worklogs/upload`
- Add test: `InvalidApiKey_Returns401`

**DataEntryToolsMcpTests.cs:**
- Remove `FACTGRID_API_KEY` env var setup
- Set `FACTGRID_SERVER_URL` to `http://localhost:5000/api/mcp/{testKey}`
- Update `MockHttpMessageHandler` to verify the upload URL path contains the key
- Add test: `UploadExcel_MissingApiKey_ReturnsError` (set URL without key, expect error)
- Add test: `UploadExcel_ApiKeyParam_OverridesUrl` (pass `apiKey` param, verify URL)

### Acceptance criteria

- All existing tests pass with updated paths and auth setup
- New auth failure tests pass (401 for bad key)
- New `DataEntryTools` tests pass (missing key error, param override)
- No compilation errors from `ApplicationUser` type change

### Files likely involved

- `tests/FactGrid.Tests/McpEndpointTests.cs`
- `tests/FactGrid.Tests/IngestionControllerTests.cs`
- `tests/FactGrid.Tests/DataEntryToolsMcpTests.cs`

---

## Task 9: Documentation Updates

### Priority

Medium

### Goal

Update `GETTING-STARTED.md`, `AGENTS.md`, and `opencode.json` to reflect the new auth model.

### Scope

**GETTING-STARTING.md:**
- Add "Authentication" section after "Quick Start"
- Document first-run seed: admin/Admin1!
- Document DemoMode toggle in appsettings.json
- New route table:
  | Endpoint | Auth | Description |
  | `/api/mcp/{apiKey}` | API Key | Global MCP (all entities) |
  | `/api/mcp/{apiKey}/{entityName}` | API Key | Scoped MCP |
  | `/api/ingestion/{apiKey}/{entityName}/upload` | API Key | Excel upload |
  | `/Entity` | Web Login | Web UI (browse/upload/delete) |
  | `/Admin/CreateUser` | Web Login | Create new users |
  | `/Identity/Account/Manage/ApiKeys` | Web Login | View/regenerate API key |
- How to get your API key (login → ApiKeys page)
- How to configure opencode.json:
  ```json
  "factgrid": {
    "type": "remote",
    "url": "http://localhost:4792/api/mcp/{your-guid}/worklogs"
  }
  ```
- How to configure FactGrid.Mcp:
  ```json
  "env": {
    "FACTGRID_SERVER_URL": "http://localhost:5000/api/mcp/{your-guid}"
  }
  ```
- Update the curl examples to include the key in the path
- Remove references to FACTGRID_API_KEY

**AGENTS.md:**
- Add auth pattern to Key Patterns section:
  - `ApplicationUser` with `ApiKey` property
  - `UserAccountService` for user/key operations
  - Route pattern: `/api/mcp/{apiKey}`, `/api/ingestion/{apiKey}`
  - ApiKey extracted from `FACTGRID_SERVER_URL` in FactGrid.Mcp
- Add `FACTGRID_SERVER_URL` format note

**opencode.json:**
- Uncomment and update the factgrid entry with a placeholder URL:
  ```json
  "factgrid": {
    "type": "remote",
    "url": "http://localhost:4792/api/mcp/{your-guid}/worklogs"
  }
  ```

### Acceptance criteria

- `GETTING-STARTED.md` has complete auth setup instructions
- `AGENTS.md` has auth pattern
- `opencode.json` has factgrid entry with placeholder
- No outdated references to `FACTGRID_API_KEY` remain

### Files likely involved

- `GETTING-STARTED.md`
- `AGENTS.md`
- `opencode.json`

---

## Suggested Agent Handout Batches

### Batch A: decision-critical (sequential)

- Task 1: ApplicationUser + DbContext + migration
- Task 2: UserAccountService facade
- Task 3: ApiKeyAuthenticationService + MCP routes (modifies Program.cs)

### Batch B: parallel implementation

- Task 4: Program.cs seed + demo mode (depends on Tasks 1–3 being stable — same file)
- Task 5: IngestionController + EntityController (parallel with Task 6)
- Task 6: AdminController + Register override + ApiKeys page (parallel with Task 5)
- Task 7: DataEntryTools (can start after Task 3 at earliest)

### Batch C: verification and docs

- Task 8: Test updates (depends on all implementation tasks)
- Task 9: Documentation (can start after Task 3 route shapes are finalised)

## Final Checklist

- [x] every task has a clear owner-sized scope
- [x] every task has acceptance criteria
- [x] decision-gate tasks are clearly marked
- [x] likely files are listed to reduce agent search time
- [x] execution order reflects real dependencies
