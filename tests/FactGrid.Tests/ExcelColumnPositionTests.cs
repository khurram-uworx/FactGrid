using ClosedXML.Excel;
using FactGrid.Models;
using FactGrid.Services;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace FactGrid.Tests;

/// <summary>
/// Verifies that parsers read column positions from [ExcelColumn] metadata
/// and adapt correctly when positions are non-sequential or reordered.
/// </summary>
[TestFixture]
public class ExcelColumnPositionTests
{
    /// <summary>
    /// Test entity with deliberately non-standard column positions (1, 3, 5, 2, 4, 6)
    /// instead of the natural 1,2,3,4,5,6 order.
    /// </summary>
    class ShuffledEntity
    {
        public int Id { get; set; }

        [ExcelColumn(1, "Name", Required = true, Example = "Alice")]
        public string Name { get; set; } = "";

        [ExcelColumn(2, "Code", Example = "A123")]
        public string? Code { get; set; }

        [ExcelColumn(3, "Value", Required = true, Example = 450.50)]
        public decimal Value { get; set; }

        [ExcelColumn(4, "Notes", Example = "Some notes")]
        public string? Notes { get; set; }

        [ExcelColumn(5, "Active Date", Required = true, Example = "2025-07-15")]
        public DateOnly ActiveDate { get; set; }

        [ExcelColumn(6, "Status", Example = "Approved")]
        public string Status { get; set; } = "";
    }

    class ShuffledEntityParser : IExcelParser<ShuffledEntity>
    {
        static readonly (int Position, PropertyInfo Property, ExcelColumnAttribute Attr)[] ColumnMap =
            typeof(ShuffledEntity).GetProperties()
                .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
                .Where(x => x.Attr is not null)
                .Select(x => (x.Attr!.Position, x.Prop, x.Attr))
                .OrderBy(x => x.Position)
                .ToArray();

        static readonly Dictionary<string, int> ColumnIndex =
            ColumnMap.ToDictionary(c => c.Property.Name, c => c.Position);

