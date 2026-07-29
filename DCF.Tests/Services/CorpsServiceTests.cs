using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class CorpsServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task GetCorpsAsync_ReturnsAllCorpsKeyedByName()
    {
        using var db = CreateDb("corps_all");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        db.Corps.AddRange(
            new CorpsEntity { Id = id1, Name = "Blue Devils" },
            new CorpsEntity { Id = id2, Name = "Cavaliers" }
        );

        await db.SaveChangesAsync();

        var service = new CorpsService(db);
        var result = await service.GetCorpsAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("Blue Devils"));
        Assert.True(result.ContainsKey("Cavaliers"));
        Assert.Equal(id1, result["Blue Devils"].Id);
        Assert.Equal(id2, result["Cavaliers"].Id);
    }

    [Fact]
    public async Task GetCorpsAsync_LookupIsCaseInsensitive()
    {
        using var db = CreateDb("corps_case_insensitive");
        var id = Guid.NewGuid();
        db.Corps.Add(new CorpsEntity { Id = id, Name = "Blue Devils" });

        await db.SaveChangesAsync();

        var service = new CorpsService(db);
        var result = await service.GetCorpsAsync();

        Assert.True(result.TryGetValue("blue devils", out var corps));
        Assert.Equal(id, corps!.Id);
    }

    [Fact]
    public async Task GetCorpsAsync_EmptyDatabase_ReturnsEmptyDictionary()
    {
        using var db = CreateDb("corps_empty");
        var service = new CorpsService(db);

        var result = await service.GetCorpsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCorpsAsync_CorpsNameMatchesKey()
    {
        using var db = CreateDb("corps_name");
        var id = Guid.NewGuid();
        db.Corps.Add(new CorpsEntity { Id = id, Name = "Phantom Regiment" });

        await db.SaveChangesAsync();

        var service = new CorpsService(db);
        var result = await service.GetCorpsAsync();

        Assert.Equal("Phantom Regiment", result["Phantom Regiment"].Name);
        Assert.Equal(id, result["Phantom Regiment"].Id);
    }
}
