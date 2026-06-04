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
    /// Test entity where C# property declaration order differs from Excel column
    /// position order: Value (declared 2nd) has position 3, Code (declared 3rd) has
    /// position 2. The parser and generator must use position metadata, not declaration
    /// order.
    /// </summary>
    class ReorderedEntity
    {
        public int Id { get; set; }

        [ExcelColumn(1, "Name", Required = true, Example = "Alice")]
        public string Name { get; set; } = "";

        // declared 2nd but appears in column 3
        [ExcelColumn(3, "Value", Required = true, Example = 450.50)]
        public decimal Value { get; set; }

        // declared 3rd but appears in column 2
        [ExcelColumn(2, "Code", Example = "A123")]
        public string? Code { get; set; }

        [ExcelColumn(4, "Notes", Example = "Some notes")]
        public string? Notes { get; set; }

        [ExcelColumn(5, "Active Date", Required = true, Example = "2025-07-15")]
        public DateOnly ActiveDate { get; set; }

        [ExcelColumn(6, "Status", Example = "Approved")]
        public string Status { get; set; } = "";
    }

    class ReorderedEntityParser : IExcelParser<ReorderedEntity>
    {
        public (List<ReorderedEntity> Records, List<string> Errors) Parse(Stream excelStream)
        {
            var records = new List<ReorderedEntity>();
            var errors = new List<string>();
            var columnIndex = ExcelColumnMetadata.GetColumnIndex(typeof(ReorderedEntity));

            using var workbook = new XLWorkbook(excelStream);
            var sheet = workbook.Worksheets.First();
            var rows = sheet.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>();

            foreach (var row in rows)
            {
                if (row.RowNumber() == 1) continue;

                var nameCell = sheet.Cell(row.RowNumber(), columnIndex["Name"]);
                var codeCell = sheet.Cell(row.RowNumber(), columnIndex["Code"]);
                var valueCell = sheet.Cell(row.RowNumber(), columnIndex["Value"]);
                var notesCell = sheet.Cell(row.RowNumber(), columnIndex["Notes"]);
                var dateCell = sheet.Cell(row.RowNumber(), columnIndex["ActiveDate"]);
                var statusCell = sheet.Cell(row.RowNumber(), columnIndex["Status"]);

                var name = nameCell.GetString().Trim();
                var code = codeCell.GetString().Trim();
                var valueStr = valueCell.GetString().Trim();
                var notes = notesCell.GetString().Trim();
                var status = statusCell.GetString().Trim();

                var anyData = new[] { name, code, valueStr, notes, status }.Any(v => v.Length > 0);
                if (!anyData)
                    continue;

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

                var dt = dateCell.DataType == XLDataType.DateTime
                    ? DateOnly.FromDateTime(dateCell.GetDateTime())
                    : ExcelDateHelper.ParseDateText(dateCell.GetString().Trim());

                if (dt is null)
                {
                    errors.Add($"Row {row.RowNumber()}: Could not parse ActiveDate");
                    continue;
                }

                records.Add(new ReorderedEntity
                {
                    Name = name,
                    Value = value,
                    ActiveDate = dt.Value,
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
    public void ReorderedPositions_RoundTripsThroughGeneratorAndParser()
    {
        var registry = new EntityRegistry();
        registry.Register<ReorderedEntity>(new EntityRegistration(
            EntityName: "reordered",
            DisplayName: "Reordered",
            ModelType: typeof(ReorderedEntity),
            ExcelParserType: typeof(ReorderedEntityParser),
            TableName: "Reordered",
            Description: "Entity where declaration order differs from position order"
        ));

        var generator = new ExcelTemplateGenerator(registry);
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            generator.Generate("reordered", tempPath);
            Assert.That(File.Exists(tempPath), Is.True);

            // Positions dictate column order, not declaration order.
            using var verifyWorkbook = new XLWorkbook(tempPath);
            var verifySheet = verifyWorkbook.Worksheets.First();
            Assert.That(verifySheet.Cell(1, 1).GetString(), Is.EqualTo("Name"));
            Assert.That(verifySheet.Cell(1, 2).GetString(), Is.EqualTo("Code"));
            Assert.That(verifySheet.Cell(1, 3).GetString(), Is.EqualTo("Value"));
            Assert.That(verifySheet.Cell(1, 4).GetString(), Is.EqualTo("Notes"));
            Assert.That(verifySheet.Cell(1, 5).GetString(), Is.EqualTo("Active Date"));
            Assert.That(verifySheet.Cell(1, 6).GetString(), Is.EqualTo("Status"));

            // Parse back — reads by position, should match regardless of declaration order
            var parser = new ReorderedEntityParser();
            using var stream = File.OpenRead(tempPath);
            var (records, errors) = parser.Parse(stream);

            Assert.That(errors, Is.Empty, $"Parse errors: {string.Join(", ", errors)}");
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Name, Is.EqualTo("Alice"));
            // Template generator puts the example value "A123" in the Code cell
            Assert.That(records[0].Code, Is.EqualTo("A123"));
            Assert.That(records[0].Value, Is.EqualTo(450.50m));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public void ReorderedPositions_ReadsDataFromCorrectColumns()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Data");
            // Excel positions: 1=Name, 2=Code, 3=Value, 4=Notes, 5=ActiveDate, 6=Status
            sheet.Cell(2, 1).Value = "Alice";
            sheet.Cell(2, 2).Value = "A123";
            sheet.Cell(2, 3).Value = "450.50";
            sheet.Cell(2, 4).Value = "Some notes";
            sheet.Cell(2, 5).Value = "7/1/2025 12:00:00 AM";
            sheet.Cell(2, 6).Value = "Approved";
            workbook.SaveAs(stream);
        }
        stream.Position = 0;

        var parser = new ReorderedEntityParser();
        var (records, errors) = parser.Parse(stream);

        Assert.That(errors, Is.Empty);
        Assert.That(records, Has.Count.EqualTo(1));
        // Code at position 2 read correctly even though it's declared 3rd
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

[TestFixture]
public class ExcelColumnMetadataValidationTests
{
    class ValidEntity
    {
        public int Id { get; set; }
        [ExcelColumn(1, "Name")] public string Name { get; set; } = "";
        [ExcelColumn(2, "Value")] public decimal Value { get; set; }
    }

    class DuplicatePositionEntity
    {
        [ExcelColumn(1, "A")] public string A { get; set; } = "";
        [ExcelColumn(1, "B")] public string B { get; set; } = "";
    }

    class NonPositiveEntity
    {
        [ExcelColumn(0, "A")] public string A { get; set; } = "";
    }

    class NegativePositionEntity
    {
        [ExcelColumn(-1, "A")] public string A { get; set; } = "";
    }

    [Test]
    public void GetColumns_ValidEntity_ReturnsOrdered()
    {
        var cols = ExcelColumnMetadata.GetColumns(typeof(ValidEntity));
        Assert.That(cols, Has.Count.EqualTo(2));
        Assert.That(cols[0].Position, Is.EqualTo(1));
        Assert.That(cols[1].Position, Is.EqualTo(2));
        Assert.That(cols[0].Property.Name, Is.EqualTo("Name"));
    }

    [Test]
    public void Validate_ValidEntity_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => ExcelColumnMetadata.Validate(typeof(ValidEntity)));
    }

    [Test]
    public void Validate_DuplicatePositions_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExcelColumnMetadata.Validate(typeof(DuplicatePositionEntity)));
        Assert.That(ex!.Message, Does.Contain("Duplicate"));
        Assert.That(ex.Message, Does.Contain("position 1"));
    }

    [Test]
    public void Validate_ZeroPosition_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExcelColumnMetadata.Validate(typeof(NonPositiveEntity)));
        Assert.That(ex!.Message, Does.Contain("must be positive"));
    }

    [Test]
    public void Validate_NegativePosition_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExcelColumnMetadata.Validate(typeof(NegativePositionEntity)));
        Assert.That(ex!.Message, Does.Contain("must be positive"));
    }

    [Test]
    public void ValidateRequired_EmptyRequiredField_ReportsError()
    {
        var cellTexts = new Dictionary<string, string>
        {
            ["ResourceName"] = "",
            ["Project"] = "Alpha",
            ["WorkDate"] = "1/1/2025",
            ["Hours"] = "8",
            ["ApprovalStatus"] = "Approved"
        };

        var errors = ExcelColumnMetadata.ValidateRequired(typeof(Worklog), cellTexts, 2);
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0], Does.Contain("ResourceName"));
    }

    [Test]
    public void ValidateRequired_PopulatedRequiredField_NoError()
    {
        var cellTexts = new Dictionary<string, string>
        {
            ["ResourceName"] = "Alice",
            ["Project"] = "Alpha",
            ["WorkDate"] = "1/1/2025",
            ["Hours"] = "8",
            ["ApprovalStatus"] = "Approved"
        };

        var errors = ExcelColumnMetadata.ValidateRequired(typeof(Worklog), cellTexts, 2);
        Assert.That(errors, Is.Empty);
    }
}
