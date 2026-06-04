using FactGrid.AspNet.Data;
using FactGrid.AspNet.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections;

namespace FactGrid.AspNet.Controllers;

[Route("Entity")]
public class EntityController : Controller
{
    readonly ApplicationDbContext db;
    readonly EntityRegistry registry;
    readonly IEntityServiceFactory factory;

    public EntityController(ApplicationDbContext db, EntityRegistry registry, IEntityServiceFactory factory)
    {
        this.db = db;
        this.registry = registry;
        this.factory = factory;
    }

    Task<long> countRows(EntityRegistration entity)
        => factory.CreateTableService(entity.ModelType).CountAsync();

    Task deleteAll(EntityRegistration entity)
        => factory.CreateTableService(entity.ModelType).DeleteAllAsync();

    (IList Records, List<string> Errors) parseExcel(EntityRegistration entity, Stream stream)
        => factory.CreateExcelParser(entity.ModelType).Parse(stream);

    [HttpGet("")]
    public IActionResult List()
    {
        var entities = registry.GetAll().ToList();
        return View(entities);
    }

    [HttpGet("{entityName}")]
    public async Task<IActionResult> Detail(string entityName)
    {
        var entity = registry.Get(entityName);
        if (entity is null) return NotFound();

        var count = await countRows(entity);
        ViewBag.Entity = entity;
        return View(count);
    }

    [HttpPost("{entityName}/Upload")]
    public async Task<IActionResult> Upload(string entityName, IFormFile file)
    {
        var entity = registry.Get(entityName);
        if (entity is null) return NotFound();

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a file.";
            return RedirectToAction(nameof(Detail), new { entityName });
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Only .xlsx files are supported.";
            return RedirectToAction(nameof(Detail), new { entityName });
        }

        using var stream = file.OpenReadStream();
        var (records, errors) = parseExcel(entity, stream);

        if (records.Count > 0)
        {
            foreach (var record in records)
                db.Add(record);
            await db.SaveChangesAsync();
        }

        var message = $"Inserted {records.Count} records.";
        if (errors.Count > 0)
            message += $" Errors: {string.Join("; ", errors)}";

        TempData["Message"] = message;
        return RedirectToAction(nameof(Detail), new { entityName });
    }

    [HttpPost("{entityName}/DeleteAll")]
    public async Task<IActionResult> DeleteAll(string entityName)
    {
        var entity = registry.Get(entityName);
        if (entity is null) return NotFound();

        await deleteAll(entity);
        TempData["Message"] = "All records deleted.";
        return RedirectToAction(nameof(Detail), new { entityName });
    }
}
