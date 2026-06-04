using ClosedXML.Excel;
using FactGrid.AspNet.Data;
using FactGrid.Models;
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
}
