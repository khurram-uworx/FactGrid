using FactGrid.Services;

namespace FactGrid.AspNet.Services;

public class EntityServiceFactory : IEntityServiceFactory
{
    readonly IServiceProvider serviceProvider;

    public EntityServiceFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public IExcelParser CreateExcelParser(Type modelType)
    {
        var t = typeof(IExcelParser<>).MakeGenericType(modelType);
        return (IExcelParser)serviceProvider.GetRequiredService(t);
    }

    public IEntityTableService CreateTableService(Type modelType)
    {
        var t = typeof(IEntityTableService<>).MakeGenericType(modelType);
        return (IEntityTableService)serviceProvider.GetRequiredService(t);
    }
}
