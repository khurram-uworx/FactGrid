using System.Collections;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using FactGrid.AspNet.Models;
using System.Globalization;

namespace FactGrid.AspNet.Services;

public class WorklogsExcelParser : IExcelParser<Worklog>
{
    const string DateFormat = "d-MMM-yyyy";

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
            var r = row.RowNumber();

            DateTime? workDateTime = null;
            var cell = sheet.Cell(r, 4);
            if (cell.DataType == XLDataType.DateTime)
                workDateTime = cell.GetDateTime();

            var resourceName = sheet.Cell(r, 1).GetString().Trim();
            var project = sheet.Cell(r, 2).GetString().Trim();
            var description = sheet.Cell(r, 3).GetString().Trim();
            var workDateStr = sheet.Cell(r, 4).GetString().Trim();
            var hoursStr = sheet.Cell(r, 5).GetString().Trim();
            var approvalStatus = sheet.Cell(r, 6).GetString().Trim();

            var anyData = !string.IsNullOrWhiteSpace(resourceName)
                || !string.IsNullOrWhiteSpace(project)
                || !string.IsNullOrWhiteSpace(description)
                || !string.IsNullOrWhiteSpace(workDateStr)
                || !string.IsNullOrWhiteSpace(hoursStr)
                || !string.IsNullOrWhiteSpace(approvalStatus);

            if (!anyData)
                continue;

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                errors.Add($"Row {row.RowNumber()}: ResourceName is required");
                continue;
            }

            if (workDateTime is null)
            {
                if (!DateTime.TryParseExact(workDateStr, "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedWorkDateTime))
                {
                    errors.Add($"Row {row.RowNumber()}: Could not parse WorkDate '{workDateStr}'");
                    continue;
                }

                workDateTime = parsedWorkDateTime;
            }

            var workDate = DateOnly.FromDateTime(workDateTime.Value);

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
                WorkDate = workDate,
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
