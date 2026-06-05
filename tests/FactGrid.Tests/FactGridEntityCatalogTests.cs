using FactGrid.Models;
using FactGrid.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FactGrid.Tests;

public class FactGridEntityCatalogTests
{
    [Test]
    public void GetEntities_ReturnsWorklogsAndExpenses()
    {
        var entities = FactGridEntityCatalog.GetEntities().ToList();

        Assert.That(entities, Has.Count.EqualTo(2));

        var worklogs = entities.Single(e => e.EntityName == "worklogs");
        Assert.That(worklogs.DisplayName, Is.EqualTo("Worklogs"));
        Assert.That(worklogs.ModelType, Is.EqualTo(typeof(Worklog)));
        Assert.That(worklogs.ParserType, Is.EqualTo(typeof(WorklogsExcelParser)));
        Assert.That(worklogs.TableName, Is.EqualTo("ResourceHours"));

        var expenses = entities.Single(e => e.EntityName == "expenses");
        Assert.That(expenses.DisplayName, Is.EqualTo("Expenses"));
        Assert.That(expenses.ModelType, Is.EqualTo(typeof(Expense)));
        Assert.That(expenses.ParserType, Is.EqualTo(typeof(ExpensesExcelParser)));
        Assert.That(expenses.TableName, Is.EqualTo("Expenses"));
    }

    [Test]
    public void AddFactGridEntities_PopulatesRegistry()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddFactGridEntities();

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<EntityRegistry>();

        var entities = registry.GetAll().ToList();
        Assert.That(entities, Has.Count.EqualTo(2));

        Assert.That(registry.Get("worklogs"), Is.Not.Null);
        Assert.That(registry.Get("expenses"), Is.Not.Null);
    }

    [Test]
    public void AddFactGridEntities_RegistersParsers()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddFactGridEntities();

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var worklogParser = scope.ServiceProvider.GetRequiredService(typeof(IExcelParser<Worklog>));
        Assert.That(worklogParser, Is.TypeOf<WorklogsExcelParser>());

        var expenseParser = scope.ServiceProvider.GetRequiredService(typeof(IExcelParser<Expense>));
        Assert.That(expenseParser, Is.TypeOf<ExpensesExcelParser>());
    }

    [Test]
    public void AddFactGridEntities_RegistersTemplateGenerator()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddFactGridEntities();

        var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ExcelTemplateGenerator>();

        Assert.That(generator, Is.Not.Null);
    }
}
