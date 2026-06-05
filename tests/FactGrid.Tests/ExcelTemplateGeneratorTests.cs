using ClosedXML.Excel;
using FactGrid.Models;
using FactGrid.Services;
using System.Collections;

namespace FactGrid.Tests;

public class ExcelTemplateGeneratorTests
{
    static EntityRegistry CreateRegistry()
    {
        var r = new EntityRegistry();
        r.Register<Worklog>(new EntityRegistration(
            EntityName: "worklogs",
            DisplayName: "Worklogs",
            ModelType: typeof(Worklog),
            ExcelParserType: typeof(WorklogsExcelParser),
            TableName: "ResourceHours",
            Description: "Employee worklog entries"
        ));
        r.Register<Expense>(new EntityRegistration(
            EntityName: "expenses",
            DisplayName: "Expenses",
            ModelType: typeof(Expense),
            ExcelParserType: typeof(ExpensesExcelParser),
            TableName: "Expenses",
            Description: "Employee expense entries"
        ));
        return r;
    }

    [Test]
    public void Generate_WorklogTemplate_RoundTripsThroughParser()
    {
        var registry = CreateRegistry();
        var generator = new ExcelTemplateGenerator(registry);

        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            generator.Generate("worklogs", tempPath);

            Assert.That(File.Exists(tempPath), Is.True);
            var fileInfo = new FileInfo(tempPath);
            Assert.That(fileInfo.Length, Is.GreaterThan(0));

            var parser = new WorklogsExcelParser();
            using var stream = File.OpenRead(tempPath);
            var (records, errors) = parser.Parse(stream);

            Assert.That(errors, Is.Empty, $"Parsing generated template produced errors: {string.Join(", ", errors)}");
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].ResourceName, Is.EqualTo("John Doe"));
            Assert.That(records[0].Hours, Is.EqualTo(8.0m));
            Assert.That(records[0].WorkDate, Is.EqualTo(new DateOnly(2024, 12, 25)));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public void Generate_ExpenseTemplate_RoundTripsThroughParser()
    {
        var registry = CreateRegistry();
        var generator = new ExcelTemplateGenerator(registry);

        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            generator.Generate("expenses", tempPath);

            Assert.That(File.Exists(tempPath), Is.True);
            var fileInfo = new FileInfo(tempPath);
            Assert.That(fileInfo.Length, Is.GreaterThan(0));

            var parser = new ExpensesExcelParser();
            using var stream = File.OpenRead(tempPath);
            var (records, errors) = parser.Parse(stream);

            Assert.That(errors, Is.Empty, $"Parsing generated template produced errors: {string.Join(", ", errors)}");
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].ResourceName, Is.EqualTo("Jane Smith"));
            Assert.That(records[0].Amount, Is.EqualTo(450.00m));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public void Generate_UnknownEntity_Throws()
    {
        var registry = CreateRegistry();
        var generator = new ExcelTemplateGenerator(registry);

        Assert.Throws<ArgumentException>(() => generator.Generate("nonexistent", "test.xlsx"));
    }

    [Test]
    public void Generate_CreatesOutputDirectoryIfNotExists()
    {
        var registry = CreateRegistry();
        var generator = new ExcelTemplateGenerator(registry);

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var tempPath = Path.Combine(tempDir, "template.xlsx");
        try
        {
            generator.Generate("worklogs", tempPath);
            Assert.That(File.Exists(tempPath), Is.True);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir);
        }
    }

    [Test]
    public void Generate_OverwritesExistingFile()
    {
        var registry = CreateRegistry();
        var generator = new ExcelTemplateGenerator(registry);

        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            File.WriteAllText(tempPath, "old content");
            var beforeWrite = new FileInfo(tempPath).Length;

            generator.Generate("worklogs", tempPath);

            Assert.That(File.Exists(tempPath), Is.True);
            Assert.That(new FileInfo(tempPath).Length, Is.Not.EqualTo(beforeWrite));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public void Generate_WorklogTemplate_HasDateAndDecimalFormats()
    {
        var registry = CreateRegistry();
        var generator = new ExcelTemplateGenerator(registry);

        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            generator.Generate("worklogs", tempPath);

            using var workbook = new XLWorkbook(tempPath);
            var sheet = workbook.Worksheets.First();

            // WorkDate is column 4 (D), Hours is column 5 (E)
            var dateCell = sheet.Cell("D2");
            Assert.That(dateCell.Style.NumberFormat.Format, Is.EqualTo("yyyy-mm-dd"));

            var hoursCell = sheet.Cell("E2");
            Assert.That(hoursCell.Style.NumberFormat.Format, Is.EqualTo("0.00"));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public void Generate_ExpenseTemplate_HasDateAndDecimalFormats()
    {
        var registry = CreateRegistry();
        var generator = new ExcelTemplateGenerator(registry);

        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            generator.Generate("expenses", tempPath);

            using var workbook = new XLWorkbook(tempPath);
            var sheet = workbook.Worksheets.First();

            // Amount is column 4 (D), ExpenseDate is column 5 (E)
            var amountCell = sheet.Cell("D2");
            Assert.That(amountCell.Style.NumberFormat.Format, Is.EqualTo("0.00"));

            var dateCell = sheet.Cell("E2");
            Assert.That(dateCell.Style.NumberFormat.Format, Is.EqualTo("yyyy-mm-dd"));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    class CustomFormatEntity
    {
        public int Id { get; set; }
        [ExcelColumn(1, "Custom Date", Required = true, Example = "2025-06-01", Format = "dd/MM/yyyy")]
        public DateOnly CustomDate { get; set; }
        [ExcelColumn(2, "No Format Decimal", Required = true, Example = 42.5)]
        public decimal NoFormatDecimal { get; set; }
    }

    [Test]
    public void Generate_FormatAttribute_OverridesTypeBasedFallback()
    {
        var registry = new EntityRegistry();
        registry.Register<CustomFormatEntity>(new EntityRegistration(
            EntityName: "custom", DisplayName: "Custom", ModelType: typeof(CustomFormatEntity),
            ExcelParserType: typeof(CustomFormatParser), TableName: "Custom",
            Description: "Entity for format override test"
        ));

        var generator = new ExcelTemplateGenerator(registry);
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            generator.Generate("custom", tempPath);

            using var workbook = new XLWorkbook(tempPath);
            var sheet = workbook.Worksheets.First();

            // Column 1 — Format attribute overrides DateOnly fallback
            var dateCell = sheet.Cell("A2");
            Assert.That(dateCell.Style.NumberFormat.Format, Is.EqualTo("dd/MM/yyyy"));

            // Column 2 — no Format attribute, falls back to type-based "0.00"
            var decimalCell = sheet.Cell("B2");
            Assert.That(decimalCell.Style.NumberFormat.Format, Is.EqualTo("0.00"));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    class CustomFormatParser : IExcelParser<CustomFormatEntity>
    {
        public (List<CustomFormatEntity> Records, List<string> Errors) Parse(Stream excelStream) =>
            (new List<CustomFormatEntity>(), new List<string>());
        (IList Records, List<string> Errors) IExcelParser.Parse(Stream excelStream) =>
            (new List<CustomFormatEntity>(), new List<string>());
    }
}
