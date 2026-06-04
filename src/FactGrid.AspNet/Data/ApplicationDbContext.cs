using FactGrid.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FactGrid.AspNet.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
    {
        public DbSet<Worklog> Worklogs => Set<Worklog>();
        public DbSet<Expense> Expenses => Set<Expense>();
    }
}
