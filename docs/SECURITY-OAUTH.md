# FactGrid Security & OAuth Implementation Plan

## Status: Draft — Plan for Implementation

---

## Overview

Add authentication and authorization to both FactGrid MCP servers:

| MCP Server | Transport | Auth Mechanism | Target Clients |
|------------|-----------|----------------|----------------|
| **FactGrid.AspNet** | HTTP (Streamable) | OpenIddict OAuth 2.0 + JWT Bearer | AI Chat apps (ChatGPT, etc.), browser users |
| **FactGrid.Mcp** | STDIO (local) | OS process boundary (local tools); API key for remote upload | Local IDE tools, automation |

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| OAuth provider | OpenIddict (self-hosted) | Works with existing ASP.NET Core Identity + EF Core. No external dependency. Apache 2.0 license. |
| Auth flows | Authorization code + PKCE + refresh tokens | ChatGPT requires `offline_access` scope and refresh tokens. PKCE mandatory for public clients. |
| Token signing (dev) | ASP.NET Core dev cert | Already trusted on dev machine. `AddDevelopmentSigningCertificate()`. |
| Token signing (prod) | Certificate stored in config/Key Vault | Rotatable, secure. TBD during production deployment. |
| Consent UI | Full Identity login + consent page | Users log in via existing Identity UI, then approve scope. |
| Auth granularity | Auth only (no RBAC) | Any authenticated user can access any entity/tool. Extended later. |
| FactGrid.Mcp upload auth | Static API key (env var + tool arg) | Sent as `X-Api-Key` header. Server accepts JWT OR API key on ingestion endpoint. |
| HTTPS | Enforced | `UseHttpsRedirection()` + `UseHsts()` + `AllowedHosts`. |

---

## Architecture

### Auth Flow: ChatGPT → FactGrid.AspNet

```
┌──────────┐   1. POST /api/mcp (no token)      ┌──────────────────┐
│          │ ──────────────────────────────────→  │                  │
│ ChatGPT  │   401 + WWW-Authenticate (PRM URL)   │  FactGrid.AspNet │
│          │ ←──────────────────────────────────  │                  │
│          │                                      │  ┌────────────┐  │
│          │   2. GET /.well-known/oauth-protected-resource           │
│          │ ──────────────────────────────────→  │  │  OpenIddict │  │
│          │   PRM (auth server, scopes, etc.)    │  │  Server     │  │
│          │ ←──────────────────────────────────  │  │             │  │
│          │                                      │  │  Endpoints: │  │
│          │   3. Redirect user to /connect/authorize                 │
│          │ ──────────────────────────────────→  │  │  /connect/   │  │
│          │   User logs in via Identity (cookie) │  │  authorize   │  │
│          │   User approves consent page         │  │  /connect/   │  │
│          │ ← Auth code ──────────────────────→  │  │  token       │  │
│          │                                      │  │             │  │
│          │   4. POST /connect/token             │  │  /.well-known│  │
│          │   code + code_verifier + PKCE        │  │  /oauth-     │  │
│          │ ← access_token + refresh_token       │  │  protected-  │  │
│          │                                      │  │  resource    │  │
│          │   5. POST /api/mcp (Bearer token)    │  │             │  │
│          │ ──────────────────────────────────→  │  │  /api/       │  │
│          │   JWT validated → ClaimsPrincipal    │  │  ingestion/  │  │
│          │   flows into MCP tools               │  │  {entity}/   │  │
│          │                                      │  │  upload      │  │
└──────────┘                                      └──────────────────┘
```

### Auth Flow: FactGrid.Mcp → FactGrid.AspNet Upload

```
┌──────────────┐    POST /api/ingestion/{entity}/upload    ┌──────────────────┐
│ FactGrid.Mcp │ ────────────────────────────────────────→  │  FactGrid.AspNet │
│              │    X-Api-Key: {key}                        │                  │
│              │    (local validation → file stream)        │  Accepts JWT OR  │
│              │                                            │  X-Api-Key       │
│              │ ← IngestionResult (JSON)                   │                  │
└──────────────┘                                            └──────────────────┘
```

---

## Files

### New Files

| File | Purpose |
|------|---------|
| `src/FactGrid.AspNet/Controllers/AuthorizationController.cs` | OpenIddict authorize + token + consent endpoints |
| `src/FactGrid.AspNet/Services/ApiKeyAuthenticationHandler.cs` | Custom authentication handler for `X-Api-Key` header |
| `src/FactGrid.AspNet/Views/Authorization/Authorize.cshtml` | Consent UI (app name, scopes, approve/deny) |
| `src/FactGrid.AspNet/Views/Authorization/Error.cshtml` | OAuth error display |

