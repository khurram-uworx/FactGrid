using FactGrid.AspNet.Data;
using FactGrid.AspNet.Services;
using FactGrid.Models;
using FactGrid.Services;
using Microsoft.AspNetCore.Mvc;

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
        try
        {
            var entity = registry.Get(entityName);
            if (entity is null)
            {
                return NotFound(new IngestionResult
                {
                    Success = false,
                    InsertedCount = 0,
                    Errors = [$"Unknown entity '{entityName}'"]
                });
            }

            if (file is null || file.Length == 0)
            {
                return BadRequest(new IngestionResult
                {
                    Success = false,
                    InsertedCount = 0,
                    Errors = ["No file provided or file is empty."]
                });
            }

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new IngestionResult
                {
                    Success = false,
                    InsertedCount = 0,
                    Errors = ["Only .xlsx files are supported."]
                });
            }

            using var stream = file.OpenReadStream();
            var parser = factory.CreateExcelParser(entity.ModelType);

            System.Collections.IList records = new List<object>();
            List<string> errors = [];
            try
            {
                (records, errors) = parser.Parse(stream);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return BadRequest(new IngestionResult
                {
                    Success = false,
                    InsertedCount = 0,
                    Errors = [$"Could not read workbook: {ex.Message}"]
                });
            }

            if (errors.Count > 0)
            {
                return UnprocessableEntity(new IngestionResult
                {
                    Success = false,
                    InsertedCount = 0,
                    Errors = errors.ToArray()
                });
            }

            foreach (var record in records)
                db.Add(record);
            await db.SaveChangesAsync();

            return Ok(new IngestionResult
            {
                Success = true,
                InsertedCount = records.Count,
                Errors = []
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return StatusCode(500, new IngestionResult
            {
                Success = false,
                InsertedCount = 0,
                Errors = ["An unexpected error occurred while processing the upload."]
            });
        }
    }
}
