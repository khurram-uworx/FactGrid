using ClosedXML.Excel;
using FactGrid.Models;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace FactGrid.Services;

public class ExpensesExcelParser : IExcelParser<Expense>
{
    static readonly (int Position, PropertyInfo Property, ExcelColumnAttribute Attr)[] ColumnMap =
        typeof(Expense).GetProperties()
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => (x.Attr!.Position, x.Prop, x.Attr))
            .OrderBy(x => x.Position)
            .ToArray();

    static readonly Dictionary<string, int> ColumnIndex =
        ColumnMap.ToDictionary(c => c.Property.Name, c => c.Position);

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

            var values = new Dictionary<int, (string Text, XLDataType DataType, DateTime? DateTimeValue)>();
            foreach (var (pos, prop, attr) in ColumnMap)
            {
                var cell = sheet.Cell(row.RowNumber(), pos);
                DateTime? dt = null;
                if (cell.DataType == XLDataType.DateTime)
                    dt = cell.GetDateTime();
                values[pos] = (cell.GetString().Trim(), cell.DataType, dt);
            }

            var anyData = values.Values.Any(v => !string.IsNullOrWhiteSpace(v.Text));
            if (!anyData)
                continue;

            var resourceName = values[ColumnIndex["ResourceName"]].Text;
            var category = values[ColumnIndex["Category"]].Text;
            var description = values[ColumnIndex["Description"]].Text;
            var amountStr = values[ColumnIndex["Amount"]].Text;
            var approvalStatus = values[ColumnIndex["ApprovalStatus"]].Text;

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

            var expenseDateCell = values[ColumnIndex["ExpenseDate"]];
            var expenseDate = expenseDateCell.DateTimeValue is not null
                ? DateOnly.FromDateTime(expenseDateCell.DateTimeValue.Value)
                : ExcelDateHelper.ParseDateText(expenseDateCell.Text);

            if (expenseDate is null)
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse ExpenseDate '{expenseDateCell.Text}'");
                continue;
            }

            records.Add(new Expense
            {
                ResourceName = resourceName,
                Category = string.IsNullOrWhiteSpace(category) ? null : category,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                Amount = amount,
                ExpenseDate = expenseDate.Value,
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
