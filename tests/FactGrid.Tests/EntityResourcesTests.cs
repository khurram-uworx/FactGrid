using FactGrid.AspNet.Services;
using FactGrid.AspNet.Tools;
using FactGrid.Models;
using FactGrid.Services;
using System.Text.Json;

namespace FactGrid.Tests;

public class EntityResourcesTests
{
    static EntityRegistry CreateTestRegistry()
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
            Description: "Employee expense report entries"
        ));
        return r;
    }

    [Test]
    public void GetEntityListAsync_GlobalMode_ReturnsAllEntities()
    {
        var registry = CreateTestRegistry();
        var resources = new EntityResources(registry, new EntityContextAccessor());

        var json = resources.GetEntityListAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var byName = doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e);

        Assert.That(byName, Has.Count.EqualTo(2));

        var wl = byName["worklogs"];
        Assert.That(wl.GetProperty("displayName").GetString(), Is.EqualTo("Worklogs"));
        Assert.That(wl.GetProperty("table").GetString(), Is.EqualTo("ResourceHours"));

        var ex = byName["expenses"];
        Assert.That(ex.GetProperty("displayName").GetString(), Is.EqualTo("Expenses"));
        Assert.That(ex.GetProperty("table").GetString(), Is.EqualTo("Expenses"));
    }

    [Test]
    public void GetEntityListAsync_ScopedMode_ReturnsScopedEntityOnly()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("expenses") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntityListAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var entities = doc.RootElement.EnumerateArray().ToList();
        Assert.That(entities, Has.Count.EqualTo(1));
        Assert.That(entities[0].GetProperty("name").GetString(), Is.EqualTo("expenses"));
        Assert.That(entities[0].GetProperty("table").GetString(), Is.EqualTo("Expenses"));
    }

    [Test]
    public void GetEntitySchemaAsync_GlobalMode_ReturnsAllSchemas()
    {
        var registry = CreateTestRegistry();
        var resources = new EntityResources(registry, new EntityContextAccessor());

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var schemas = doc.RootElement.EnumerateArray().ToList();
        Assert.That(schemas, Has.Count.EqualTo(2));

        var byName = schemas.ToDictionary(s => s.GetProperty("entityName").GetString()!, s => s);

        var wl = byName["worklogs"];
        Assert.That(wl.GetProperty("table").GetString(), Is.EqualTo("ResourceHours"));
        var wlColumns = wl.GetProperty("columns").EnumerateArray().ToList();
        Assert.That(wlColumns.Any(c => c.GetProperty("name").GetString() == "ResourceName"));
        Assert.That(wlColumns.Any(c => c.GetProperty("name").GetString() == "Hours"));

        var ex = byName["expenses"];
        Assert.That(ex.GetProperty("table").GetString(), Is.EqualTo("Expenses"));
        var exColumns = ex.GetProperty("columns").EnumerateArray().ToList();
        Assert.That(exColumns.Any(c => c.GetProperty("name").GetString() == "Category"));
        Assert.That(exColumns.Any(c => c.GetProperty("name").GetString() == "Amount"));
    }

    [Test]
    public void GetEntitySchemaAsync_ScopedMode_ReturnsScopedSchemaOnly()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var schemas = doc.RootElement.EnumerateArray().ToList();
        Assert.That(schemas, Has.Count.EqualTo(1));
        Assert.That(schemas[0].GetProperty("entityName").GetString(), Is.EqualTo("worklogs"));
        Assert.That(schemas[0].GetProperty("table").GetString(), Is.EqualTo("ResourceHours"));
    }

    [Test]
    public void GetEntitySchemaAsync_IncludesColumnDescriptions()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray().ToList();
        var resourceName = columns.First(c => c.GetProperty("name").GetString() == "ResourceName");
        Assert.That(resourceName.GetProperty("description").GetString(), Does.Contain("resource"));
    }

    [Test]
    public void GetEntitySchemaAsync_SchemaOmitsId()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray().ToList();
        Assert.That(columns.Any(c => c.GetProperty("name").GetString() == "Id"), Is.False);
    }

    [Test]
    public void GetEntitySchemaAsync_Worklogs_RequiredStringsReportNonNullable()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c);

        Assert.That(columns["ResourceName"].GetProperty("isNullable").GetBoolean(), Is.False);
        Assert.That(columns["ApprovalStatus"].GetProperty("isNullable").GetBoolean(), Is.False);
    }

    [Test]
    public void GetEntitySchemaAsync_Worklogs_NullableStringsReportNullable()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c);

        Assert.That(columns["Project"].GetProperty("isNullable").GetBoolean(), Is.True);
        Assert.That(columns["Description"].GetProperty("isNullable").GetBoolean(), Is.True);
    }

    [Test]
    public void GetEntitySchemaAsync_Worklogs_ValueTypesReportNonNullable()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c);

        Assert.That(columns["WorkDate"].GetProperty("isNullable").GetBoolean(), Is.False);
        Assert.That(columns["Hours"].GetProperty("isNullable").GetBoolean(), Is.False);
    }

    [Test]
    public void GetEntitySchemaAsync_Expenses_RequiredStringsReportNonNullable()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("expenses") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c);

        Assert.That(columns["ResourceName"].GetProperty("isNullable").GetBoolean(), Is.False);
        Assert.That(columns["ApprovalStatus"].GetProperty("isNullable").GetBoolean(), Is.False);
    }

    [Test]
    public void GetEntitySchemaAsync_Expenses_NullableStringsReportNullable()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("expenses") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c);

        Assert.That(columns["Category"].GetProperty("isNullable").GetBoolean(), Is.True);
        Assert.That(columns["Description"].GetProperty("isNullable").GetBoolean(), Is.True);
    }

    [Test]
    public void GetEntitySchemaAsync_Expenses_ValueTypesReportNonNullable()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("expenses") };
        var resources = new EntityResources(registry, accessor);

        var json = resources.GetEntitySchemaAsync().Result;
        using var doc = JsonDocument.Parse(json);

        var columns = doc.RootElement[0].GetProperty("columns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c);

        Assert.That(columns["Amount"].GetProperty("isNullable").GetBoolean(), Is.False);
        Assert.That(columns["ExpenseDate"].GetProperty("isNullable").GetBoolean(), Is.False);
    }
}
