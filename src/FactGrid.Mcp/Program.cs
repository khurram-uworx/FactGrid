using FactGrid.Mcp.Tools;
using FactGrid.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFactGridEntities();
builder.Services.AddHttpClient();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(DataEntryTools).Assembly);

await builder.Build().RunAsync();
