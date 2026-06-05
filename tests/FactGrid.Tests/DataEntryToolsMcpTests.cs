using ClosedXML.Excel;
using FactGrid.Mcp.Tools;
using FactGrid.Models;
using FactGrid.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FactGrid.Tests;

[TestFixture]
public class DataEntryToolsMcpTests
{
    static EntityRegistry _registry = null!;
    static ExcelTemplateGenerator _generator = null!;
    static ServiceProvider _serviceProvider = null!;
    static MockHttpClientFactory _httpFactory = null!;

    class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? SendAsyncFunc { get; set; }
        public HttpRequestMessage? LastRequest { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (SendAsyncFunc is not null)
                return Task.FromResult(SendAsyncFunc(request));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new IngestionResult { Success = true, InsertedCount = 5, Errors = [] }), Encoding.UTF8, "application/json")
            });
        }
    }

    class MockHttpClientFactory : IHttpClientFactory
    {
        public MockHttpMessageHandler Handler { get; } = new();
        public HttpClient CreateClient(string name) => new(Handler);
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _registry = new EntityRegistry();
        _registry.Register<Worklog>(new EntityRegistration(
            EntityName: "worklogs",
            DisplayName: "Worklogs",
            ModelType: typeof(Worklog),
            ExcelParserType: typeof(WorklogsExcelParser),
            TableName: "ResourceHours",
            Description: "Employee worklog entries"
        ));
        _registry.Register<Expense>(new EntityRegistration(
            EntityName: "expenses",
            DisplayName: "Expenses",
            ModelType: typeof(Expense),
            ExcelParserType: typeof(ExpensesExcelParser),
            TableName: "Expenses",
            Description: "Employee expense entries"
        ));

        _generator = new ExcelTemplateGenerator(_registry);

        var services = new ServiceCollection();
        services.AddScoped<IExcelParser<Worklog>, WorklogsExcelParser>();
        services.AddScoped<IExcelParser<Expense>, ExpensesExcelParser>();
        _serviceProvider = services.BuildServiceProvider();

        _httpFactory = new MockHttpClientFactory();
    }

    [SetUp]
    public void SetUp()
    {
        _httpFactory.Handler.SendAsyncFunc = null;
        _httpFactory.Handler.LastRequest = null;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _serviceProvider.Dispose();

    DataEntryTools CreateTools() => new(_registry, _generator, _serviceProvider, _httpFactory);

    static MemoryStream CreateWorklogExcel(params Action<IXLWorksheet>[] rowActions)
    {
        var stream = new MemoryStream();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        sheet.Cell(1, 1).Value = "Resource Display Name";
        sheet.Cell(1, 2).Value = "Project";
        sheet.Cell(1, 3).Value = "Description";
        sheet.Cell(1, 4).Value = "Work Date";
        sheet.Cell(1, 5).Value = "Person Hours";
        sheet.Cell(1, 6).Value = "Approval Workflow Status";
        foreach (var action in rowActions)
            action(sheet);
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    // ── generate_template ──────────────────────────────────────────

    [Test]
    public void GenerateTemplate_Valid_CreatesFileAndReturnsSummary()
    {
        var tools = CreateTools();
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            var result = tools.GenerateTemplate("worklogs", tempPath);

            Assert.That(File.Exists(tempPath), Is.True);
            Assert.That(result, Does.Contain("Template saved to"));
            Assert.That(result, Does.Contain("Worklogs"));
            Assert.That(result, Does.Contain("Resource Name"));
            Assert.That(result, Does.Contain("Work Date"));
            Assert.That(result, Does.Contain("Hours"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Test]
    public void GenerateTemplate_UnknownEntity_ReturnsError()
    {
        var tools = CreateTools();
        var result = tools.GenerateTemplate("nope", "out.xlsx");
        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("nope"));
    }

    [Test]
    public void GenerateTemplate_NonXlsxPath_ReturnsError()
    {
        var tools = CreateTools();
        var result = tools.GenerateTemplate("worklogs", "out.txt");
        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain(".xlsx"));
    }

    // ── validate_excel ─────────────────────────────────────────────

    [Test]
    public async Task ValidateExcel_ValidFile_ReturnsPreview()
    {
        var tools = CreateTools();
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
            {
                sheet.Cell(2, 1).Value = "Alice";
                sheet.Cell(2, 2).Value = "Alpha";
                sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                sheet.Cell(2, 5).Value = "8";
                sheet.Cell(2, 6).Value = "Approved";
            }).ToArray());

            var result = await tools.ValidateExcelAsync("worklogs", tempPath);

            Assert.That(result, Does.Contain("Validation Results"));
            Assert.That(result, Does.Contain("Records parsed: 1"));
            Assert.That(result, Does.Contain("Alice"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Test]
    public async Task ValidateExcel_UnknownEntity_ReturnsError()
    {
        var tools = CreateTools();
        var result = await tools.ValidateExcelAsync("nope", "test.xlsx");
        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("nope"));
    }

    [Test]
    public async Task ValidateExcel_FileNotFound_ReturnsError()
    {
        var tools = CreateTools();
        var result = await tools.ValidateExcelAsync("worklogs", "Z:\\does_not_exist.xlsx");
        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("not found"));
    }

    [Test]
    public async Task ValidateExcel_NonXlsxPath_ReturnsError()
    {
        var tools = CreateTools();
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, "not an excel file");
            var result = await tools.ValidateExcelAsync("worklogs", tempPath);
            Assert.That(result, Does.Contain("Error:"));
            Assert.That(result, Does.Contain(".xlsx"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Test]
    public async Task ValidateExcel_MalformedWorkbook_DoesNotCrash()
    {
        var tools = CreateTools();
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            File.WriteAllBytes(tempPath, [0, 1, 2, 3, 4, 5]);
            var result = await tools.ValidateExcelAsync("worklogs", tempPath);
            Assert.That(result, Does.Contain("Error:"));
            Assert.That(result, Does.Contain("parse"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Test]
    public async Task ValidateExcel_PreviewLimit_20Records()
    {
        var tools = CreateTools();
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell(1, 1).Value = "Resource Display Name";
            sheet.Cell(1, 2).Value = "Project";
            sheet.Cell(1, 4).Value = "Work Date";
            sheet.Cell(1, 5).Value = "Person Hours";
            sheet.Cell(1, 6).Value = "Approval Workflow Status";
            for (var i = 1; i <= 25; i++)
            {
                var row = i + 1;
                sheet.Cell(row, 1).Value = $"Person{i}";
                sheet.Cell(row, 2).Value = $"Project{i}";
                sheet.Cell(row, 4).Value = $"1/{i}/2025 12:00:00 AM";
                sheet.Cell(row, 5).Value = $"{i}";
                sheet.Cell(row, 6).Value = "Approved";
            }
            workbook.SaveAs(tempPath);

            var result = await tools.ValidateExcelAsync("worklogs", tempPath);

            Assert.That(result, Does.Contain("Records parsed: 25"));
            Assert.That(result, Does.Contain("and 5 more records"));
            // 1 header + 1 separator + 20 preview + 1 "...n more" = 23 pipe lines
            var pipeLines = result.Split('\n').Count(l => l.TrimStart().StartsWith('|'));
            Assert.That(pipeLines, Is.EqualTo(22)); // 1 header + 1 sep + 20 preview
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Test]
    public async Task ValidateExcel_InvalidData_ReturnsValidationErrors()
    {
        var tools = CreateTools();
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
            {
                sheet.Cell(2, 1).Value = "";
                sheet.Cell(2, 4).Value = "bad-date";
                sheet.Cell(2, 5).Value = "abc";
                sheet.Cell(2, 6).Value = "";
            }).ToArray());

            var result = await tools.ValidateExcelAsync("worklogs", tempPath);

            Assert.That(result, Does.Contain("Errors"));
            Assert.That(result, Does.Contain("ResourceName"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Test]
    public void GenerateTemplate_OutputParentIsFile_ReturnsFileError()
    {
        var tools = CreateTools();
        var parentFilePath = Path.GetTempFileName();
        try
        {
            var badPath = Path.Combine(parentFilePath, "template.xlsx");
            var result = tools.GenerateTemplate("worklogs", badPath);
            Assert.That(result, Does.Contain("Error:"));
            Assert.That(result, Does.Contain("write"));
        }
        finally
        {
            if (File.Exists(parentFilePath)) File.Delete(parentFilePath);
        }
    }

    // ── upload_excel ───────────────────────────────────────────────

    [Test]
    public async Task UploadExcel_UnknownEntity_ReturnsError()
    {
        var tools = CreateTools();
        var result = await tools.UploadExcelAsync("nope", "test.xlsx");
        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("nope"));
    }

    [Test]
    public async Task UploadExcel_FileNotFound_ReturnsError()
    {
        var tools = CreateTools();
        var result = await tools.UploadExcelAsync("worklogs", "Z:\\does_not_exist.xlsx");
        Assert.That(result, Does.Contain("Error:"));
        Assert.That(result, Does.Contain("not found"));
    }

    [Test]
    public async Task UploadExcel_NonXlsxPath_ReturnsError()
    {
        var tools = CreateTools();
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, "not an excel file");
            var result = await tools.UploadExcelAsync("worklogs", tempPath);
            Assert.That(result, Does.Contain("Error:"));
            Assert.That(result, Does.Contain(".xlsx"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Test]
    public async Task UploadExcel_MalformedWorkbook_DoesNotCrash()
    {
        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "http://localhost:5000");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, [0, 1, 2, 3, 4, 5]);
                var result = await tools.UploadExcelAsync("worklogs", tempPath);
                Assert.That(result, Does.Contain("Error:"));
                Assert.That(result, Does.Contain("parse"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_MissingServerUrl_ReturnsError()
    {
        // Unset the env var so the tool reports it's missing
        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", null);

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 2).Value = "Alpha";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);
                Assert.That(result, Does.Contain("Error:"));
                Assert.That(result, Does.Contain("FACTGRID_SERVER_URL"));
                Assert.That(result, Does.Contain("not set"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_MalformedUrl_ReturnsError()
    {
        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "not-a-url");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 2).Value = "Alpha";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);
                Assert.That(result, Does.Contain("Error:"));
                Assert.That(result, Does.Contain("FACTGRID_SERVER_URL"));
                Assert.That(result, Does.Not.Contain("localhost"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_FtpUrl_ReturnsError()
    {
        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "ftp://files.example.com");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 2).Value = "Alpha";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);
                Assert.That(result, Does.Contain("Error:"));
                Assert.That(result, Does.Contain("HTTP or HTTPS"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_TransportFailure_ReturnsError()
    {
        _httpFactory.Handler.SendAsyncFunc = _ => throw new HttpRequestException("Connection refused");

        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "http://localhost:1");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 2).Value = "Alpha";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);
                Assert.That(result, Does.Contain("Error:"));
                Assert.That(result, Does.Contain("Connection refused"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_ServerSuccess_ReturnsResultSummary()
    {
        _httpFactory.Handler.SendAsyncFunc = req =>
        {
            Assert.That(req.RequestUri!.AbsolutePath, Does.EndWith("/api/ingestion/worklogs/upload"));
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(req.Content, Is.TypeOf<MultipartFormDataContent>());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new IngestionResult { Success = true, InsertedCount = 3, Errors = [] }),
                    Encoding.UTF8, "application/json")
            };
        };

        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "http://localhost:5000");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);

                Assert.That(result, Does.Contain("status 200"));
                Assert.That(result, Does.Contain("Success: True"));
                Assert.That(result, Does.Contain("Records inserted: 3"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_ServerValidationError_ReturnsStructuredResult()
    {
        _httpFactory.Handler.SendAsyncFunc = _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new IngestionResult { Success = false, InsertedCount = 0, Errors = ["Row 2: Amount is required", "Row 3: Invalid date"] }),
                Encoding.UTF8, "application/json")
        };

        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "http://localhost:5000");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);

                Assert.That(result, Does.Contain("status 422"));
                Assert.That(result, Does.Contain("Success: False"));
                Assert.That(result, Does.Contain("Records inserted: 0"));
                Assert.That(result, Does.Contain("Amount is required"));
                Assert.That(result, Does.Contain("Invalid date"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_MalformedJsonResponse_ReturnsRawBody()
    {
        _httpFactory.Handler.SendAsyncFunc = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>server error</html>", Encoding.UTF8, "text/html")
        };

        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "http://localhost:5000");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);

                Assert.That(result, Does.Contain("status 500"));
                Assert.That(result, Does.Contain("<html>server error</html>"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_UploadUrlNormalizedCorrectly()
    {
        HttpResponseMessage? captured = null;
        _httpFactory.Handler.SendAsyncFunc = req =>
        {
            captured = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new IngestionResult { Success = true, InsertedCount = 0, Errors = [] }),
                    Encoding.UTF8, "application/json")
            };
            return captured;
        };

        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "http://localhost:5000/");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "Alice";
                    sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
                    sheet.Cell(2, 5).Value = "8";
                    sheet.Cell(2, 6).Value = "Approved";
                }).ToArray());

                await tools.UploadExcelAsync("worklogs", tempPath);

                Assert.That(_httpFactory.Handler.LastRequest, Is.Not.Null);
                Assert.That(_httpFactory.Handler.LastRequest!.RequestUri!.AbsoluteUri,
                    Is.EqualTo("http://localhost:5000/api/ingestion/worklogs/upload"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    [Test]
    public async Task UploadExcel_InvalidData_ReturnsLocalValidationErrors()
    {
        var prior = Environment.GetEnvironmentVariable("FACTGRID_SERVER_URL");
        Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", "http://localhost:9999");

        try
        {
            var tools = CreateTools();
            var tempPath = Path.GetTempFileName() + ".xlsx";
            try
            {
                File.WriteAllBytes(tempPath, CreateWorklogExcel(sheet =>
                {
                    sheet.Cell(2, 1).Value = "";
                    sheet.Cell(2, 4).Value = "bad-date";
                    sheet.Cell(2, 5).Value = "abc";
                    sheet.Cell(2, 6).Value = "";
                }).ToArray());

                var result = await tools.UploadExcelAsync("worklogs", tempPath);
                Assert.That(result, Does.Contain("validation errors"));
                Assert.That(result, Does.Contain("ResourceName"));
                Assert.That(result, Does.Not.Contain("localhost"));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACTGRID_SERVER_URL", prior);
        }
    }

    // ── list_entities ──────────────────────────────────────────────

    [Test]
    public void ListEntities_ReturnsAllEntitiesWithColumns()
    {
        var tools = CreateTools();
        var result = tools.ListEntities();

        Assert.That(result, Does.Contain("worklogs"));
        Assert.That(result, Does.Contain("ResourceHours"));
        Assert.That(result, Does.Contain("expenses"));
        Assert.That(result, Does.Contain("Expenses"));

        // Column metadata uses [ExcelColumn] titles
        Assert.That(result, Does.Contain("Resource Name"));
        Assert.That(result, Does.Contain("Work Date"));
        Assert.That(result, Does.Contain("Hours"));
        Assert.That(result, Does.Contain("Category"));
        Assert.That(result, Does.Contain("Amount"));
    }

    [Test]
    public void ListEntities_EmptyRegistry_ReturnsMessage()
    {
        var tools = new DataEntryTools(
            new EntityRegistry(),
            _generator,
            _serviceProvider,
            _httpFactory);
        var result = tools.ListEntities();
        Assert.That(result, Does.Contain("No entities registered"));
    }
}
