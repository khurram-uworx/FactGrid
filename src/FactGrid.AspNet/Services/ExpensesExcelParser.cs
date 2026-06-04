using ClosedXML.Excel;
using FactGrid.AspNet.Models;
using System.Collections;
using System.Globalization;

namespace FactGrid.AspNet.Services;

public class ExpensesExcelParser : IExcelParser<Expense>
{
    public (List<Expense> Records, List<string> Errors) Parse(Stream excelStream)
    {
        var records = new List<Expense>();
        var errors = new List<string>();

        using var workbook = new XLWorkbook(excelStream);
        var sheet = workbook.Worksheets.First();
        var rows = sheet.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>();

        foreach (var row in rows)
        {
            if (row.RowNumber() == 1) continue;
            var r = row.RowNumber();

            var resourceName = sheet.Cell(r, 1).GetString().Trim();
            var category = sheet.Cell(r, 2).GetString().Trim();
            var description = sheet.Cell(r, 3).GetString().Trim();
            var amountStr = sheet.Cell(r, 4).GetString().Trim();
            var expenseDateStr = sheet.Cell(r, 5).GetString().Trim();
            var approvalStatus = sheet.Cell(r, 6).GetString().Trim();

            var anyData = !string.IsNullOrWhiteSpace(resourceName)
                || !string.IsNullOrWhiteSpace(category)
                || !string.IsNullOrWhiteSpace(description)
                || !string.IsNullOrWhiteSpace(amountStr)
                || !string.IsNullOrWhiteSpace(expenseDateStr)
                || !string.IsNullOrWhiteSpace(approvalStatus);

            if (!anyData)
                continue;

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                errors.Add($"Row {row.RowNumber()}: ResourceName is required");
                continue;
            }

            if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse Amount '{amountStr}'");
                continue;
            }

            if (!DateTime.TryParseExact(expenseDateStr, "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expenseDateTime))
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse ExpenseDate '{expenseDateStr}'");
                continue;
            }

            var expenseDate = DateOnly.FromDateTime(expenseDateTime);

            records.Add(new Expense
            {
                ResourceName = resourceName,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                Amount = amount,
                ExpenseDate = expenseDate,
                ApprovalStatus = approvalStatus
            });
        }

        return (records, errors);
    }

    (IList Records, List<string> Errors) IExcelParser.Parse(Stream excelStream)
    {
        var (records, errors) = Parse(excelStream);
        return (records, errors);
    }
}