### Modified Files

| File | Changes |
|------|---------|
| `FactGrid.AspNet.csproj` | Add `OpenIddict`, `OpenIddict.EntityFrameworkCore`, `OpenIddict.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer` |
| `Data/ApplicationDbContext.cs` | Add `IOpenIddictDbContext`, OpenIddict DbSets |
| `Program.cs` | OpenIddict config, JWT Bearer + `AddMcp()` + ApiKey schemes, MCP resource metadata, HTTPS, remove `.AllowAnonymous()`, add `.RequireAuthorization()`, `UseAuthentication()` |
| `Controllers/IngestionController.cs` | `[Authorize(AuthenticationSchemes = "JwtBearer,ApiKey")]` |
| `Controllers/EntityController.cs` | `[Authorize]` (defaults to Identity cookies) |
| `Properties/launchSettings.json` | Add HTTPS URL |
| `appsettings.json` | `AllowedHosts`, `Auth:BaseUrl`, `Auth:ApiKey` |
| `appsettings.Development.json` | `AllowedHosts`, `Auth:BaseUrl`, `Auth:ApiKey` |
| `Tools/DataEntryTools.cs` | `apiKey` parameter + `FACTGRID_SERVER_APIKEY` env var |
| `tests/FactGrid.Tests/IngestionControllerTests.cs` | Auth integration, API key tests |

---

## Implementation Tasks

### Phase 1: HTTPS + Host Validation

**`Properties/launchSettings.json`**
```json
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "launchBrowser": true,
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "dotnetRunMessages": true,
      "applicationUrl": "https://localhost:4793;http://localhost:4792"
    },
    "http": {
      "commandName": "Project",
      "launchBrowser": true,
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "dotnetRunMessages": true,
      "applicationUrl": "http://localhost:4792"
    }
  },
  "$schema": "https://json.schemastore.org/launchsettings.json"
}
```

**`appsettings.json`** / **`appsettings.Development.json`**
```json
{
  "AllowedHosts": "localhost;127.0.0.1;[::1]"
}
```

**`Program.cs`**
- Add `app.UseHttpsRedirection()` after `app.UseRouting()`
- Add `app.UseHsts()` in non-development

---

### Phase 2: OpenIddict — DbContext

**`Data/ApplicationDbContext.cs`**
```csharp
using OpenIddict.EntityFrameworkCore.Models;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>,
    IOpenIddictDbContext<OpenIddictEntityFrameworkCoreApplication,
                         OpenIddictEntityFrameworkCoreAuthorization,
                         OpenIddictEntityFrameworkCoreScope,
                         OpenIddictEntityFrameworkCoreToken>
{
    public DbSet<OpenIddictEntityFrameworkCoreApplication> Applications => Set<OpenIddictEntityFrameworkCoreApplication>();
    public DbSet<OpenIddictEntityFrameworkCoreAuthorization> Authorizations => Set<OpenIddictEntityFrameworkCoreAuthorization>();
    public DbSet<OpenIddictEntityFrameworkCoreScope> Scopes => Set<OpenIddictEntityFrameworkCoreScope>();
    public DbSet<OpenIddictEntityFrameworkCoreToken> Tokens => Set<OpenIddictEntityFrameworkCoreToken>();
}
```

**Generate EF migration** for the new tables.

---

### Phase 3: OpenIddict — Server Configuration

**`FactGrid.AspNet.csproj`** — add:
```xml
<PackageReference Include="OpenIddict" Version="7.5.0" />
<PackageReference Include="OpenIddict.EntityFrameworkCore" Version="7.5.0" />
<PackageReference Include="OpenIddict.AspNetCore" Version="7.5.0" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.8" />
```

**`Program.cs`** — add after `AddDefaultIdentity`:
```csharp
builder.Services.AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<ApplicationDbContext>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUri("/connect/authorize")
               .SetTokenEndpointUri("/connect/token")
               .SetLogoutEndpointUri("/connect/logout");

        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange()
               .AllowRefreshTokenFlow();

        options.RegisterScopes(
            Scopes.OpenId,
            Scopes.Email,
            Scopes.Profile,
            Scopes.OfflineAccess,    // Required for ChatGPT
            "mcp:tools");            // Custom scope for MCP access

        // Use ASP.NET Core dev cert for signing (development)
        options.AddDevelopmentSigningCertificate();
        options.AddDevelopmentEncryptionCertificate();

        // Integrate with ASP.NET Core
        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableLogoutEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
```

