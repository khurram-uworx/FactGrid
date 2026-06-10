using FactGrid.AspNet.Data;
using FactGrid.AspNet.Services;
using FactGrid.AspNet.Tools;
using FactGrid.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

var provider = builder.Configuration.GetValue<string>("Storage:Provider") ?? "sqlserver";

var connectionString = provider.ToLowerInvariant() switch
{
    "sqlite" => EnsureSqliteCacheShared(
        builder.Configuration.GetConnectionString("Sqlite")
        ?? "Data Source=App_Data/factgrid.db"),
    "postgresql" => builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException("Connection string 'PostgreSql' not found."),
    "sqlserver" => builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("Connection string 'SqlServer' not found."),
    var p => throw new InvalidOperationException($"Unsupported Storage:Provider '{p}'.")
};

var baseUrl = builder.Configuration.GetValue<string>("Auth:BaseUrl")
    ?? throw new InvalidOperationException("Auth:BaseUrl not configured.");

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

    options.UseOpenIddict();
});

if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<ApplicationDbContext>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token")
               .SetEndSessionEndpointUris("/connect/logout");

        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange()
               .AllowRefreshTokenFlow();

        options.RegisterScopes(
            Scopes.OpenId,
            Scopes.Email,
            Scopes.Profile,
            Scopes.OfflineAccess,
            "mcp:tools");

        options.AddDevelopmentSigningCertificate();
        options.AddDevelopmentEncryptionCertificate();

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableEndSessionEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
})
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

builder.Services.AddControllersWithViews();

// Phase 3 — Shared entity catalog and DI
builder.Services.AddFactGridEntities();
builder.Services.AddScoped(typeof(IEntityTableService<>), typeof(EntityTableService<>));
builder.Services.AddScoped<IEntityServiceFactory, EntityServiceFactory>();
builder.Services.AddSingleton<IEntityContextAccessor, EntityContextAccessor>();
builder.Services.AddSingleton<QueryValidationService>();

// MCP server with per-request entity context
builder.Services.AddMcpServer()
    .WithHttpTransport(o =>
    {
        o.Stateless = true;
        o.PerSessionExecutionContext = true;
        o.ConfigureSessionOptions = (context, options, ct) =>
        {
            var entityName = context.Request.RouteValues["entityName"] as string;
            if (!string.IsNullOrEmpty(entityName))
            {
                var entityRegistry = context.RequestServices.GetRequiredService<EntityRegistry>();
                var entity = entityRegistry.Get(entityName);
                if (entity is null)
                {
                    context.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }
                var accessor = context.RequestServices.GetRequiredService<IEntityContextAccessor>();
                accessor.CurrentEntity = entity;
            }
            return Task.CompletedTask;
        };
    })
    .WithToolsFromAssembly(typeof(GenericSqlQueryTool).Assembly)
    .WithResourcesFromAssembly(typeof(EntityResources).Assembly)
    .WithPromptsFromAssembly(typeof(EntityPrompts).Assembly);

var app = builder.Build();

// Ensure database is ready at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
    {
        var connStr = db.Database.GetConnectionString();
        if (connStr is not null)
        {
            var sqliteBuilder = new SqliteConnectionStringBuilder(connStr);
            var dataSource = sqliteBuilder.DataSource;
            if (!string.IsNullOrEmpty(dataSource) && dataSource != ":memory:")
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
        }
    }

    await db.Database.MigrateAsync();

    if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
    {
        await db.Database.ExecuteSqlRawAsync(
            "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");
    }
}

// Seed OpenIddict clients
await SeedOpenIddictClientsAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();

// Clear stale entity context before each request (prevents cross-request leakage)
app.Use(async (context, next) =>
{
    var accessor = context.RequestServices.GetRequiredService<IEntityContextAccessor>();
    accessor.CurrentEntity = null;
    await next();
});

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

// MCP routes — global (/api/mcp) and scoped (/api/mcp/{entityName})
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

app.Run();

static string EnsureSqliteCacheShared(string connString)
{
    var builder = new SqliteConnectionStringBuilder(connString);
    if (builder.Cache != SqliteCacheMode.Shared)
    {
        builder.Cache = SqliteCacheMode.Shared;
    }
    return builder.ConnectionString;
}

static async Task SeedOpenIddictClientsAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

    if (await manager.FindByClientIdAsync("factgrid-mcp") is null)
    {
        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "factgrid-mcp",
            DisplayName = "FactGrid MCP Client (demo)",
            ConsentType = ConsentTypes.Explicit,
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
                Permissions.Prefixes.Scope + "offline_access",
                Permissions.Prefixes.Scope + "mcp:tools",
                Permissions.ResponseTypes.Code,
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange,
            },
        });
    }
}
