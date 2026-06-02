using EfMcp.AspNet.Data;
using EfMcp.AspNet.Services;
using EfMcp.AspNet.Tools;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var provider = builder.Configuration.GetValue<string>("Storage:Provider") ?? "sqlserver";

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

// Configure the HTTP request pipeline.
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
