namespace FactGrid.AspNet.Services;

public interface IEntityTableService
{
    Task<long> CountAsync();
    Task DeleteAllAsync();
}

public interface IEntityTableService<T> : IEntityTableService where T : class
{
}
