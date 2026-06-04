using ClosedXML.Excel;
using FactGrid.Services;

namespace FactGrid.Tests;

public class ExpenseReportParserTests
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
            sheet.Cell(1, 1).Value = "Resource";
            sheet.Cell(1, 2).Value = "Category";
            sheet.Cell(1, 3).Value = "Description";
            sheet.Cell(1, 4).Value = "Amount";
            sheet.Cell(1, 5).Value = "Expense Date";
            sheet.Cell(1, 6).Value = "Status";

            sheet.Cell(2, 1).Value = "Alice";
            sheet.Cell(2, 2).Value = "Travel";
            sheet.Cell(2, 3).Value = "Conference flight";
            sheet.Cell(2, 4).Value = "450.00";
            sheet.Cell(2, 5).Value = "3/15/2025 12:00:00 AM";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(errors, Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(records[0].ResourceName, Is.EqualTo("Alice"));
            Assert.That(records[0].Category, Is.EqualTo("Travel"));
            Assert.That(records[0].Description, Is.EqualTo("Conference flight"));
            Assert.That(records[0].Amount, Is.EqualTo(450.00m));
            Assert.That(records[0].ExpenseDate, Is.EqualTo(new DateOnly(2025, 3, 15)));
            Assert.That(records[0].ApprovalStatus, Is.EqualTo("Approved"));
        });
    }

    [Test]
    public void Parse_HeaderRow_IsSkipped()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(1, 1).Value = "Resource";
            sheet.Cell(2, 1).Value = "Bob";
            sheet.Cell(2, 2).Value = "Meals";
            sheet.Cell(2, 4).Value = "35.50";
            sheet.Cell(2, 5).Value = "4/1/2025 12:00:00 AM";
            sheet.Cell(2, 6).Value = "Pending";
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Parse_BlankRow_ReturnsNoRecordsNoErrors()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "";
            sheet.Cell(2, 2).Value = "";
            sheet.Cell(2, 3).Value = "";
            sheet.Cell(2, 4).Value = "";
            sheet.Cell(2, 5).Value = "";
            sheet.Cell(2, 6).Value = "";
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Parse_MissingResourceName_ReportsError()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "";
            sheet.Cell(2, 2).Value = "Office Supplies";
            sheet.Cell(2, 4).Value = "120.00";
            sheet.Cell(2, 5).Value = "5/10/2025 12:00:00 AM";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("ResourceName"));
    }

    [Test]
    public void Parse_NonNumericAmount_ReportsError()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Charlie";
            sheet.Cell(2, 2).Value = "Travel";
            sheet.Cell(2, 4).Value = "not-a-number";
            sheet.Cell(2, 5).Value = "6/1/2025 12:00:00 AM";
            sheet.Cell(2, 6).Value = "Pending";
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("Amount"));
    }

    [Test]
    public void Parse_MalformedExpenseDate_ReportsError()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Diana";
            sheet.Cell(2, 2).Value = "Meals";
            sheet.Cell(2, 4).Value = "25.00";
            sheet.Cell(2, 5).Value = "bad-date";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("ExpenseDate"));
    }

    [Test]
    public void Parse_TypedDateTimeCell_ParsesCorrectly()
    {
        var stream = CreateExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Bob";
            sheet.Cell(2, 2).Value = "Travel";
            sheet.Cell(2, 4).Value = "250.00";
            sheet.Cell(2, 5).Value = new DateTime(2025, 7, 15);
            sheet.Cell(2, 6).Value = "Pending";
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(errors, Is.Empty);
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].ExpenseDate, Is.EqualTo(new DateOnly(2025, 7, 15)));
    }

    [Test]
    public void Parse_EmptySheet_ReturnsEmpty()
    {
        var stream = CreateExcel(_ => { });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Is.Empty);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Parse_MultipleRows_AllParsed()
    {
        var stream = CreateExcel(sheet =>
        {
            for (var i = 1; i <= 3; i++)
            {
                var row = i + 1;
                sheet.Cell(row, 1).Value = $"Person {i}";
                sheet.Cell(row, 2).Value = $"Category {i}";
                sheet.Cell(row, 4).Value = $"{i * 100}.00";
                sheet.Cell(row, 5).Value = $"{i}/1/2025 12:00:00 AM";
                sheet.Cell(row, 6).Value = "Approved";
            }
        });

        var parser = new ExpensesExcelParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(records, Has.Count.EqualTo(3));
        Assert.That(errors, Is.Empty);
    }
}
