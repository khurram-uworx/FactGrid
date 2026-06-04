using FactGrid.AspNet.Models;
using FactGrid.AspNet.Services;

namespace FactGrid.Tests;

public class EntityRegistryTests
{
    [Test]
    public void Register_ValidParserType_Succeeds()
    {
        var registry = new EntityRegistry();
        var registration = new EntityRegistration(
            EntityName: "worklogs",
            DisplayName: "Worklogs",
            ModelType: typeof(Worklog),
            ExcelParserType: typeof(WorklogsExcelParser),
            TableName: "ResourceHours",
            Description: "test"
        );

        Assert.DoesNotThrow(() => registry.Register<Worklog>(registration));
    }

    [Test]
    public void Register_InvalidParserType_Throws()
    {
        var registry = new EntityRegistry();
        var registration = new EntityRegistration(
            EntityName: "bad",
            DisplayName: "Bad",
            ModelType: typeof(Worklog),
            ExcelParserType: typeof(string),
            TableName: "BadTable",
            Description: "test"
        );

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register<Worklog>(registration));
        Assert.That(ex.Message, Does.Contain("String"));
        Assert.That(ex.Message, Does.Contain("IExcelParser"));
    }

    [Test]
    public void RegisterWithParser_TypedHelper_Succeeds()
    {
        var registry = new EntityRegistry();

        var result = registry.RegisterWithParser<Worklog, WorklogsExcelParser>(
            entityName: "worklogs",
            displayName: "Worklogs",
            tableName: "ResourceHours",
            description: "test"
        );

        Assert.That(result, Is.Not.Null);
        Assert.That(result.EntityName, Is.EqualTo("worklogs"));
        Assert.That(result.ModelType, Is.EqualTo(typeof(Worklog)));
        Assert.That(result.ExcelParserType, Is.EqualTo(typeof(WorklogsExcelParser)));
    }

    [Test]
    public void RegisterWithParser_DuplicateName_Throws()
    {
        var registry = new EntityRegistry();
        registry.RegisterWithParser<Worklog, WorklogsExcelParser>(
            entityName: "worklogs",
            displayName: "Worklogs",
            tableName: "ResourceHours",
            description: "test"
        );

        Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterWithParser<Worklog, WorklogsExcelParser>(
                entityName: "worklogs",
                displayName: "Worklogs",
                tableName: "ResourceHours",
                description: "test"
            ));
    }
}