---

### Phase 4: OpenIddict — Client Registration (Seed)

**`Program.cs`** — seed a demo MCP client at startup:
```csharp
// After EnsureCreated / MigrateAsync
await SeedOpenIddictClientsAsync(app.Services);

// ...

static async Task SeedOpenIddictClientsAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    // Register the ChatGPT / MCP demo client
    if (await manager.FindByClientIdAsync("factgrid-mcp") is null)
    {
        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "factgrid-mcp",
            DisplayName = "FactGrid MCP Client (demo)",
            // ChatGPT uses https://chatgpt.com/* redirect URIs.
            // OpenAI docs: https://help.openai.com/en/articles/12584461
            // For dev, use the ChatGPT custom GPT + Actions config panel to
            // obtain the exact redirect URI (typically the ChatGPT workspace URL).
            RedirectUris =
            {
                new Uri("https://chatgpt.com/login/callback", UriKind.Absolute),
                new Uri("https://chatgpt.com/oauth/authorized", UriKind.Absolute),
            },
            PostLogoutRedirectUris =
            {
                new Uri("https://chatgpt.com", UriKind.Absolute),
            },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.OfflineAccess,
                Permissions.Prefixes.Scope + "mcp:tools",
                Permissions.ResponseTypes.Code
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange
            }
        });
    }

    // Register the API key client for FactGrid.Mcp uploads
    // (Handled separately via ApiKeyAuthenticationHandler, not OpenIddict)
}
```

**Important:** The Redirect URIs depend on the ChatGPT workspace — they're typically the ChatGPT domain. For development, use a placeholder and update when configuring in ChatGPT admin console.

---

### Phase 5: Authorization Controller

**`Controllers/AuthorizationController.cs`**

Three endpoints needed:

1. **`GET /connect/authorize`** — Login + consent:
   - If user not authenticated → redirect to Identity login page
   - If user is authenticated → show consent view listing requested scopes
   - On approval → issue authorization code

2. **`POST /connect/token`** — Token exchange:
   - Exchange authorization code for access + refresh tokens
   - Validate PKCE code_verifier
   - Return tokens to client

3. **`POST /connect/logout`** — Logout:
   - Sign out of Identity
   - Clear OpenIddict session

**`Views/Authorization/Authorize.cshtml`**
- Show application name (`factgrid-mcp`)
- Show requested scopes (`openid`, `email`, `profile`, `offline_access`, `mcp:tools`)
- "Allow" / "Deny" buttons
- Post back to `AcceptAsync` / `DenyAsync`

---

### Phase 6: JWT Bearer + MCP Auth

**`Program.cs`** — replace auth config:
```csharp
// Derive base URL from configuration for token issuance/validation.
// Add to appsettings.json/appsettings.Development.json:
//   "Auth": { "BaseUrl": "https://localhost:4793" }
var baseUrl = builder.Configuration.GetValue<string>("Auth:BaseUrl")
    ?? throw new InvalidOperationException("Auth:BaseUrl not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
})
.AddCookie(IdentityConstants.ApplicationScheme)
.AddJwtBearer(options =>
{
    options.Authority = baseUrl;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidIssuer = baseUrl,
        ValidAudience = baseUrl,
        NameClaimType = "name",
        RoleClaimType = "roles"
    };
    // IMPORTANT: Do NOT add an OnAuthenticationFailed fallback to ApiKey here.
    // Instead, MCP route policies and controller [Authorize] attributes declare
    // both schemes explicitly (see below). The framework tries each scheme in order.
})
.AddMcp(options =>
{
    options.ResourceMetadata = new()
    {
        ResourceDocumentation = "https://github.com/khurram-uworx/FactGrid",
        AuthorizationServers = { baseUrl },
        ScopesSupported = { "mcp:tools" },
    };
})
.AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationHandler.SchemeName, null);
```

**Usings to add to `Program.cs`:**
```csharp
using FactGrid.AspNet.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
```

**`Services/ApiKeyAuthenticationHandler.cs`** (NEW):
```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

public static class ApiKeyAuthenticationHandler
{
    public const string SchemeName = "ApiKey";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        var configuredKey = Options.ApiKey
            ?? Context.RequestServices.GetRequiredService<IConfiguration>()
                .GetValue<string>("Auth:ApiKey");

        if (!string.IsNullOrEmpty(apiKey) && apiKey == configuredKey)
        {
            var identity = new ClaimsIdentity(SchemeName);
            identity.AddClaim(new Claim(ClaimTypes.Name, "factgrid-mcp"));
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public string? ApiKey { get; set; }
}
```

