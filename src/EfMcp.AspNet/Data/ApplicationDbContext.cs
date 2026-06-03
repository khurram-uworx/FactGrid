using EfMcp.AspNet.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EfMcp.AspNet.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
    {
        public DbSet<Worklogs> Worklogs => Set<Worklogs>();
        public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();

    }
}
