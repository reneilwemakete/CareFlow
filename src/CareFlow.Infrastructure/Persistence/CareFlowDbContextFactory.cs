using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CareFlow.Infrastructure.Persistence;

public sealed class CareFlowDbContextFactory : IDesignTimeDbContextFactory<CareFlowDbContext>
{
    public CareFlowDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__CareFlowDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set ConnectionStrings__CareFlowDb before running EF Core tooling.");
        var options = new DbContextOptionsBuilder<CareFlowDbContext>()
            .UseSqlServer(connection)
            .Options;
        return new CareFlowDbContext(options);
    }
}
