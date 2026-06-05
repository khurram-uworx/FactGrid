using FactGrid.Models;

namespace FactGrid.Services;

public static class FactGridEntityCatalog
{
    public static IEnumerable<(string EntityName, string DisplayName, Type ModelType, Type ParserType, string TableName, string Description)> GetEntities()
    {
        yield return ("worklogs", "Worklogs", typeof(Worklog), typeof(WorklogsExcelParser), "ResourceHours", "Employee worklog entries");
        yield return ("expenses", "Expenses", typeof(Expense), typeof(ExpensesExcelParser), "Expenses", "Employee expense entries");
    }
}
