using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DCF.Data;

public class DcfDbContextFactory : IDesignTimeDbContextFactory<DcfDbContext>
{
    public DcfDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? "Host=localhost;Database=dcf;Username=dcf;Password=dcf_dev";

        var options = new DbContextOptionsBuilder<DcfDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DcfDbContext(options);
    }
}
