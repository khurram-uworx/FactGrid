using Microsoft.Extensions.DependencyInjection;

namespace FactGrid.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFactGridEntities(this IServiceCollection services)
    {
        var registry = new EntityRegistry();
        foreach (var (entityName, displayName, modelType, parserType, tableName, description) in FactGridEntityCatalog.GetEntities())
        {
            var registerMethod = typeof(EntityRegistry)
                .GetMethod(nameof(EntityRegistry.RegisterWithParser))
                ?.MakeGenericMethod(modelType, parserType);

            registerMethod!.Invoke(registry, [entityName, displayName, tableName, description]);
        }

        services.AddSingleton(registry);
        services.AddSingleton<ExcelTemplateGenerator>();

        foreach (var (entityName, displayName, modelType, parserType, tableName, description) in FactGridEntityCatalog.GetEntities())
        {
            var parserInterface = typeof(IExcelParser<>).MakeGenericType(modelType);
            services.AddScoped(parserInterface, parserType);
        }

        return services;
    }
}
