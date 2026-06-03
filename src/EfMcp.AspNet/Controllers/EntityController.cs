using System.Collections;
using System.Data;
using System.Data.Common;
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

    async Task<long> countRows(EntityRegistration entity)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM " + entity.TableName;
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    object? resolveParser(Type parserType, Type modelType)
    {
        var interfaceType = typeof(IExcelParser<>).MakeGenericType(modelType);
        return serviceProvider.GetService(interfaceType);
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

        var parser = resolveParser(entity.ExcelParserType, entity.ModelType);
        if (parser is null)
        {
            TempData["Error"] = $"No Excel parser registered for entity '{entity.DisplayName}'.";
            return RedirectToAction(nameof(Detail), new { entityName });
        }

        var parserType = typeof(IExcelParser<>).MakeGenericType(entity.ModelType);
        var parseMethod = parserType.GetMethod("Parse")!;

        using var stream = file.OpenReadStream();
        dynamic result = parseMethod.Invoke(parser, [stream])!;
        IList records = (IList)result.Item1;
        var errors = (List<string>)result.Item2;

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

#pragma warning disable EF1003 // table name is from controlled registry, not user input
        await db.Database.ExecuteSqlRawAsync("DELETE FROM " + entity.TableName);
#pragma warning restore EF1003
        TempData["Message"] = "All records deleted.";
        return RedirectToAction(nameof(Detail), new { entityName });
    }
}