**`appsettings.json` / `appsettings.Development.json`** — add:
```json
{
  "Auth": {
    "BaseUrl": "https://localhost:4793",
    "ApiKey": "dev-api-key-change-in-production"
  }
}
```

**Middleware pipeline order — `Program.cs`:**
```csharp
app.UseRouting();
app.UseHttpsRedirection();
app.UseAuthentication();      // MUST be before UseAuthorization
app.UseAuthorization();
```

**MCP route protection — both routes require JWT Bearer OR API key:**
```csharp
app.MapMcp("/api/mcp")
   .RequireAuthorization(policy =>
   {
       policy.AddAuthenticationSchemes(
           JwtBearerDefaults.AuthenticationScheme,
           ApiKeyAuthenticationHandler.SchemeName);
       policy.RequireAuthenticatedUser();
   });

app.MapMcp("/api/mcp/{entityName}")
   .RequireAuthorization(policy =>
   {
       policy.AddAuthenticationSchemes(
           JwtBearerDefaults.AuthenticationScheme,
           ApiKeyAuthenticationHandler.SchemeName);
       policy.RequireAuthenticatedUser();
   });
```

**MCP tool-level authorization (optional):**
```csharp
// Also add authorization filters to support [Authorize] on individual tools:
builder.Services.AddMcpServer()
    .WithHttpTransport(/* ... */)
    .AddAuthorizationFilters()
    .WithToolsFromAssembly(typeof(GenericSqlQueryTool).Assembly)
    // ...
```

---

### Phase 7: Controller Authorization

**`Controllers/IngestionController.cs`**

Accept both JWT Bearer (ChatGPT) and ApiKey (FactGrid.Mcp upload):
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;

