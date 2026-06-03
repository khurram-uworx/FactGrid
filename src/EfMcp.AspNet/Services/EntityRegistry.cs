using System.Collections.Concurrent;

namespace EfMcp.AspNet.Services;

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
        if (!entities.TryAdd(registration.EntityName, registration))
            throw new InvalidOperationException($"Entity '{registration.EntityName}' is already registered.");
    }

    public EntityRegistration? Get(string entityName)
    {
        entities.TryGetValue(entityName, out var registration);
        return registration;
    }

    public IEnumerable<EntityRegistration> GetAll()
        => entities.Values;
}
