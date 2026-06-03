using EfMcp.AspNet.Data;
using EfMcp.AspNet.Services;
using EfMcp.AspNet.Tools;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var provider = builder.Configuration.GetValue<string>("Storage:Provider") ?? "sqlserver";

var connectionString = provider.ToLowerInvariant() switch
{
    "sqlite" => EnsureSqliteCacheShared(
        builder.Configuration.GetConnectionString("Sqlite")
        ?? "Data Source=App_Data/efmcp.db"),
    "postgresql" => builder.Configuration.GetConnectionString("PostgreSql")
        ?? throw new InvalidOperationException("Connection string 'PostgreSql' not found."),
    "sqlserver" => builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("Connection string 'SqlServer' not found."),
    var p => throw new InvalidOperationException($"Unsupported Storage:Provider '{p}'.")
};

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
});

if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();

builder.Services.AddScoped<ExcelUploadService>();
builder.Services.AddSingleton<QueryValidationService>();

builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly(typeof(WorklogsMcpTools).Assembly);

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

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.MapMcp("/api/mcp/Worklogs")
   .AllowAnonymous();

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
