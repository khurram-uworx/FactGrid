using ClosedXML.Excel;
using FactGrid.Models;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace FactGrid.Services;

public class ExpensesExcelParser : IExcelParser<Expense>
{
    static readonly IReadOnlyList<ExcelColumnMetadata.ColumnInfo> ColumnMap =
        ExcelColumnMetadata.GetColumns(typeof(Expense));

    static readonly IReadOnlyDictionary<string, int> ColumnIndex =
        ExcelColumnMetadata.GetColumnIndex(typeof(Expense));

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
            foreach (var col in ColumnMap)
            {
                var cell = sheet.Cell(row.RowNumber(), col.Position);
                DateTime? dt = null;
                if (cell.DataType == XLDataType.DateTime)
                    dt = cell.GetDateTime();
                values[col.Position] = (cell.GetString().Trim(), cell.DataType, dt);
            }

            var anyData = values.Values.Any(v => !string.IsNullOrWhiteSpace(v.Text));
            if (!anyData)
                continue;

            var cellTexts = ColumnMap.ToDictionary(c => c.Property.Name, c => values[c.Position].Text);
            var requiredErrors = ExcelColumnMetadata.ValidateRequired(typeof(Expense), cellTexts, row.RowNumber());
            if (requiredErrors.Count > 0)
            {
                errors.AddRange(requiredErrors);
                continue;
            }

            var resourceName = values[ColumnIndex["ResourceName"]].Text;
            var category = values[ColumnIndex["Category"]].Text;
            var description = values[ColumnIndex["Description"]].Text;
            var amountStr = values[ColumnIndex["Amount"]].Text;
            var approvalStatus = values[ColumnIndex["ApprovalStatus"]].Text;

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
