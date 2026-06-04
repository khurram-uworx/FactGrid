using FactGrid.Models;
using FactGrid.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;

namespace FactGrid.Mcp.Tools;

[McpServerToolType]
public sealed class DataEntryTools
{
    readonly EntityRegistry registry;
    readonly ExcelTemplateGenerator templateGenerator;
    readonly IServiceProvider serviceProvider;
    readonly IHttpClientFactory httpClientFactory;

    public DataEntryTools(
        EntityRegistry registry,
        ExcelTemplateGenerator templateGenerator,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        this.registry = registry;
        this.templateGenerator = templateGenerator;
        this.serviceProvider = serviceProvider;
        this.httpClientFactory = httpClientFactory;
    }

    [McpServerTool, Description("Generates an Excel template for the given entity and saves it to the specified path. Returns the saved path and a summary of columns.")]
    public string GenerateTemplate(
        [Description("Entity name (e.g., worklogs, expenses)")] string entityName,
        [Description("Full file path where the template should be saved")] string outputPath)
    {
        var entity = registry.Get(entityName);
        if (entity is null)
            return $"Error: Unknown entity '{entityName}'";

        var path = templateGenerator.Generate(entityName, outputPath);

        var columns = entity.ModelType.GetProperties()
            .Select(p => p.GetCustomAttribute<ExcelColumnAttribute>())
            .Where(a => a is not null)
            .OrderBy(a => a!.Position)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Template saved to: {path}");
        sb.AppendLine();
        sb.AppendLine($"Entity: {entity.DisplayName}");
        sb.AppendLine($"Columns: {columns.Count}");
        sb.AppendLine();
        sb.AppendLine("| # | Column | Required | Example |");
        sb.AppendLine("|---|--------|----------|---------|");
        foreach (var col in columns)
            sb.AppendLine($"| {col!.Position} | {col.Title} | {col.Required} | {col.Example} |");

        return sb.ToString();
    }

    [McpServerTool, Description("Validates an Excel file against the entity's parser rules. Returns a preview of parsed records and any validation errors. Does not modify any data.")]
    public async Task<string> ValidateExcelAsync(
        [Description("Entity name (e.g., worklogs, expenses)")] string entityName,
        [Description("Full file path to the Excel file to validate")] string filePath,
        CancellationToken ct = default)
    {
        var entity = registry.Get(entityName);
        if (entity is null)
            return $"Error: Unknown entity '{entityName}'";

        if (!File.Exists(filePath))
            return $"Error: File not found at '{filePath}'";

        if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return $"Error: Only .xlsx files are supported.";

        using var scope = serviceProvider.CreateScope();
        var parser = (IExcelParser)scope.ServiceProvider.GetRequiredService(
            typeof(IExcelParser<>).MakeGenericType(entity.ModelType));

        await using var stream = File.OpenRead(filePath);
        var (records, errors) = parser.Parse(stream);

        var sb = new StringBuilder();

        sb.AppendLine($"## Validation Results for {entity.DisplayName}");
        sb.AppendLine();
        sb.AppendLine($"File: {filePath}");
        sb.AppendLine($"Records parsed: {records.Count}");
        sb.AppendLine($"Errors: {errors.Count}");

        if (records.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Record Preview");
            sb.AppendLine();

            var columns = entity.ModelType.GetProperties()
                .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
                .Where(x => x.Attr is not null)
                .OrderBy(x => x.Attr!.Position)
                .ToList();

            sb.Append('|');
            foreach (var (_, attr) in columns)
                sb.Append($" {attr!.Title} |");
            sb.AppendLine();

            sb.Append('|');
            foreach (var _ in columns)
                sb.Append("---|");
            sb.AppendLine();

            var previewCount = Math.Min(records.Count, 5);
            for (var i = 0; i < previewCount; i++)
            {
                var record = records[i];
                sb.Append('|');
                foreach (var (prop, _) in columns)
                {
                    var val = prop.GetValue(record)?.ToString() ?? "";
                    sb.Append($" {val.Replace("|", "\\|")} |");
                }
                sb.AppendLine();
            }

            if (records.Count > 5)
                sb.AppendLine($"*... and {records.Count - 5} more records*");
        }

        if (errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Errors");
            sb.AppendLine();
            foreach (var error in errors)
                sb.AppendLine($"- {error}");
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Uploads an Excel file to the central FactGrid server for processing and storage. The server validates and atomically inserts records. Returns the server's structured response.")]
    public async Task<string> UploadExcelAsync(
        [Description("Entity name (e.g., worklogs, expenses)")] string entityName,
        [Description("Full file path to the Excel file to upload")] string filePath,
        CancellationToken ct = default)
    {
        var entity = registry.Get(entityName);
        if (entity is null)
            return $"Error: Unknown entity '{entityName}'";

        if (!File.Exists(filePath))
            return $"Error: File not found at '{filePath}'";

        if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return $"Error: Only .xlsx files are supported.";

        var serverUrl = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL") ?? "http://localhost:5000";
        var uploadUrl = $"{serverUrl.TrimEnd('/')}/api/ingestion/{entityName}/upload";

        var httpClient = httpClientFactory.CreateClient();

        await using var fileStream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "file", Path.GetFileName(filePath));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(uploadUrl, content, ct);
        }
        catch (Exception ex)
        {
            return $"Error: Upload failed — {ex.Message}";
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return $"Server responded with status {(int)response.StatusCode}:\n\n{responseBody}";
    }

    [McpServerTool, Description("Lists all registered entities with their descriptions and Excel column metadata.")]
    public string ListEntities()
    {
        var entities = registry.GetAll().ToList();
        if (entities.Count == 0)
            return "No entities registered.";

        var sb = new StringBuilder();
        sb.AppendLine("# Registered Entities");
        sb.AppendLine();
        sb.AppendLine("| Entity Name | Display Name | Table | Description |");
        sb.AppendLine("|-------------|--------------|-------|-------------|");
        foreach (var entity in entities)
            sb.AppendLine($"| {entity.EntityName} | {entity.DisplayName} | {entity.TableName} | {entity.Description} |");

        foreach (var entity in entities)
        {
            sb.AppendLine();
            sb.AppendLine($"## {entity.DisplayName} Columns");
            sb.AppendLine();
            sb.AppendLine("| # | Column | Required | Example |");
            sb.AppendLine("|---|--------|----------|---------|");

            var columns = entity.ModelType.GetProperties()
                .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
                .Where(x => x.Attr is not null)
                .OrderBy(x => x.Attr!.Position)
                .ToList();

            foreach (var (_, attr) in columns)
                sb.AppendLine($"| {attr!.Position} | {attr.Title} | {attr.Required} | {attr.Example} |");
        }

        return sb.ToString();
    }
}
