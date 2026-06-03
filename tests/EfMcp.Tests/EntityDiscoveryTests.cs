using EfMcp.AspNet.Models;
using EfMcp.AspNet.Services;
using System.Text.Json;

namespace EfMcp.Tests;

public class EntityDiscoveryTests
{
    internal static EntityRegistry CreateTestRegistry()
    {
        var r = new EntityRegistry();
        r.Register<Worklogs>(new EntityRegistration(
            EntityName: "worklogs",
            DisplayName: "Worklogs",
            ModelType: typeof(Worklogs),
            ExcelParserType: typeof(WorklogsExcelParser),
            TableName: "ResourceHours",
            Description: "Employee worklog entries"
        ));
        r.Register<ExpenseReport>(new EntityRegistration(
            EntityName: "expenses",
            DisplayName: "Expense Reports",
            ModelType: typeof(ExpenseReport),
            ExcelParserType: typeof(ExpenseReportExcelParser),
            TableName: "ExpenseReports",
            Description: "Employee expense report entries"
        ));
        return r;
    }

    [Test]
    public void GetEntityList_ReturnsAllRegistrations()
    {
        var registry = CreateTestRegistry();
        var entities = registry.GetAll().ToDictionary(e => e.EntityName);

        Assert.That(entities, Has.Count.EqualTo(2));

        var wl = entities["worklogs"];
        Assert.That(wl.DisplayName, Is.EqualTo("Worklogs"));
        Assert.That(wl.TableName, Is.EqualTo("ResourceHours"));

        var ex = entities["expenses"];
        Assert.That(ex.DisplayName, Is.EqualTo("Expense Reports"));
        Assert.That(ex.TableName, Is.EqualTo("ExpenseReports"));
    }

    [Test]
    public void GetEntityList_SerializesToExpectedShape()
    {
        var registry = CreateTestRegistry();
        var result = registry.GetAll().Select(e => new
        {
            name = e.EntityName,
            displayName = e.DisplayName,
            description = e.Description,
            table = e.TableName
        });

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        var byName = doc.RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e);

        var worklogs = byName["worklogs"];
        Assert.That(worklogs.GetProperty("displayName").GetString(), Is.EqualTo("Worklogs"));
        Assert.That(worklogs.GetProperty("table").GetString(), Is.EqualTo("ResourceHours"));

        var expenses = byName["expenses"];
        Assert.That(expenses.GetProperty("displayName").GetString(), Is.EqualTo("Expense Reports"));
        Assert.That(expenses.GetProperty("table").GetString(), Is.EqualTo("ExpenseReports"));
    }

    [Test]
    public void GetEntityList_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new EntityRegistry();
        Assert.That(registry.GetAll(), Is.Empty);
    }

    [Test]
    public void Registry_Get_KnownEntity_ReturnsRegistration()
    {
        var registry = CreateTestRegistry();
        var entity = registry.Get("worklogs");
        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.TableName, Is.EqualTo("ResourceHours"));

        var expenses = registry.Get("expenses");
        Assert.That(expenses, Is.Not.Null);
        Assert.That(expenses!.TableName, Is.EqualTo("ExpenseReports"));
    }

    [Test]
    public void Registry_Get_UnknownEntity_ReturnsNull()
    {
        var registry = CreateTestRegistry();
        Assert.That(registry.Get("not-real"), Is.Null);
    }

    [Test]
    public void EntityRegistry_IsCaseInsensitive()
    {
        var registry = CreateTestRegistry();
        Assert.That(registry.Get("WORKLOGS"), Is.Not.Null);
        Assert.That(registry.Get("Worklogs"), Is.Not.Null);
        Assert.That(registry.Get("EXPENSES"), Is.Not.Null);
        Assert.That(registry.Get("Expenses"), Is.Not.Null);
    }

    [Test]
    public void EntityContextAccessor_ClearsStaleContext()
    {
        var accessor = new EntityContextAccessor();
        var registry = CreateTestRegistry();

        accessor.CurrentEntity = registry.Get("worklogs");
        Assert.That(accessor.CurrentEntity, Is.Not.Null);

        accessor.CurrentEntity = null;
        Assert.That(accessor.CurrentEntity, Is.Null);
    }

    [Test]
    public void EntityContextAccessor_AsyncLocal_IsScoped()
    {
        var accessor = new EntityContextAccessor();
        accessor.CurrentEntity = CreateTestRegistry().Get("worklogs");

        var otherValue = Task.Run(() =>
        {
            var other = new EntityContextAccessor();
            other.CurrentEntity = null;
            return other.CurrentEntity;
        }).Result;

        Assert.That(accessor.CurrentEntity, Is.Not.Null);
        Assert.That(otherValue, Is.Null);
    }
}
