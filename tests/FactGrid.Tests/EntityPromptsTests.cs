using FactGrid.AspNet.Services;
using FactGrid.AspNet.Tools;
using FactGrid.Models;
using FactGrid.Services;
using ModelContextProtocol.Protocol;

namespace FactGrid.Tests;

public class EntityPromptsTests
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
    public void GetEntitiesGuide_GlobalMode_ListsAllEntities()
    {
        var registry = CreateTestRegistry();
        var prompts = new EntityPrompts(registry, new EntityContextAccessor());

        var result = prompts.GetEntitiesGuide();
        var text = ((TextContentBlock)result.Messages[0].Content).Text;

        Assert.That(text, Does.Contain("worklogs"));
        Assert.That(text, Does.Contain("ResourceHours"));
        Assert.That(text, Does.Contain("expenses"));
        Assert.That(text, Does.Contain("Expenses"));
        Assert.That(text, Does.Contain("any registered table"));
    }

    [Test]
    public void GetEntitiesGuide_ScopedMode_ListsScopedEntityOnly()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("expenses") };
        var prompts = new EntityPrompts(registry, accessor);

        var result = prompts.GetEntitiesGuide();
        var text = ((TextContentBlock)result.Messages[0].Content).Text;

        Assert.That(text, Does.Contain("expenses"));
        Assert.That(text, Does.Contain("Expenses"));
        Assert.That(text, Does.Not.Contain("worklogs"));
        Assert.That(text, Does.Not.Contain("ResourceHours"));
        Assert.That(text, Does.Not.Contain("any registered table"));
    }

    [Test]
    public void GetEntityGuide_WithScope_ShowsSchemaAndGuidance()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var prompts = new EntityPrompts(registry, accessor);

        var result = prompts.GetEntityGuide();
        var text = ((TextContentBlock)result.Messages[0].Content).Text;

        Assert.That(text, Does.Contain("Worklogs"));
        Assert.That(text, Does.Contain("ResourceHours"));
        Assert.That(text, Does.Contain("ResourceName"));
        Assert.That(text, Does.Contain("NVARCHAR"));
        Assert.That(text, Does.Contain("WorkDate"));
        Assert.That(text, Does.Contain("SELECT * FROM ResourceHours"));
    }

    [Test]
    public void GetEntityGuide_NoScope_ReturnsErrorMessage()
    {
        var prompts = new EntityPrompts(new EntityRegistry(), new EntityContextAccessor());

        var result = prompts.GetEntityGuide();
        var text = ((TextContentBlock)result.Messages[0].Content).Text;

        Assert.That(text, Does.Contain("No entity scope"));
    }

    [Test]
    public void GetEntitiesGuide_HasCorrectPromptMetadata()
    {
        var registry = CreateTestRegistry();
        var prompts = new EntityPrompts(registry, new EntityContextAccessor());

        var result = prompts.GetEntitiesGuide();

        Assert.That(result.Description, Is.EqualTo("Entity query guide"));
    }

    [Test]
    public void GetEntityGuide_WithScope_HasEntitySpecificDescription()
    {
        var registry = CreateTestRegistry();
        var accessor = new EntityContextAccessor { CurrentEntity = registry.Get("worklogs") };
        var prompts = new EntityPrompts(registry, accessor);

        var result = prompts.GetEntityGuide();

        Assert.That(result.Description, Is.EqualTo("worklogs query guide"));
    }
}
