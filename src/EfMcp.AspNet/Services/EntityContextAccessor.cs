namespace EfMcp.AspNet.Services;

public interface IEntityContextAccessor
{
    EntityRegistration? CurrentEntity { get; set; }
}

public sealed class EntityContextAccessor : IEntityContextAccessor
{
    readonly AsyncLocal<EntityRegistration?> current = new();

    public EntityRegistration? CurrentEntity
    {
        get => current.Value;
        set => current.Value = value;
    }
}
