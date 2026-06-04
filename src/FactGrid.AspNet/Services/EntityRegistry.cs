using System.Collections.Concurrent;

namespace FactGrid.AspNet.Services;

public record EntityRegistration(
    string EntityName,
    string DisplayName,
    Type ModelType,
    Type ExcelParserType,
    string TableName,
    string Description
);

public class EntityRegistry
{
    readonly ConcurrentDictionary<string, EntityRegistration> entities = new(StringComparer.OrdinalIgnoreCase);

    public void Register<T>(EntityRegistration registration) where T : class
    {
        ValidateRegistration<T>(registration);
        if (!entities.TryAdd(registration.EntityName, registration))
            throw new InvalidOperationException($"Entity '{registration.EntityName}' is already registered.");
    }

    public EntityRegistration RegisterWithParser<TModel, TParser>(
        string entityName,
        string displayName,
        string tableName,
        string description)
        where TModel : class
        where TParser : IExcelParser<TModel>
    {
        var registration = new EntityRegistration(
            EntityName: entityName,
            DisplayName: displayName,
            ModelType: typeof(TModel),
            ExcelParserType: typeof(TParser),
            TableName: tableName,
            Description: description
        );
        if (!entities.TryAdd(registration.EntityName, registration))
            throw new InvalidOperationException($"Entity '{registration.EntityName}' is already registered.");
        return registration;
    }

    static void ValidateRegistration<T>(EntityRegistration registration) where T : class
    {
        var expectedInterface = typeof(IExcelParser<>).MakeGenericType(typeof(T));
        if (!expectedInterface.IsAssignableFrom(registration.ExcelParserType))
        {
            throw new InvalidOperationException(
                $"Parser type '{registration.ExcelParserType.Name}' does not implement {expectedInterface.Name}.");
        }
    }

    public EntityRegistration? Get(string entityName)
    {
        entities.TryGetValue(entityName, out var registration);
        return registration;
    }

    public IEnumerable<EntityRegistration> GetAll()
        => entities.Values;
}
