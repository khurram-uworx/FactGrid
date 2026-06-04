using FactGrid.Models;
using FactGrid.Services;

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
}
