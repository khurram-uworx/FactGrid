using ClosedXML.Excel;
using EfMcp.AspNet.Models;

namespace EfMcp.AspNet.Services;

public class ExcelUploadService
{
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

            if (string.IsNullOrWhiteSpace(resourceName) && string.IsNullOrWhiteSpace(project))
                continue;

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                errors.Add($"Row {row.RowNumber()}: ResourceName is required");
                continue;
            }

            if (string.IsNullOrWhiteSpace(project))
            {
                errors.Add($"Row {row.RowNumber()}: Project is required");
                continue;
            }

            if (string.IsNullOrWhiteSpace(approvalStatus))
            {
                errors.Add($"Row {row.RowNumber()}: ApprovalStatus is required");
                continue;
            }

            if (!DateOnly.TryParse(workDateStr, out var workDate))
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse WorkDate '{workDateStr}'");
                continue;
            }

            if (!decimal.TryParse(hoursStr, out var hours))
            {
                errors.Add($"Row {row.RowNumber()}: Could not parse Hours '{hoursStr}'");
                continue;
            }

            records.Add(new Worklogs
            {
                ResourceName = resourceName,
                Project = project,
                Description = string.IsNullOrWhiteSpace(description) ? null : description,
                WorkDate = workDate.ToDateTime(TimeOnly.MinValue),
                Hours = hours,
                ApprovalStatus = approvalStatus
            });
        }

        return (records, errors);
    }
}
