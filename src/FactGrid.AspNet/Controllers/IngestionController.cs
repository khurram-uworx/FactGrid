using FactGrid.AspNet.Data;
using FactGrid.AspNet.Services;
using FactGrid.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FactGrid.AspNet.Controllers;

[Route("api/ingestion")]
[ApiController]
public class IngestionController : ControllerBase
{
    readonly ApplicationDbContext db;
    readonly EntityRegistry registry;
    readonly IEntityServiceFactory factory;

    public IngestionController(ApplicationDbContext db, EntityRegistry registry, IEntityServiceFactory factory)
    {
        this.db = db;
        this.registry = registry;
        this.factory = factory;
    }

    [HttpPost("{entityName}/upload")]
    public async Task<IActionResult> Upload(string entityName, IFormFile? file)
    {
        var entity = registry.Get(entityName);
        if (entity is null)
        {
            return NotFound(new
            {
                success = false,
                insertedCount = 0,
                errors = new[] { $"Unknown entity '{entityName}'" }
            });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new
            {
                success = false,
                insertedCount = 0,
                errors = new[] { "No file provided or file is empty." }
            });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                success = false,
                insertedCount = 0,
                errors = new[] { "Only .xlsx files are supported." }
            });
        }

        using var stream = file.OpenReadStream();
        var parser = factory.CreateExcelParser(entity.ModelType);
        var (records, errors) = parser.Parse(stream);

        if (errors.Count > 0)
        {
            return UnprocessableEntity(new
            {
                success = false,
                insertedCount = 0,
                errors
            });
        }

        foreach (var record in records)
            db.Add(record);
        await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            insertedCount = records.Count,
            errors = Array.Empty<string>()
        });
    }
}
