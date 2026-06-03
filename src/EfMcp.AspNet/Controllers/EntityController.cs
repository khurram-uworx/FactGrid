using System.Collections;
using System.Reflection;
using EfMcp.AspNet.Data;
using EfMcp.AspNet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EfMcp.AspNet.Controllers;

[Route("Entity")]
public class EntityController : Controller
{
    readonly ApplicationDbContext db;
    readonly EntityRegistry registry;
    readonly IServiceProvider serviceProvider;

    public EntityController(ApplicationDbContext db, EntityRegistry registry, IServiceProvider serviceProvider)
    {
        this.db = db;
        this.registry = registry;
        this.serviceProvider = serviceProvider;
    }

    Task<long> CountRows(EntityRegistration entity)
    {
        var method = typeof(EntityController)
            .GetMethod(nameof(CountRowsCore), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entity.ModelType);
        var task = (Task<long>)method.Invoke(this, null)!;
        return task;
    }

    async Task<long> CountRowsCore<T>() where T : class
    {
        return await db.Set<T>().LongCountAsync();
    }

    Task DeleteAll(EntityRegistration entity)
    {
        var method = typeof(EntityController)
            .GetMethod(nameof(DeleteAllCore), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entity.ModelType);
        var task = (Task)method.Invoke(this, null)!;
        return task;
    }

    async Task DeleteAllCore<T>() where T : class
    {
        await db.Set<T>().ExecuteDeleteAsync();
    }

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

        var count = await CountRows(entity);
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
        var (records, errors) = ParseExcel(entity, stream);

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

    (IList Records, List<string> Errors) ParseExcel(EntityRegistration entity, Stream stream)
    {
        var interfaceType = typeof(IExcelParser<>).MakeGenericType(entity.ModelType);
        var parser = serviceProvider.GetRequiredService(interfaceType);

        if (parser.GetType() != entity.ExcelParserType)
        {
            throw new InvalidOperationException(
                $"Expected parser '{entity.ExcelParserType.Name}' for entity '{entity.EntityName}', " +
                $"but DI resolved '{parser.GetType().Name}'.");
        }

        var parseMethod = interfaceType.GetMethod("Parse")!;
        var result = parseMethod.Invoke(parser, [stream])!;

        var item1Prop = result.GetType().GetProperty("Item1")!;
        var item2Prop = result.GetType().GetProperty("Item2")!;

        return ((IList)item1Prop.GetValue(result)!, (List<string>)item2Prop.GetValue(result)!);
    }

    [HttpPost("{entityName}/DeleteAll")]
    public async Task<IActionResult> DeleteAll(string entityName)
    {
        var entity = registry.Get(entityName);
        if (entity is null) return NotFound();

        await DeleteAll(entity);
        TempData["Message"] = "All records deleted.";
        return RedirectToAction(nameof(Detail), new { entityName });
    }
}
