using FactGrid.AspNet.Data;
using Microsoft.EntityFrameworkCore;

namespace FactGrid.AspNet.Services;

public class EntityTableService<T> : IEntityTableService<T> where T : class
{
    readonly ApplicationDbContext db;

    public EntityTableService(ApplicationDbContext db)
    {
        this.db = db;
    }

    public Task<long> CountAsync()
    {
        return db.Set<T>().LongCountAsync();
    }

    public Task DeleteAllAsync()
    {
        return db.Set<T>().ExecuteDeleteAsync();
    }
}
