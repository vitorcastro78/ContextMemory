using ContextMemory.Core.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ContextMemory.Api;

public sealed class ContextMemoryDbContextFactory : IDesignTimeDbContextFactory<ContextMemoryDbContext>
{
    public ContextMemoryDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("ContextMemory")
            ?? "Host=localhost;Port=5432;Database=contextmemory;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ContextMemoryDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(Program).Assembly.GetName().Name));

        return new ContextMemoryDbContext(optionsBuilder.Options);
    }
}
