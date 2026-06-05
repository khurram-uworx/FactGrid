using ClosedXML.Excel;
using FactGrid.AspNet.Data;
using FactGrid.AspNet.Services;
using FactGrid.Models;
using FactGrid.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;

namespace FactGrid.Tests;

[TestFixture]
public class IngestionControllerTests
{
    static SqliteConnection _connection = null!;
    static WebApplicationFactory<Program> _factory = null!;
    static HttpClient _client = null!;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptorsToRemove = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                                    || d.ServiceType == typeof(ApplicationDbContext))
                        .ToList();

                    foreach (var d in descriptorsToRemove)
                        services.Remove(d);

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlite(_connection));
                });
            });

        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    static MemoryStream CreateWorklogExcel(params Action<IXLWorksheet>[] rowActions)
    {
        var stream = new MemoryStream();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        sheet.Cell(1, 1).Value = "Resource Name";
        sheet.Cell(1, 2).Value = "Project";
        sheet.Cell(1, 3).Value = "Description";
        sheet.Cell(1, 4).Value = "Work Date";
        sheet.Cell(1, 5).Value = "Hours";
        sheet.Cell(1, 6).Value = "Approval Status";
        foreach (var action in rowActions)
            action(sheet);
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    static MultipartFormDataContent CreateUploadContent(Stream excelStream, string fileName = "upload.xlsx")
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(((MemoryStream)excelStream).ToArray()), "file", fileName);
        return content;
    }

    static async Task<JsonElement> ParseResponse(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
    }

    [Test]
    public async Task Upload_ValidWorklogs_ReturnsSuccessWithCount()
    {
        var excel = CreateWorklogExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Alice";
            sheet.Cell(2, 2).Value = "Alpha";
            sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
            sheet.Cell(2, 5).Value = "8";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var response = await _client.PostAsync("/api/ingestion/worklogs/upload", CreateUploadContent(excel));
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task Upload_ValidExpenses_ReturnsSuccessWithCount()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Data");
            sheet.Cell(1, 1).Value = "Resource Name";
            sheet.Cell(1, 2).Value = "Category";
            sheet.Cell(1, 3).Value = "Description";
            sheet.Cell(1, 4).Value = "Amount";
            sheet.Cell(1, 5).Value = "Expense Date";
            sheet.Cell(1, 6).Value = "Approval Status";
            sheet.Cell(2, 1).Value = "Bob";
            sheet.Cell(2, 2).Value = "Travel";
            sheet.Cell(2, 4).Value = "250.00";
            sheet.Cell(2, 5).Value = "7/15/2025 12:00:00 AM";
            sheet.Cell(2, 6).Value = "Pending";
            workbook.SaveAs(stream);
        }
        stream.Position = 0;

        var response = await _client.PostAsync("/api/ingestion/expenses/upload", CreateUploadContent(stream));
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task Upload_InvalidRows_ReturnsUnprocessable()
    {
        var excel = CreateWorklogExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "";
            sheet.Cell(2, 4).Value = "bad-date";
            sheet.Cell(2, 5).Value = "abc";
            sheet.Cell(2, 6).Value = "";
        });

        var response = await _client.PostAsync("/api/ingestion/worklogs/upload", CreateUploadContent(excel));
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(0));
        var errors = json.GetProperty("errors").EnumerateArray().ToList();
        Assert.That(errors, Is.Not.Empty);
    }

    [Test]
    public async Task Upload_UnknownEntity_ReturnsNotFound()
    {
        var excel = CreateWorklogExcel(sheet => { });
        var response = await _client.PostAsync("/api/ingestion/nonexistent/upload", CreateUploadContent(excel));
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public async Task Upload_NonXlsxFile_ReturnsBadRequest()
    {
        var excel = CreateWorklogExcel(sheet => { });
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(((MemoryStream)excel).ToArray()), "file", "upload.txt");

        var response = await _client.PostAsync("/api/ingestion/worklogs/upload", content);
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public async Task Upload_NoFile_ReturnsBadRequest()
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent("dummy"), "not_a_file_field" }
        };
        var response = await _client.PostAsync("/api/ingestion/worklogs/upload", content);
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public async Task Upload_MalformedXlsx_ReturnsBadRequest()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0, 1, 2, 3, 4, 5]), "file", "bad.xlsx");

        var response = await _client.PostAsync("/api/ingestion/worklogs/upload", content);
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(0));
        Assert.That(json.GetProperty("errors").EnumerateArray().Any(), Is.True);
    }

    [Test]
    public async Task Upload_InvalidRows_LeavesDatabaseUnchanged()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Worklogs.ExecuteDelete();
        db.SaveChanges();

        // Insert one valid record
        db.Worklogs.Add(new Worklog { ResourceName = "Alice", Project = "P1", WorkDate = new DateOnly(2025, 1, 1), Hours = 8m, ApprovalStatus = "Approved" });
        db.SaveChanges();

        var beforeCount = await db.Set<Worklog>().LongCountAsync();

        // Upload with invalid rows
        var excel = CreateWorklogExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "";
            sheet.Cell(2, 4).Value = "bad-date";
            sheet.Cell(2, 5).Value = "abc";
            sheet.Cell(2, 6).Value = "";
        });

        var response = await _client.PostAsync("/api/ingestion/worklogs/upload", CreateUploadContent(excel));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));

        // DB should still have exactly 1 record
        var afterCount = await db.Set<Worklog>().LongCountAsync();
        Assert.That(afterCount, Is.EqualTo(beforeCount));
    }

    [Test]
    public async Task Upload_StructuredErrorResponses_ShareSameShape()
    {
        // 500 — simulate by sending a valid request to a route that will cause
        // an unhandled exception. The outer catch in the controller should return
        // a structured 500 with the IngestionResult shape.
        //
        // Since we can't easily trigger a 500 from outside, verify that 400 and
        // 422 responses share the same contract shape as 200.

        // 200 success
        var validExcel = CreateWorklogExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Alice";
            sheet.Cell(2, 2).Value = "Alpha";
            sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
            sheet.Cell(2, 5).Value = "8";
            sheet.Cell(2, 6).Value = "Approved";
        });
        var okResp = await _client.PostAsync("/api/ingestion/worklogs/upload", CreateUploadContent(validExcel));
        Assert.That(okResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 400 bad request — non-xlsx file
        var badExt = new MultipartFormDataContent();
        badExt.Add(new ByteArrayContent([0, 1, 2]), "file", "test.txt");
        var badResp = await _client.PostAsync("/api/ingestion/worklogs/upload", badExt);
        Assert.That(badResp.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        // 422 unprocessable — invalid rows
        var invalidExcel = CreateWorklogExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "";
            sheet.Cell(2, 4).Value = "bad-date";
            sheet.Cell(2, 5).Value = "abc";
            sheet.Cell(2, 6).Value = "";
        });
        var unprocResp = await _client.PostAsync("/api/ingestion/worklogs/upload", CreateUploadContent(invalidExcel));
        Assert.That(unprocResp.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));

        // All three share the same contract shape
        foreach (var (resp, _) in new[] { (okResp, "ok"), (badResp, "bad"), (unprocResp, "unproc") })
        {
            var body = await resp.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.That(root.TryGetProperty("success", out _), Is.True, $"{resp.StatusCode} lacks success");
            Assert.That(root.TryGetProperty("insertedCount", out _), Is.True, $"{resp.StatusCode} lacks insertedCount");
            Assert.That(root.TryGetProperty("errors", out _), Is.True, $"{resp.StatusCode} lacks errors");
        }
    }

    class ThrowingEntityServiceFactory : IEntityServiceFactory
    {
        public IExcelParser CreateExcelParser(Type modelType) =>
            throw new InvalidOperationException("Simulated DB failure — sensitive details");
        public IEntityTableService CreateTableService(Type modelType) =>
            throw new InvalidOperationException("Simulated DB failure — sensitive details");
    }

    [Test]
    public async Task Upload_UnexpectedFactoryFailure_ReturnsStructured500()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptorsToRemove = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                                    || d.ServiceType == typeof(ApplicationDbContext))
                        .ToList();

                    foreach (var d in descriptorsToRemove)
                        services.Remove(d);

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlite(connection));

                    // Replace the real factory with a throwing one
                    var factoryDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IEntityServiceFactory));
                    if (factoryDesc is not null)
                        services.Remove(factoryDesc);
                    services.AddScoped<IEntityServiceFactory>(_ => new ThrowingEntityServiceFactory());
                });
            });

        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        var excel = CreateWorklogExcel(sheet =>
        {
            sheet.Cell(2, 1).Value = "Alice";
            sheet.Cell(2, 2).Value = "Alpha";
            sheet.Cell(2, 4).Value = "6/1/2025 12:00:00 AM";
            sheet.Cell(2, 5).Value = "8";
            sheet.Cell(2, 6).Value = "Approved";
        });

        var response = await client.PostAsync("/api/ingestion/worklogs/upload", CreateUploadContent(excel));
        var json = await ParseResponse(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(json.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("insertedCount").GetInt32(), Is.EqualTo(0));
        var errors = json.GetProperty("errors").EnumerateArray().ToList();
        Assert.That(errors, Has.Count.EqualTo(1));
        // Verify the error message does NOT expose the sensitive exception details
        Assert.That(errors[0].GetString(), Does.Contain("unexpected error"));
        Assert.That(errors[0].GetString(), Does.Not.Contain("sensitive details"));
        Assert.That(errors[0].GetString(), Does.Not.Contain("Simulated DB failure"));
    }

    [Test]
    public async Task Upload_AllResponses_ShareSameJsonShape()
    {
        // Verify every error path returns the same contract shape.
        // No file.
        var noFile = await _client.PostAsync("/api/ingestion/worklogs/upload", new MultipartFormDataContent
        {
            { new StringContent("x"), "not_a_file_field" }
        });
        var noFileJson = await ParseResponse(noFile);
        Assert.That(noFileJson.TryGetProperty("success", out _), Is.True);
        Assert.That(noFileJson.TryGetProperty("insertedCount", out _), Is.True);
        Assert.That(noFileJson.TryGetProperty("errors", out _), Is.True);

        // Unknown entity.
        var unknown = await _client.PostAsync("/api/ingestion/nonexistent/upload", new MultipartFormDataContent
        {
            { new ByteArrayContent([0]), "file", "test.xlsx" }
        });
        var unknownJson = await ParseResponse(unknown);
        Assert.That(unknownJson.TryGetProperty("success", out _), Is.True);
        Assert.That(unknownJson.TryGetProperty("insertedCount", out _), Is.True);
        Assert.That(unknownJson.TryGetProperty("errors", out _), Is.True);

        // Malformed file.
        var malformed = await _client.PostAsync("/api/ingestion/worklogs/upload", new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "bad.xlsx" }
        });
        var malformedJson = await ParseResponse(malformed);
        Assert.That(malformedJson.TryGetProperty("success", out _), Is.True);
        Assert.That(malformedJson.TryGetProperty("insertedCount", out _), Is.True);
        Assert.That(malformedJson.TryGetProperty("errors", out _), Is.True);

        // Non-xlsx extension.
        var wrongExt = await _client.PostAsync("/api/ingestion/worklogs/upload", new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "test.txt" }
        });
        var wrongExtJson = await ParseResponse(wrongExt);
        Assert.That(wrongExtJson.TryGetProperty("success", out _), Is.True);
        Assert.That(wrongExtJson.TryGetProperty("insertedCount", out _), Is.True);
        Assert.That(wrongExtJson.TryGetProperty("errors", out _), Is.True);
    }
}
