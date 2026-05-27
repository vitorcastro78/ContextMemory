using ContextMemory.Core.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContextMemory.Api;

public sealed class ContextMemoryDbContextFactory : IDesignTimeDbContextFactory<ContextMemoryDbContext>
{
    public ContextMemoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ContextMemoryDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=contextmemory;Username=postgres;Password=postgres",
            npgsql => npgsql.MigrationsAssembly(typeof(Program).Assembly.GetName().Name));
        return new ContextMemoryDbContext(optionsBuilder.Options);
    }
}
