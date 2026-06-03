using EfMcp.AspNet.Models;
using EfMcp.AspNet.Services;

namespace EfMcp.Tests;

public class EntityRegistryTests
{
    [Test]
    public void Register_ValidParserType_Succeeds()
    {
        var registry = new EntityRegistry();
        var registration = new EntityRegistration(
            EntityName: "worklogs",
            DisplayName: "Worklogs",
            ModelType: typeof(Worklogs),
            ExcelParserType: typeof(WorklogsExcelParser),
            TableName: "ResourceHours",
            Description: "test"
        );

        Assert.DoesNotThrow(() => registry.Register<Worklogs>(registration));
    }

    [Test]
    public void Register_InvalidParserType_Throws()
    {
        var registry = new EntityRegistry();
        var registration = new EntityRegistration(
            EntityName: "bad",
            DisplayName: "Bad",
            ModelType: typeof(Worklogs),
            ExcelParserType: typeof(string),
            TableName: "BadTable",
            Description: "test"
        );

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register<Worklogs>(registration));
        Assert.That(ex.Message, Does.Contain("String"));
        Assert.That(ex.Message, Does.Contain("IExcelParser"));
    }

    [Test]
    public void RegisterWithParser_TypedHelper_Succeeds()
    {
        var registry = new EntityRegistry();

        var result = registry.RegisterWithParser<Worklogs, WorklogsExcelParser>(
            entityName: "worklogs",
            displayName: "Worklogs",
            tableName: "ResourceHours",
            description: "test"
        );

        Assert.That(result, Is.Not.Null);
        Assert.That(result.EntityName, Is.EqualTo("worklogs"));
        Assert.That(result.ModelType, Is.EqualTo(typeof(Worklogs)));
        Assert.That(result.ExcelParserType, Is.EqualTo(typeof(WorklogsExcelParser)));
    }

    [Test]
    public void RegisterWithParser_DuplicateName_Throws()
    {
        var registry = new EntityRegistry();
        registry.RegisterWithParser<Worklogs, WorklogsExcelParser>(
            entityName: "worklogs",
            displayName: "Worklogs",
            tableName: "ResourceHours",
            description: "test"
        );

        Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterWithParser<Worklogs, WorklogsExcelParser>(
                entityName: "worklogs",
                displayName: "Worklogs",
                tableName: "ResourceHours",
                description: "test"
            ));
    }
}