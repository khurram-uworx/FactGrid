using System.Globalization;
using ClosedXML.Excel;
using EfMcp.AspNet.Models;

namespace EfMcp.AspNet.Services;

public class WorklogsExcelParser : IExcelParser<Worklogs>
{
    const string DateFormat = "d-MMM-yyyy";

    public (List<Worklogs> Records, List<string> Errors) Parse(Stream excelStream)
    {
        var records = new List<Worklogs>();
        var errors = new List<string>();

        using var workbook = new XLWorkbook(excelStream);
        var sheet = workbook.Worksheets.First();
        var rows = sheet.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>();

        foreach (var row in rows)
        {
            if (row.RowNumber() == 1) continue;

            var resourceName = row.Cell(1).GetString().Trim();
            var project = row.Cell(2).GetString().Trim();
            var description = row.Cell(3).GetString().Trim();
            var workDateStr = row.Cell(4).GetString().Trim();
            var hoursStr = row.Cell(5).GetString().Trim();
            var approvalStatus = row.Cell(6).GetString().Trim();

            if (string.IsNullOrWhiteSpace(resourceName))
                continue;

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                errors.Add($"Row {row.RowNumber()}: ResourceName is required");
                continue;
            }

            if (!DateTime.TryParseExact(workDateStr, "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var workDateTime))
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse WorkDate '{workDateStr}'");
                continue;
            }

            var workDate = DateOnly.FromDateTime(workDateTime);

            if (!decimal.TryParse(hoursStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours))
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse Hours '{hoursStr}'");
                continue;
            }

            records.Add(new Worklogs
            {
                ResourceName = resourceName,
                Project = string.IsNullOrWhiteSpace(project) ? null : project,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                WorkDate = workDate,
                Hours = hours,
                ApprovalStatus = approvalStatus
            });
        }

        return (records, errors);
    }
}
