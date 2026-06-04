using FactGrid.Models;
using FactGrid.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.Collections;
using System.ComponentModel;
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
        [Description("Full .xlsx file path where the template should be saved")] string outputPath)
    {
        var entity = registry.Get(entityName);
        if (entity is null)
            return $"Error: Unknown entity '{entityName}'";

        if (!outputPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return "Error: Output path must end with .xlsx";

        var path = templateGenerator.Generate(entityName, outputPath);

        var columns = ExcelColumnMetadata.GetColumns(entity.ModelType);

        var sb = new StringBuilder();
        sb.AppendLine($"Template saved to: {path}");
        sb.AppendLine();
        sb.AppendLine($"Entity: {entity.DisplayName}");
        sb.AppendLine($"Columns: {columns.Count}");
        sb.AppendLine();
        sb.AppendLine("| # | Column | Required | Example |");
        sb.AppendLine("|---|--------|----------|---------|");
        foreach (var col in columns)
            sb.AppendLine($"| {col.Position} | {col.Attr.Title} | {col.Attr.Required} | {col.Attr.Example} |");

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

        IList records;
        List<string> errors;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var parser = (IExcelParser)scope.ServiceProvider.GetRequiredService(
                typeof(IExcelParser<>).MakeGenericType(entity.ModelType));

            await using var stream = File.OpenRead(filePath);
            (records, errors) = parser.Parse(stream);
        }
        catch (Exception ex)
        {
            return $"Error: Failed to parse workbook — {ex.Message}";
        }

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

            var columns = ExcelColumnMetadata.GetColumns(entity.ModelType);

            sb.Append('|');
            foreach (var col in columns)
                sb.Append($" {col.Attr.Title} |");
            sb.AppendLine();

            sb.Append('|');
            foreach (var _ in columns)
                sb.Append("---|");
            sb.AppendLine();

            var previewCount = Math.Min(records.Count, 20);
            for (var i = 0; i < previewCount; i++)
            {
                var record = records[i];
                sb.Append('|');
                foreach (var col in columns)
                {
                    var val = col.Property.GetValue(record)?.ToString() ?? "";
                    sb.Append($" {val.Replace("|", "\\|")} |");
                }
                sb.AppendLine();
            }

            if (records.Count > 20)
                sb.AppendLine($"*... and {records.Count - 20} more records*");
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

        var serverUrl = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        if (string.IsNullOrWhiteSpace(serverUrl))
            return "Error: FACTGRID_SERVER_URL environment variable is not set. Point it at the FactGrid server URL (e.g., http://localhost:5000).";

        // Validate locally before uploading
        IList localRecords;
        List<string> localErrors;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var parser = (IExcelParser)scope.ServiceProvider.GetRequiredService(
                typeof(IExcelParser<>).MakeGenericType(entity.ModelType));

            await using var parseStream = File.OpenRead(filePath);
            (localRecords, localErrors) = parser.Parse(parseStream);
        }
        catch (Exception ex)
        {
            return $"Error: Failed to parse workbook locally — {ex.Message}";
        }

        if (localErrors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Error: Workbook has validation errors. Fix them before uploading.");
            sb.AppendLine();
            sb.AppendLine($"Records parsed: {localRecords.Count}");
            sb.AppendLine($"Errors: {localErrors.Count}");
            sb.AppendLine();
            foreach (var error in localErrors)
                sb.AppendLine($"- {error}");
            return sb.ToString();
        }

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

        var statusCode = (int)response.StatusCode;
        IngestionResult? result = null;
        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<IngestionResult>(responseBody,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            // Not JSON or unexpected shape — fall through to raw text
        }

        if (result is not null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Server responded with status {statusCode}");
            sb.AppendLine();
            sb.AppendLine($"Success: {result.Success}");
            sb.AppendLine($"Records inserted: {result.InsertedCount}");
            if (result.Errors is { Length: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("Server errors:");
                foreach (var error in result.Errors)
                    sb.AppendLine($"- {error}");
            }
            return sb.ToString();
        }

        return $"Server responded with status {statusCode}:\n\n{responseBody}";
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

            var columns = ExcelColumnMetadata.GetColumns(entity.ModelType);

            foreach (var col in columns)
                sb.AppendLine($"| {col.Position} | {col.Attr.Title} | {col.Attr.Required} | {col.Attr.Example} |");
        }

        return sb.ToString();
    }
}
