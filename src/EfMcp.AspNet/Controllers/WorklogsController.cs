using EfMcp.AspNet.Data;
using EfMcp.AspNet.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EfMcp.AspNet.Controllers;

public class WorklogsController : Controller
{
    readonly ApplicationDbContext db;
    readonly ExcelUploadService excel;

    public WorklogsController(ApplicationDbContext db, ExcelUploadService excel)
    {
        this.db = db;
        this.excel = excel;
    }

    public async Task<IActionResult> Index()
    {
        var count = await db.Worklogs.CountAsync();
        return View(count);
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a file.";
            return RedirectToAction(nameof(Index));
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Only .xlsx files are supported.";
            return RedirectToAction(nameof(Index));
        }

        List<Models.Worklogs> records;
        List<string> errors;

        using (var stream = file.OpenReadStream())
        {
            (records, errors) = excel.Parse(stream);
        }

        if (records.Count > 0)
        {
            db.Worklogs.AddRange(records);
            await db.SaveChangesAsync();
        }

        var message = $"Inserted {records.Count} records.";
        if (errors.Count > 0)
            message += $" Errors: {string.Join("; ", errors)}";

        TempData["Message"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAll()
    {
        await db.Worklogs.ExecuteDeleteAsync();
        TempData["Message"] = "All records deleted.";
        return RedirectToAction(nameof(Index));
    }
}
