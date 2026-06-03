using ClosedXML.Excel;
using EfMcp.AspNet.Services;

namespace EfMcp.Tests;

public class ExcelUploadServiceTests
{
    static MemoryStream CreateExcel(Action<IXLWorksheet> populate)
    {
        var stream = new MemoryStream();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        populate(sheet);
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Test]
    public void Parse_ValidFile_ReturnsRecords()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(1, 1).Value = "Resource Display Name";
            sheet.Cell(1, 2).Value = "Project";
            sheet.Cell(1, 3).Value = "Description";
            sheet.Cell(1, 4).Value = "Work Date";
            sheet.Cell(1, 5).Value = "Person Hours";
            sheet.Cell(1, 6).Value = "Approval Workflow Status";

            sheet.Cell(2, 1).Value = "John Doe";
            sheet.Cell(2, 2).Value = "Project Alpha";
            sheet.Cell(2, 3).Value = "Some work";
            sheet.Cell(2, 4).Value = "12/25/2024 12:00:00 AM";
            sheet.Cell(2, 5).Value = "8.00";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(errors, Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(records[0].ResourceName, Is.EqualTo("John Doe"));
            Assert.That(records[0].Project, Is.EqualTo("Project Alpha"));
            Assert.That(records[0].Description, Is.EqualTo("Some work"));
            Assert.That(records[0].WorkDate, Is.EqualTo(new DateOnly(2024, 12, 25)));
            Assert.That(records[0].Hours, Is.EqualTo(8.00m));
            Assert.That(records[0].ApprovalStatus, Is.EqualTo("Approved"));
        });
    }

    [Test]
    public void Parse_HeaderRow_IsSkipped()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(1, 1).Value = "Resource\nDisplay\nName";
            sheet.Cell(1, 4).Value = "Work\nDate";

            sheet.Cell(2, 1).Value = "Alice";
            sheet.Cell(2, 2).Value = "Project B";
            sheet.Cell(2, 4).Value = "1/1/2025 12:00:00 AM";
            sheet.Cell(2, 5).Value = "6.5";
            sheet.Cell(2, 6).Value = "Pending";
        });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Parse_EmptyRows_Skipped()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(1, 1).Value = "Header";
            sheet.Cell(2, 1).Value = "";
            sheet.Cell(2, 2).Value = "";
            sheet.Cell(3, 1).Value = "Bob";
            sheet.Cell(3, 2).Value = "Project C";
            sheet.Cell(3, 4).Value = "2/15/2025 12:00:00 AM";
            sheet.Cell(3, 5).Value = "4";
            sheet.Cell(3, 6).Value = "Approved";
        });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_MalformedDate_ReportsError()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Charlie";
            sheet.Cell(2, 2).Value = "Project D";
            sheet.Cell(2, 4).Value = "not-a-date";
            sheet.Cell(2, 5).Value = "5";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("WorkDate"));
    }

    [Test]
    public void Parse_NonNumericHours_ReportsError()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Diana";
            sheet.Cell(2, 2).Value = "Project E";
            sheet.Cell(2, 4).Value = "3/10/2025 12:00:00 AM";
            sheet.Cell(2, 5).Value = "abc";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Hours"));
    }

    [Test]
    public void Parse_MissingRequiredFields_ReportsError()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "";
            sheet.Cell(2, 2).Value = "";
            sheet.Cell(2, 4).Value = "3/10/2025 12:00:00 AM";
            sheet.Cell(2, 5).Value = "5";
            sheet.Cell(2, 6).Value = "";
        });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_EmptySheet_ReturnsEmpty()
    {
        var stream = CreateExcel(_ => { });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Parse_MultipleRows_AllParsed()
    {
        var stream = CreateExcel(sheet =>
        {
            for (var i = 1; i <= 5; i++)
            {
                var row = i + 1;
                sheet.Cell(row, 1).Value = $"Person {i}";
                sheet.Cell(row, 2).Value = $"Project {i}";
                sheet.Cell(row, 4).Value = $"{i}/1/2025 12:00:00 AM";
                sheet.Cell(row, 5).Value = $"{i * 2}";
                sheet.Cell(row, 6).Value = "Approved";
            }
        });

        var service = new ExcelUploadService();
        var (records, errors) = service.Parse(stream);

        Assert.That(records, Has.Count.EqualTo(5));
        Assert.That(errors, Is.Empty);
    }
}