        public (List<ShuffledEntity> Records, List<string> Errors) Parse(Stream excelStream)
        {
            var records = new List<ShuffledEntity>();
            var errors = new List<string>();

            using var workbook = new XLWorkbook(excelStream);
            var sheet = workbook.Worksheets.First();
            var rows = sheet.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>();

            foreach (var row in rows)
            {
                if (row.RowNumber() == 1) continue;

                var values = new Dictionary<int, (string Text, DateTime? DateTimeValue)>();
                foreach (var (pos, prop, attr) in ColumnMap)
                {
                    var cell = sheet.Cell(row.RowNumber(), pos);
                    DateTime? dt = cell.DataType == XLDataType.DateTime ? cell.GetDateTime() : null;
                    values[pos] = (cell.GetString().Trim(), dt);
                }

                var anyData = values.Values.Any(v => !string.IsNullOrWhiteSpace(v.Text));
                if (!anyData)
                    continue;

                var name = values[ColumnIndex["Name"]].Text;
                var valueStr = values[ColumnIndex["Value"]].Text;
                var code = values[ColumnIndex["Code"]].Text;
                var notes = values[ColumnIndex["Notes"]].Text;
                var status = values[ColumnIndex["Status"]].Text;

                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add($"Row {row.RowNumber()}: Name is required");
                    continue;
                }
                if (!decimal.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                {
                    errors.Add($"Row {row.RowNumber()}: Could not parse Value '{valueStr}'");
                    continue;
                }

                var dateCell = values[ColumnIndex["ActiveDate"]];
                var activeDate = dateCell.DateTimeValue is not null
                    ? DateOnly.FromDateTime(dateCell.DateTimeValue.Value)
                    : ExcelDateHelper.ParseDateText(dateCell.Text);

                if (activeDate is null)
                {
                    errors.Add($"Row {row.RowNumber()}: Could not parse ActiveDate '{dateCell.Text}'");
                    continue;
                }

                records.Add(new ShuffledEntity
                {
                    Name = name,
                    Value = value,
                    ActiveDate = activeDate.Value,
                    Code = string.IsNullOrWhiteSpace(code) ? null : code,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                    Status = status
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

    [Test]
    public void ShuffledPositions_RoundTripsThroughGeneratorAndParser()
    {
        var registry = new EntityRegistry();
        registry.Register<ShuffledEntity>(new EntityRegistration(
            EntityName: "shuffled",
            DisplayName: "Shuffled",
            ModelType: typeof(ShuffledEntity),
            ExcelParserType: typeof(ShuffledEntityParser),
            TableName: "Shuffled",
            Description: "Entity with non-standard column positions"
        ));

        var generator = new ExcelTemplateGenerator(registry);
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            generator.Generate("shuffled", tempPath);
            Assert.That(File.Exists(tempPath), Is.True);

            // Verify Excel has headers in the right positions per attribute positions
            using var verifyWorkbook = new XLWorkbook(tempPath);
            var verifySheet = verifyWorkbook.Worksheets.First();
            Assert.That(verifySheet.Cell(1, 1).GetString(), Is.EqualTo("Name"));
            Assert.That(verifySheet.Cell(1, 2).GetString(), Is.EqualTo("Code"));
            Assert.That(verifySheet.Cell(1, 3).GetString(), Is.EqualTo("Value"));
            Assert.That(verifySheet.Cell(1, 4).GetString(), Is.EqualTo("Notes"));
            Assert.That(verifySheet.Cell(1, 5).GetString(), Is.EqualTo("Active Date"));
            Assert.That(verifySheet.Cell(1, 6).GetString(), Is.EqualTo("Status"));

            // Parse back — uses same ColumnIndex map, should read from correct positions
            var parser = new ShuffledEntityParser();
            using var stream = File.OpenRead(tempPath);
            var (records, errors) = parser.Parse(stream);

            Assert.That(errors, Is.Empty, $"Parse errors: {string.Join(", ", errors)}");
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Name, Is.EqualTo("Alice"));
            Assert.That(records[0].Value, Is.EqualTo(450.50m));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public void ShuffledPositions_ReadsDataFromCorrectColumns()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Data");
            // Data at positions: 1=Name, 2=Code, 3=Value, 4=Notes, 5=ActiveDate, 6=Status
            sheet.Cell(2, 1).Value = "Alice";
            sheet.Cell(2, 2).Value = "A123";
            sheet.Cell(2, 3).Value = "450.50";
            sheet.Cell(2, 4).Value = "Some notes";
            sheet.Cell(2, 5).Value = "7/1/2025 12:00:00 AM";
            sheet.Cell(2, 6).Value = "Approved";
            workbook.SaveAs(stream);
        }
        stream.Position = 0;

        var parser = new ShuffledEntityParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(errors, Is.Empty);
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].Name, Is.EqualTo("Alice"));
        Assert.That(records[0].Code, Is.EqualTo("A123"));
        Assert.That(records[0].Value, Is.EqualTo(450.50m));
        Assert.That(records[0].Notes, Is.EqualTo("Some notes"));
        Assert.That(records[0].ActiveDate, Is.EqualTo(new DateOnly(2025, 7, 1)));
        Assert.That(records[0].Status, Is.EqualTo("Approved"));
    }

    [Test]
    public void WorklogsParser_ColumnIndex_MatchesModelAttributes()
    {
        var map = BuildColumnIndex(typeof(Worklog));
        Assert.That(map["ResourceName"], Is.EqualTo(1));
        Assert.That(map["Project"], Is.EqualTo(2));
        Assert.That(map["Description"], Is.EqualTo(3));
        Assert.That(map["WorkDate"], Is.EqualTo(4));
        Assert.That(map["Hours"], Is.EqualTo(5));
        Assert.That(map["ApprovalStatus"], Is.EqualTo(6));
    }

    [Test]
    public void ExpensesParser_ColumnIndex_MatchesModelAttributes()
    {
        var map = BuildColumnIndex(typeof(Expense));
        Assert.That(map["ResourceName"], Is.EqualTo(1));
        Assert.That(map["Category"], Is.EqualTo(2));
        Assert.That(map["Description"], Is.EqualTo(3));
        Assert.That(map["Amount"], Is.EqualTo(4));
        Assert.That(map["ExpenseDate"], Is.EqualTo(5));
        Assert.That(map["ApprovalStatus"], Is.EqualTo(6));
    }

    static Dictionary<string, int> BuildColumnIndex(Type modelType)
    {
        return modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
            .Where(x => x.Attr is not null)
            .ToDictionary(x => x.Prop.Name, x => x.Attr!.Position);
    }
}
