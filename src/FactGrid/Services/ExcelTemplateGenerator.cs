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

        var columns = ExcelColumnMetadata.GetColumns(entity.ModelType);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(entity.DisplayName);

        foreach (var col in columns)
        {
            var colLetter = GetColumnLetter(col.Position);

            sheet.Cell($"{colLetter}1").Value = col.Attr.Title;
            sheet.Cell($"{colLetter}1").Style.Font.Bold = true;

            if (col.Attr.Example is not null)
            {
                var exampleCell = sheet.Cell($"{colLetter}2");
                SetTypedCellValue(exampleCell, col.Attr.Example);
                if (!string.IsNullOrEmpty(col.Attr.Format))
                    exampleCell.Style.NumberFormat.Format = col.Attr.Format;
                else
                    ApplyNumberFormat(exampleCell, col.Property.PropertyType);
            }
        }

        sheet.Columns().AdjustToContents();

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        workbook.SaveAs(outputPath);
        return outputPath;
    }

    static void ApplyNumberFormat(IXLCell cell, Type propertyType)
    {
        if (propertyType == typeof(DateOnly) || propertyType == typeof(DateTime))
            cell.Style.NumberFormat.Format = "yyyy-mm-dd";
        else if (propertyType == typeof(decimal) || propertyType == typeof(double) || propertyType == typeof(float))
            cell.Style.NumberFormat.Format = "0.00";
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