[Route("api/ingestion")]
[ApiController]
[Authorize(AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{ApiKeyAuthenticationHandler.SchemeName}")]
public class IngestionController : ControllerBase
```

The authentication middleware tries each declared scheme in order: JWT first, then API key. No `OnAuthenticationFailed` event hack is needed — the framework handles the fallback natively.

**`Controllers/EntityController.cs`**
```csharp
[Route("Entity")]
[Authorize]  // Uses Identity cookies by default (browser users)
public class EntityController : Controller
```

---

### Phase 8: API Key for FactGrid.Mcp Upload

**`Tools/DataEntryTools.cs`** — modify `UploadExcelAsync`:
```csharp
public async Task<string> UploadExcelAsync(
    [Description("Entity name (e.g., worklogs, expenses)")] string entityName,
    [Description("Full file path to the Excel file to upload")] string filePath,
    [Description("Optional API key for server authentication")] string? apiKey = null,
    CancellationToken ct = default)
{
    // ...
    apiKey ??= Environment.GetEnvironmentVariable("FACTGRID_SERVER_APIKEY");
    // ...
    
    var httpClient = httpClientFactory.CreateClient();
    if (!string.IsNullOrEmpty(apiKey))
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    
    // ... rest of upload logic
}
```

---

### Phase 9: Pipeline & Startup Order

Final `Program.cs` structure (summary):

```
1.  Configuration (provider, connection string)
2.  Services:
    a. DbContext + provider
    b. Identity (AddDefaultIdentity)
    c. OpenIddict (core + server + validation)
    d. Authentication (cookie + JWT Bearer + MCP + ApiKey)
    e. Authorization
    f. Controllers + MVC
    g. FactGrid entities + services
    h. MCP server (tools, resources, prompts)
3.  Build
4.  Startup:
    a. Ensure DB created / migrated
    b. Seed OpenIddict clients
5.  Middleware pipeline:
    a. Exception handling (dev/staging)
    b. HttpsRedirection + Hsts
    c. Routing
    d. Authentication
    e. Authorization
    f. Entity context cleanup (clears per-request state)
    g. Static assets
    h. MVC routes
    i. MCP routes (with RequireAuthorization)
6.  Run

**Entity context cleanup runs after auth** so authenticated identity is available if needed (currently cleanup is state-only, but the auth context is available if future middleware needs it).
```

---

### Phase 10: Test Updates

**IMPORTANT:** All existing integration tests (`IngestionControllerTests.cs`, `McpEndpointTests.cs`) send unauthenticated requests. After adding auth, these will return 401. Every test must be updated.

**`tests/FactGrid.Tests/IngestionControllerTests.cs`**

**Do NOT attempt password-grant or authorization code token acquisition** in integration tests — the JWT Bearer handler requires a resolvable `Authority` URL and HTTPS, both problematic in test hosts. Instead, replace the JWT Bearer scheme with a custom `TestJwtAuthHandler` that validates a sentinel token value:

```csharp
// Nested class inside the test fixture
class TestJwtAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string TestToken = "valid-test-token";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is not null && authHeader == $"Bearer {TestToken}")
        {
            var identity = new ClaimsIdentity(Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.Name, "test-user"));
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
```

Register in the test factory's `ConfigureServices` — due to .NET 10 API changes, `AddScheme` throws on duplicate scheme names. Instead, swap the `HandlerType` on the existing Bearer scheme builder:
```csharp
builder.ConfigureServices(services =>
{
    // ... existing setup ...
    services.Configure<AuthenticationOptions>(o =>
    {
        foreach (var scheme in o.Schemes)
            if (scheme.Name == JwtBearerDefaults.AuthenticationScheme)
                scheme.HandlerType = typeof(TestJwtAuthHandler);
    });
});
```

The test handler returns `Success` for `"valid-test-token"` and `NoResult()` for anything else (falls through to ApiKey scheme). This allows testing both JWT and API key auth paths deterministically.

Key tests:

| Test | Approach |
|------|----------|
| `Upload_WithoutAuth_Returns401` | Client has no auth headers → framework challenges with 401 |
| `Upload_WithValidJwt_Returns200` | Send `Bearer valid-test-token` → TestJwtAuthHandler returns Success |
| `Upload_WithApiKey_Returns200` | Send `X-Api-Key: test-api-key` → ApiKeyAuthenticationHandler validates against config |
| `Upload_WithExpiredToken_Returns401` | Send `Bearer garbage-or-expired-token` → TestJwtAuthHandler returns NoResult → no other scheme matches → 401 |
| Existing tests | Use API key via `_client.DefaultRequestHeaders.Add("X-Api-Key", "test-api-key")` |

**`tests/FactGrid.Tests/McpEndpointTests.cs`**
- Same pattern: acquire a test token in `OneTimeSetUp`, set `_client.DefaultRequestHeaders.Authorization`
- All existing MCP endpoint tests continue to work with the bearer token

**`tests/FactGrid.Tests/DataEntryToolsMcpTests.cs`**
- Update `UploadExcel_ServerSuccess_ReturnsResultSummary` and similar tests to verify `X-Api-Key` header is sent by the tool:
  ```csharp
  Assert.That(req.Headers.Contains("X-Api-Key"), Is.True);
  ```

---

## Dependencies

| Package | Version | Source |
|---------|---------|--------|
| `OpenIddict` | 7.5.0 | NuGet |
| `OpenIddict.EntityFrameworkCore` | 7.5.0 | NuGet |
| `OpenIddict.AspNetCore` | 7.5.0 | NuGet |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.8 | NuGet |

---

## ChatGPT-Specific Requirements

Per [OpenAI developer mode docs](https://help.openai.com/en/articles/12584461-developer-mode-and-mcp-apps-in-chatgpt):

| Requirement | How We Meet It |
|-------------|----------------|
| OAuth 2.0 authorization code flow | OpenIddict `AllowAuthorizationCodeFlow()` |
| PKCE | `RequireProofKeyForCodeExchange()` |
| `offline_access` scope for refresh tokens | `Scopes.OfflineAccess` registered |
| Refresh tokens issued | `AllowRefreshTokenFlow()` |
| `/.well-known/openid-configuration` discovery | Automatic with OpenIddict |
| `/.well-known/oauth-protected-resource` (PRM) | `AddMcp(o => o.ResourceMetadata = ...)` |
| Public client (no client secret) | Register app without client secret in OpenIddict |
| Tool scanning | Standard MCP protocol — automatic after auth |

---

## Future Considerations

| Item | When |
|------|------|
| RBAC (roles: Admin, Editor, Viewer) | Post-MVP |
| Per-entity authorization scopes | Post-MVP |
| Production token signing certificate | Production deployment |
| OpenIddict token cleanup (background job) | Production deployment |
| Rate limiting on `/connect/token` | Production deployment |
| Audit logging for OAuth events | Post-MVP |
| `Secure MCP Tunnel` for private network | If deployed behind corporate VPN |
