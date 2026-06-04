using ClosedXML.Excel;
using FactGrid.Models;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace FactGrid.Services;

public class WorklogsExcelParser : IExcelParser<Worklog>
{
    static readonly (int Position, PropertyInfo Property, ExcelColumnAttribute Attr)[] ColumnMap =
        typeof(Worklog).GetProperties()
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => (x.Attr!.Position, x.Prop, x.Attr))
            .OrderBy(x => x.Position)
            .ToArray();

    static readonly Dictionary<string, int> ColumnIndex =
        ColumnMap.ToDictionary(c => c.Property.Name, c => c.Position);

    public (List<Worklog> Records, List<string> Errors) Parse(Stream excelStream)
    {
        var records = new List<Worklog>();
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
            var project = values[ColumnIndex["Project"]].Text;
            var description = values[ColumnIndex["Description"]].Text;
            var hoursStr = values[ColumnIndex["Hours"]].Text;
            var approvalStatus = values[ColumnIndex["ApprovalStatus"]].Text;

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                errors.Add($"Row {row.RowNumber()}: ResourceName is required");
                continue;
            }

            var workDateCell = values[ColumnIndex["WorkDate"]];
            var workDate = workDateCell.DateTimeValue is not null
                ? DateOnly.FromDateTime(workDateCell.DateTimeValue.Value)
                : ExcelDateHelper.ParseDateText(workDateCell.Text);

            if (workDate is null)
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse WorkDate '{workDateCell.Text}'");
                continue;
            }

            if (!decimal.TryParse(hoursStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours))
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse Hours '{hoursStr}'");
                continue;
            }

            records.Add(new Worklog
            {
                ResourceName = resourceName,
                Project = string.IsNullOrWhiteSpace(project) ? null : project,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                WorkDate = workDate.Value,
                Hours = hours,
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
