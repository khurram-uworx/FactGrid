using ClosedXML.Excel;
using FactGrid.Models;
using System.Reflection;

namespace FactGrid.Services;

public class ExcelTemplateGenerator
{
    readonly EntityRegistry registry;

    public ExcelTemplateGenerator(EntityRegistry registry)
    {
        this.registry = registry;
    }

    public string Generate(string entityName, string outputPath)
    {
        var entity = registry.Get(entityName)
            ?? throw new ArgumentException($"Unknown entity '{entityName}'");

        var columns = entity.ModelType.GetProperties()
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => (x.Attr!.Position, Attr: x.Attr))
            .OrderBy(x => x.Position)
            .ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(entity.DisplayName);

        foreach (var (pos, attr) in columns)
        {
            var colLetter = GetColumnLetter(pos);

            sheet.Cell($"{colLetter}1").Value = attr.Title;
            sheet.Cell($"{colLetter}1").Style.Font.Bold = true;

            if (attr.Example is not null)
            {
                var exampleCell = sheet.Cell($"{colLetter}2");
                SetTypedCellValue(exampleCell, attr.Example);
            }
        }

        sheet.Columns().AdjustToContents();

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    static void SetTypedCellValue(IXLCell cell, object value)
    {
        switch (value)
        {
            case string s:
                if (DateOnly.TryParse(s, out var dateOnly))
                    cell.Value = new DateTime(dateOnly.Year, dateOnly.Month, dateOnly.Day);
                else if (DateTime.TryParse(s, out var dt))
                    cell.Value = dt;
                else
                    cell.Value = s;
                break;
            case int i:
                cell.Value = i;
                break;
            case double d:
                cell.Value = d;
                break;
            case decimal m:
                cell.Value = (double)m;
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case DateOnly d:
                cell.Value = new DateTime(d.Year, d.Month, d.Day);
                break;
            default:
                cell.Value = value.ToString() ?? "";
                break;
        }
    }

    static string GetColumnLetter(int columnNumber)
    {
        var column = "";
        while (columnNumber > 0)
        {
            var mod = (columnNumber - 1) % 26;
            column = (char)('A' + mod) + column;
            columnNumber = (columnNumber - mod) / 26;
        }
        return column;
    }
}
