# DCF Fantasy Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete DCF Fantasy Platform on top of the existing DCF.ScoreScraper — database layer, ASP.NET Core API with Auth0 and MQTT, and a React frontend with live snake draft.

**Architecture:** Single Visual Studio solution with four projects: DCF.ScoreScraper (class library, scraping logic), DCF.Data (EF Core + Npgsql entities and DbContext), DCF.Api (ASP.NET Core Web API with Auth0 JWT, background scheduling, MQTT publishing via MQTTnet), DCF.Web (Vite + React + TypeScript SPA). PostgreSQL and Mosquitto run via Docker Compose.

**Tech Stack:** .NET 10, ASP.NET Core, Entity Framework Core 10, Npgsql.EntityFrameworkCore.PostgreSQL, MQTTnet 4, Microsoft.AspNetCore.Authentication.JwtBearer, HtmlAgilityPack, Auth0 React SDK, React 18, Vite 5, TypeScript, MQTT.js 5, React Router v6, xUnit 2, Moq 4, Microsoft.EntityFrameworkCore.InMemory

---

## File Map

### DCF.ScoreScraper (existing — convert to library)
- `DCF.ScoreScraper/DCF.ScoreScraper.csproj` — remove `<OutputType>Exe</OutputType>`, add HtmlAgilityPack
- `DCF.ScoreScraper/Models/Result.cs` — replace with worktree version (Percussion2, renamed GE fields)
- `DCF.ScoreScraper/Services/ICorpsService.cs` — replace misspelled file
- `DCF.ScoreScraper/Services/CorpsService.cs` — replace misspelled file
- `DCF.ScoreScraper/Tasks/IRecapScraperTask.cs` — add from worktree
- `DCF.ScoreScraper/Tasks/RecapScraperTask.cs` — add from worktree
- `DCF.ScoreScraper/ServiceCollectionExtensions.cs` — update registrations

### DCF.Data (new)
- `DCF.Data/DCF.Data.csproj`
- `DCF.Data/Entities/SeasonEntity.cs`
- `DCF.Data/Entities/CorpsEntity.cs`
- `DCF.Data/Entities/ShowEntity.cs`
- `DCF.Data/Entities/ShowCorpsEntity.cs`
- `DCF.Data/Entities/ScoreEntity.cs`
- `DCF.Data/Entities/UserEntity.cs`
- `DCF.Data/Entities/LeagueEntity.cs`
- `DCF.Data/Entities/LeagueMemberEntity.cs`
- `DCF.Data/Entities/DraftPickEntity.cs`
- `DCF.Data/DcfDbContext.cs`

### DCF.Api (new)
- `DCF.Api/DCF.Api.csproj`
- `DCF.Api/Program.cs`
- `DCF.Api/appsettings.json`
- `DCF.Api/Controllers/AuthController.cs`
- `DCF.Api/Controllers/AdminController.cs`
- `DCF.Api/Controllers/LeaguesController.cs`
- `DCF.Api/Controllers/DraftController.cs`
- `DCF.Api/Services/MqttPublisherService.cs`
- `DCF.Api/Services/ScrapeSchedulerService.cs`
- `DCF.Api/Services/DraftSchedulerService.cs`
- `DCF.Api/Services/StandingsService.cs`
- `DCF.Api/Services/DraftService.cs`
- `DCF.Api/Models/` (request/response DTOs)
- `DCF.Api/Dockerfile`

### DCF.Tests (new)
- `DCF.Tests/DCF.Tests.csproj`
- `DCF.Tests/Services/StandingsServiceTests.cs`
- `DCF.Tests/Services/DraftServiceTests.cs`

### DCF.Web (new)
- `DCF.Web/package.json`
- `DCF.Web/vite.config.ts`
- `DCF.Web/index.html`
- `DCF.Web/src/main.tsx`
- `DCF.Web/src/App.tsx`
- `DCF.Web/src/api/client.ts`
- `DCF.Web/src/mqtt/useMqtt.ts`
- `DCF.Web/src/types/api.ts`
- `DCF.Web/src/components/ProtectedRoute.tsx`
- `DCF.Web/src/components/AdminRoute.tsx`
- `DCF.Web/src/pages/Home.tsx`
- `DCF.Web/src/pages/Leagues.tsx`
- `DCF.Web/src/pages/LeagueCreate.tsx`
- `DCF.Web/src/pages/LeagueDetail.tsx`
- `DCF.Web/src/pages/DraftRoom.tsx`
- `DCF.Web/src/pages/Admin.tsx`
- `DCF.Web/src/pages/Profile.tsx`

### Infrastructure
- `docker-compose.yml`
- `mosquitto/mosquitto.conf`

---

## Task 1: Merge Worktree Changes & Convert ScoreScraper to Class Library

The worktree at `.claude/worktrees/pensive-chatelet-050d01/` contains a more complete version of the scraper. Copy those files into the main branch and convert the project from an executable to a class library.

**Files:**
- Modify: `DCF.ScoreScraper/DCF.ScoreScraper.csproj`
- Replace: `DCF.ScoreScraper/Models/Result.cs`
- Replace: `DCF.ScoreScraper/Services/ICorpsService.cs` (fix spelling)
- Replace: `DCF.ScoreScraper/Services/CorpsService.cs` (fix spelling)
- Create: `DCF.ScoreScraper/Tasks/IRecapScraperTask.cs`
- Create: `DCF.ScoreScraper/Tasks/RecapScraperTask.cs`
- Delete: `DCF.ScoreScraper/Services/ICorpsSevice.cs`
- Delete: `DCF.ScoreScraper/Services/CorpsSevice.cs`
- Modify: `DCF.ScoreScraper/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Update the csproj — remove OutputType, add HtmlAgilityPack**

Replace `DCF.ScoreScraper/DCF.ScoreScraper.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" Version="1.11.72" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Replace Result.cs with the updated version from the worktree**

Replace `DCF.ScoreScraper/Models/Result.cs`:
```csharp
namespace DCF.ScoreScraper.Models
{
    public class Result
    {
        public required int Id { get; set; }
        public required Corps Corps { get; set; }
        public required Show Show { get; set; }
        public Score? GeneralEffectMusic1 { get; set; }
        public Score? GeneralEffectMusic2 { get; set; }
        public Score? GeneralEffectVisual1 { get; set; }
        public Score? GeneralEffectVisual2 { get; set; }
        public Score? GeneralEffect { get; set; }
        public Score? VisualAnalysis { get; set; }
        public Score? VisualProficiency { get; set; }
        public Score? ColorGuard { get; set; }
        public Score? Visual { get; set; }
        public Score? Brass { get; set; }
        public Score? MusicAnalysis { get; set; }
        public Score? Percussion1 { get; set; }
        public Score? Percussion2 { get; set; }
        public Score? Music { get; set; }
        public Score? SubTotal { get; set; }
        public Score? Penalty { get; set; }
        public Score? Total { get; set; }
    }
}
```

- [ ] **Step 3: Delete old misspelled service files**

Delete `DCF.ScoreScraper/Services/ICorpsSevice.cs` and `DCF.ScoreScraper/Services/CorpsSevice.cs`.

- [ ] **Step 4: Create ICorpsService.cs**

Create `DCF.ScoreScraper/Services/ICorpsService.cs`:
```csharp
using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services;

public interface ICorpsService
{
    Dictionary<string, Corps> GetCorps();
}
```

- [ ] **Step 5: Create CorpsService.cs (stub — populated from DB in Task 8)**

Create `DCF.ScoreScraper/Services/CorpsService.cs`:
```csharp
using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Services;

public class CorpsService : ICorpsService
{
    private readonly Dictionary<string, Corps> _corps;

    public CorpsService(IEnumerable<Corps> corps)
    {
        _corps = corps.ToDictionary(c => c.Name, c => c);
    }

    public Dictionary<string, Corps> GetCorps() => _corps;
}
```

- [ ] **Step 6: Create IRecapScraperTask.cs**

Create `DCF.ScoreScraper/Tasks/IRecapScraperTask.cs`:
```csharp
using DCF.ScoreScraper.Models;

namespace DCF.ScoreScraper.Tasks;

public interface IRecapScraperTask
{
    Task<List<Result>> ScrapeAsync(Show show);
}
```

- [ ] **Step 7: Create RecapScraperTask.cs (copy from worktree)**

Create `DCF.ScoreScraper/Tasks/RecapScraperTask.cs` with the contents from `.claude/worktrees/pensive-chatelet-050d01/DCF.ScoreScraper/Tasks/RecapScraperTask.cs` (the full implementation is already written there — copy verbatim).

- [ ] **Step 8: Update ServiceCollectionExtensions.cs**

Replace `DCF.ScoreScraper/ServiceCollectionExtensions.cs`:
```csharp
using DCF.ScoreScraper.Models;
using DCF.ScoreScraper.Services;
using DCF.ScoreScraper.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DCF.ScoreScraper;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScoreScraper(
        this IServiceCollection services,
        IEnumerable<Corps> corps)
    {
        services.AddSingleton<ICorpsService>(new CorpsService(corps));
        services.AddHttpClient<IRecapScraperTask, RecapScraperTask>();
        return services;
    }
}
```

- [ ] **Step 9: Verify the project builds**

```
dotnet build DCF.ScoreScraper/DCF.ScoreScraper.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit**

```bash
git add DCF.ScoreScraper/
git commit -m "feat: convert ScoreScraper to class library, merge worktree scraper impl"
```

---

## Task 2: Docker Compose — PostgreSQL + Mosquitto

**Files:**
- Create: `docker-compose.yml`
- Create: `mosquitto/mosquitto.conf`

- [ ] **Step 1: Create mosquitto.conf**

Create `mosquitto/mosquitto.conf`:
```
listener 1883
listener 9001
protocol websockets
allow_anonymous true
persistence false
```

- [ ] **Step 2: Create docker-compose.yml**

Create `docker-compose.yml`:
```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: dcf
      POSTGRES_USER: dcf
      POSTGRES_PASSWORD: dcf_dev
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data

  mosquitto:
    image: eclipse-mosquitto:2
    ports:
      - "1883:1883"
      - "9001:9001"
    volumes:
      - ./mosquitto/mosquitto.conf:/mosquitto/config/mosquitto.conf

  api:
    build:
      context: .
      dockerfile: DCF.Api/Dockerfile
    ports:
      - "5000:8080"
    environment:
      ConnectionStrings__Default: "Host=postgres;Database=dcf;Username=dcf;Password=dcf_dev"
      Mqtt__Host: mosquitto
      Mqtt__Port: "1883"
      Auth0__Domain: "${AUTH0_DOMAIN}"
      Auth0__Audience: "${AUTH0_AUDIENCE}"
      Scraper__DelayMinutes: "5"
    depends_on:
      - postgres
      - mosquitto

volumes:
  postgres_data:
```

- [ ] **Step 3: Start infrastructure services only and verify**

```
docker compose up postgres mosquitto -d
docker compose ps
```

Expected: Both services show `running`.

- [ ] **Step 4: Commit**

```bash
git add docker-compose.yml mosquitto/
git commit -m "feat: add Docker Compose for PostgreSQL and Mosquitto"
```

---

## Task 3: DCF.Data — Entities and DbContext

**Files:**
- Create: `DCF.Data/DCF.Data.csproj`
- Create: `DCF.Data/Entities/*.cs` (9 entity files)
- Create: `DCF.Data/DcfDbContext.cs`
- Modify: `DCF.ScoreScraper.slnx`

- [ ] **Step 1: Create DCF.Data.csproj**

Create `DCF.Data/DCF.Data.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DCF.ScoreScraper\DCF.ScoreScraper.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create SeasonEntity.cs**

Create `DCF.Data/Entities/SeasonEntity.cs`:
```csharp
namespace DCF.Data.Entities;

public class SeasonEntity
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public bool IsActive { get; set; }

    public List<ShowEntity> Shows { get; set; } = [];
    public List<LeagueEntity> Leagues { get; set; } = [];
    public List<SeasonCorpsEntity> SeasonCorps { get; set; } = [];
}
```

- [ ] **Step 3: Create CorpsEntity.cs**

Create `DCF.Data/Entities/CorpsEntity.cs`:
```csharp
namespace DCF.Data.Entities;

public class CorpsEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<SeasonCorpsEntity> SeasonCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
```

- [ ] **Step 4: Create SeasonCorpsEntity.cs (join: which corps are in a season)**

Create `DCF.Data/Entities/SeasonCorpsEntity.cs`:
```csharp
namespace DCF.Data.Entities;

public class SeasonCorpsEntity
{
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
}
```

- [ ] **Step 5: Create ShowEntity.cs**

Create `DCF.Data/Entities/ShowEntity.cs`:
```csharp
namespace DCF.Data.Entities;

public class ShowEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTimeOffset ScoresAnnouncedTime { get; set; }
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
}
```

- [ ] **Step 6: Create ShowCorpsEntity.cs**

Create `DCF.Data/Entities/ShowCorpsEntity.cs`:
```csharp
namespace DCF.Data.Entities;

public class ShowCorpsEntity
{
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
}
```

- [ ] **Step 7: Create ScoreEntity.cs**

Create `DCF.Data/Entities/ScoreEntity.cs`:
```csharp
using DCF.ScoreScraper.Models;

namespace DCF.Data.Entities;

public class ScoreEntity
{
    public Guid Id { get; set; }
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public Caption Caption { get; set; }
    public string? Judge { get; set; }
    public double RepertoireScore { get; set; }
    public double PerformanceScore { get; set; }
    public double TotalScore { get; set; }
    public int RepertoireRank { get; set; }
    public int PerformanceRank { get; set; }
    public int TotalRank { get; set; }
}
```

- [ ] **Step 8: Create UserEntity.cs**

Create `DCF.Data/Entities/UserEntity.cs`:
```csharp
namespace DCF.Data.Entities;

public class UserEntity
{
    public Guid Id { get; set; }
    public string Auth0Sub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    public List<LeagueMemberEntity> LeagueMemberships { get; set; } = [];
    public List<LeagueEntity> CommissionedLeagues { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
```

- [ ] **Step 9: Create LeagueEntity.cs**

Create `DCF.Data/Entities/LeagueEntity.cs`:
```csharp
using DCF.ScoreScraper.Models;

namespace DCF.Data.Entities;

public enum DraftStatus { NotStarted, Scheduled, InProgress, Completed }

public class LeagueEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;
    public Guid CommissionerUserId { get; set; }
    public UserEntity Commissioner { get; set; } = null!;
    public bool IsPublic { get; set; }
    public string InviteCode { get; set; } = string.Empty;
    public int CorpsPerCaption { get; set; }
    public Caption[] DraftableCaptions { get; set; } = [];
    public DraftStatus DraftStatus { get; set; } = DraftStatus.NotStarted;
    public DateTimeOffset? DraftStartTime { get; set; }
    public string DraftOrderJson { get; set; } = "[]";
    public int CurrentPickNumber { get; set; }

    public List<LeagueMemberEntity> Members { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
```

- [ ] **Step 10: Create LeagueMemberEntity.cs**

Create `DCF.Data/Entities/LeagueMemberEntity.cs`:
```csharp
namespace DCF.Data.Entities;

public class LeagueMemberEntity
{
    public Guid LeagueId { get; set; }
    public LeagueEntity League { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
}
```

- [ ] **Step 11: Create DraftPickEntity.cs**

Create `DCF.Data/Entities/DraftPickEntity.cs`:
```csharp
using DCF.ScoreScraper.Models;

namespace DCF.Data.Entities;

public class DraftPickEntity
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public LeagueEntity League { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public Caption Caption { get; set; }
    public int PickNumber { get; set; }
    public int RoundNumber { get; set; }
}
```

- [ ] **Step 12: Create DcfDbContext.cs**

Create `DCF.Data/DcfDbContext.cs`:
```csharp
using System.Text.Json;
using DCF.Data.Entities;
using DCF.ScoreScraper.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Data;

public class DcfDbContext(DbContextOptions<DcfDbContext> options) : DbContext(options)
{
    public DbSet<SeasonEntity> Seasons => Set<SeasonEntity>();
    public DbSet<CorpsEntity> Corps => Set<CorpsEntity>();
    public DbSet<SeasonCorpsEntity> SeasonCorps => Set<SeasonCorpsEntity>();
    public DbSet<ShowEntity> Shows => Set<ShowEntity>();
    public DbSet<ShowCorpsEntity> ShowCorps => Set<ShowCorpsEntity>();
    public DbSet<ScoreEntity> Scores => Set<ScoreEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<LeagueEntity> Leagues => Set<LeagueEntity>();
    public DbSet<LeagueMemberEntity> LeagueMembers => Set<LeagueMemberEntity>();
    public DbSet<DraftPickEntity> DraftPicks => Set<DraftPickEntity>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<SeasonCorpsEntity>().HasKey(e => new { e.SeasonId, e.CorpsId });
        mb.Entity<ShowCorpsEntity>().HasKey(e => new { e.ShowId, e.CorpsId });
        mb.Entity<LeagueMemberEntity>().HasKey(e => new { e.LeagueId, e.UserId });

        mb.Entity<DraftPickEntity>()
            .HasIndex(e => new { e.LeagueId, e.CorpsId, e.Caption })
            .IsUnique();

        mb.Entity<UserEntity>()
            .HasIndex(e => e.Auth0Sub)
            .IsUnique();

        mb.Entity<LeagueEntity>()
            .Property(e => e.DraftableCaptions)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Caption[]>(v, JsonSerializerOptions.Default) ?? []);
    }
}
```

- [ ] **Step 13: Add DCF.Data to solution file**

Replace `DCF.ScoreScraper.slnx`:
```xml
<Solution>
  <Project Path="DCF.ScoreScraper/DCF.ScoreScraper.csproj" />
  <Project Path="DCF.Data/DCF.Data.csproj" />
</Solution>
```

- [ ] **Step 14: Build DCF.Data**

```
dotnet build DCF.Data/DCF.Data.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 15: Commit**

```bash
git add DCF.Data/ DCF.ScoreScraper.slnx
git commit -m "feat: add DCF.Data project with EF Core entities and DbContext"
```

---

## Task 4: EF Core Migration

- [ ] **Step 1: Install EF Core tools (if not already installed)**

```
dotnet tool install --global dotnet-ef
```

- [ ] **Step 2: Start PostgreSQL**

```
docker compose up postgres -d
```

- [ ] **Step 3: Create initial migration**

Run from the repo root:
```
dotnet ef migrations add InitialCreate --project DCF.Data --startup-project DCF.Data -- --connection "Host=localhost;Database=dcf;Username=dcf;Password=dcf_dev"
```

Note: DCF.Data needs a design-time factory. Create `DCF.Data/DcfDbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DCF.Data;

public class DcfDbContextFactory : IDesignTimeDbContextFactory<DcfDbContext>
{
    public DcfDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DcfDbContext>()
            .UseNpgsql(args.FirstOrDefault()
                ?? "Host=localhost;Database=dcf;Username=dcf;Password=dcf_dev")
            .Options;
        return new DcfDbContext(options);
    }
}
```

Then re-run:
```
dotnet ef migrations add InitialCreate --project DCF.Data
```

- [ ] **Step 4: Apply migration**

```
dotnet ef database update --project DCF.Data
```

Expected: Migration applied, tables created.

- [ ] **Step 5: Commit**

```bash
git add DCF.Data/
git commit -m "feat: add initial EF Core migration"
```

---

## Task 5: DCF.Tests Setup + DraftService Tests (TDD)

**Files:**
- Create: `DCF.Tests/DCF.Tests.csproj`
- Create: `DCF.Tests/Services/DraftServiceTests.cs`
- Create: `DCF.Tests/Services/StandingsServiceTests.cs`

- [ ] **Step 1: Create DCF.Tests.csproj**

Create `DCF.Tests/DCF.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DCF.Data\DCF.Data.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write DraftService snake order tests**

Create `DCF.Tests/Services/DraftServiceTests.cs`:
```csharp
using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class DraftServiceTests
{
    [Fact]
    public void GetCurrentDrafter_Round0Pick0_ReturnsFirstUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("a", DraftService.GetCurrentDrafter(order, 0));
    }

    [Fact]
    public void GetCurrentDrafter_Round0Pick2_ReturnsLastUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("c", DraftService.GetCurrentDrafter(order, 2));
    }

    [Fact]
    public void GetCurrentDrafter_Round1Pick0_ReturnsLastUser()
    {
        // Round 1 (second round) snakes: C, B, A
        var order = new[] { "a", "b", "c" };
        Assert.Equal("c", DraftService.GetCurrentDrafter(order, 3));
    }

    [Fact]
    public void GetCurrentDrafter_Round1Pick1_ReturnsMiddleUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("b", DraftService.GetCurrentDrafter(order, 4));
    }

    [Fact]
    public void GetCurrentDrafter_Round2Pick0_ReturnsFirstUser()
    {
        // Round 2 (third round) snakes forward again: A, B, C
        var order = new[] { "a", "b", "c" };
        Assert.Equal("a", DraftService.GetCurrentDrafter(order, 6));
    }
}
```

- [ ] **Step 3: Run tests — expect compile failure (DraftService doesn't exist yet)**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: Compile error — `DCF.Api.Services.DraftService` not found. This confirms the test is driving the implementation.

- [ ] **Step 4: Write StandingsService tests**

Create `DCF.Tests/Services/StandingsServiceTests.cs`:
```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.ScoreScraper.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class StandingsServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task GetStandings_AveragesCorpsScoresPerCaption()
    {
        using var db = CreateDb("standings_avg");

        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, IsActive = true };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var corps3 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity { Id = Guid.NewGuid(), Name = "Finals", Url = "", Date = DateTime.UtcNow, SeasonId = season.Id, Season = season };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|1", Email = "a@b.com", DisplayName = "Alice" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "Test", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user,
            InviteCode = "ABCD1234", CorpsPerCaption = 3,
            DraftableCaptions = [Caption.Brass],
            DraftStatus = DraftStatus.Completed,
            DraftOrderJson = $"[\"{user.Id}\"]"
        };

        db.Seasons.Add(season);
        db.Corps.AddRange(corps1, corps2, corps3);
        db.Shows.Add(show);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        db.DraftPicks.AddRange(
            new DraftPickEntity { Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id, CorpsId = corps1.Id, Caption = Caption.Brass, PickNumber = 0, RoundNumber = 0, League = league, User = user, Corps = corps1 },
            new DraftPickEntity { Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id, CorpsId = corps2.Id, Caption = Caption.Brass, PickNumber = 1, RoundNumber = 0, League = league, User = user, Corps = corps2 },
            new DraftPickEntity { Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id, CorpsId = corps3.Id, Caption = Caption.Brass, PickNumber = 2, RoundNumber = 0, League = league, User = user, Corps = corps3 }
        );
        db.Scores.AddRange(
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps1.Id, ShowId = show.Id, Caption = Caption.Brass, TotalScore = 80.0, Corps = corps1, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps2.Id, ShowId = show.Id, Caption = Caption.Brass, TotalScore = 85.0, Corps = corps2, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps3.Id, ShowId = show.Id, Caption = Caption.Brass, TotalScore = 90.0, Corps = corps3, Show = show }
        );
        await db.SaveChangesAsync();

        var service = new StandingsService(db);
        var standings = await service.GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.Equal((80.0 + 85.0 + 90.0) / 3, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_UsesLatestShowScore()
    {
        using var db = CreateDb("standings_latest");

        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, IsActive = true };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show1 = new ShowEntity { Id = Guid.NewGuid(), Name = "Show1", Url = "", Date = DateTime.UtcNow.AddDays(-7), SeasonId = season.Id, Season = season };
        var show2 = new ShowEntity { Id = Guid.NewGuid(), Name = "Show2", Url = "", Date = DateTime.UtcNow, SeasonId = season.Id, Season = season };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|2", Email = "b@c.com", DisplayName = "Bob" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L2", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user,
            InviteCode = "XYZ12345", CorpsPerCaption = 1,
            DraftableCaptions = [Caption.Brass],
            DraftStatus = DraftStatus.Completed,
            DraftOrderJson = $"[\"{user.Id}\"]"
        };

        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.AddRange(show1, show2);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        db.DraftPicks.Add(new DraftPickEntity { Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id, CorpsId = corps.Id, Caption = Caption.Brass, PickNumber = 0, RoundNumber = 0, League = league, User = user, Corps = corps });
        db.Scores.AddRange(
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show1.Id, Caption = Caption.Brass, TotalScore = 70.0, Corps = corps, Show = show1 },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show2.Id, Caption = Caption.Brass, TotalScore = 88.5, Corps = corps, Show = show2 }
        );
        await db.SaveChangesAsync();

        var service = new StandingsService(db);
        var standings = await service.GetStandingsAsync(league.Id);

        Assert.Equal(88.5, standings[0].Score, precision: 5);
    }
}
```

- [ ] **Step 5: Add DCF.Tests to solution**

Update `DCF.ScoreScraper.slnx`:
```xml
<Solution>
  <Project Path="DCF.ScoreScraper/DCF.ScoreScraper.csproj" />
  <Project Path="DCF.Data/DCF.Data.csproj" />
  <Project Path="DCF.Tests/DCF.Tests.csproj" />
</Solution>
```

- [ ] **Step 6: Commit**

```bash
git add DCF.Tests/ DCF.ScoreScraper.slnx
git commit -m "test: add xUnit project with DraftService and StandingsService tests"
```

---

## Task 6: DCF.Api — Project Setup, Auth0, User Upsert

**Files:**
- Create: `DCF.Api/DCF.Api.csproj`
- Create: `DCF.Api/Program.cs`
- Create: `DCF.Api/appsettings.json`
- Create: `DCF.Api/Controllers/AuthController.cs`

- [ ] **Step 1: Create DCF.Api.csproj**

Create `DCF.Api/DCF.Api.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageReference Include="MQTTnet" Version="4.3.7" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DCF.Data\DCF.Data.csproj" />
    <ProjectReference Include="..\DCF.ScoreScraper\DCF.ScoreScraper.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create appsettings.json**

Create `DCF.Api/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=dcf;Username=dcf;Password=dcf_dev"
  },
  "Auth0": {
    "Domain": "YOUR_AUTH0_DOMAIN",
    "Audience": "YOUR_AUTH0_AUDIENCE"
  },
  "Mqtt": {
    "Host": "localhost",
    "Port": 1883
  },
  "Scraper": {
    "DelayMinutes": 5
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

- [ ] **Step 3: Create Program.cs**

Create `DCF.Api/Program.cs`:
```csharp
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DcfDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.Authority = $"https://{builder.Configuration["Auth0:Domain"]}/";
        opt.Audience = builder.Configuration["Auth0:Audience"];
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSingleton<IMqttPublisherService, MqttPublisherService>();
builder.Services.AddHostedService(sp => (MqttPublisherService)sp.GetRequiredService<IMqttPublisherService>());

builder.Services.AddSingleton<ScrapeSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScrapeSchedulerService>());

builder.Services.AddSingleton<DraftSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DraftSchedulerService>());

builder.Services.AddScoped<StandingsService>();
builder.Services.AddScoped<DraftService>();

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:5173")
     .AllowAnyMethod()
     .AllowAnyHeader()));

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

- [ ] **Step 4: Create AuthController.cs**

Create `DCF.Api/Controllers/AuthController.cs`:
```csharp
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(DcfDbContext db) : ControllerBase
{
    [HttpPost("me")]
    public async Task<IActionResult> UpsertUser()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = User.FindFirstValue("name") ?? email;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);
        if (user is null)
        {
            user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = sub, Email = email, DisplayName = name };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        return Ok(new { user.Id, user.Email, user.DisplayName, user.IsAdmin });
    }
}
```

- [ ] **Step 5: Add DCF.Api to solution**

Update `DCF.ScoreScraper.slnx`:
```xml
<Solution>
  <Project Path="DCF.ScoreScraper/DCF.ScoreScraper.csproj" />
  <Project Path="DCF.Data/DCF.Data.csproj" />
  <Project Path="DCF.Api/DCF.Api.csproj" />
  <Project Path="DCF.Tests/DCF.Tests.csproj" />
</Solution>
```

- [ ] **Step 6: Build DCF.Api**

```
dotnet build DCF.Api/DCF.Api.csproj
```

Expected: Build succeeded (some services don't exist yet — add stubs if needed to compile).

- [ ] **Step 7: Commit**

```bash
git add DCF.Api/ DCF.ScoreScraper.slnx
git commit -m "feat: add DCF.Api project with Auth0 JWT and user upsert endpoint"
```

---

## Task 7: Admin API Endpoints

**Files:**
- Create: `DCF.Api/Controllers/AdminController.cs`
- Create: `DCF.Api/Models/AdminRequests.cs`

- [ ] **Step 1: Create request DTOs**

Create `DCF.Api/Models/AdminRequests.cs`:
```csharp
namespace DCF.Api.Models;

public record CreateSeasonRequest(int Year);
public record CreateCorpsRequest(string Name);
public record CreateShowRequest(
    string Name,
    string Url,
    DateTime Date,
    DateTimeOffset ScoresAnnouncedTime,
    Guid SeasonId,
    List<Guid> CorpsIds);
public record UpdateShowRequest(
    string Name,
    string Url,
    DateTime Date,
    DateTimeOffset ScoresAnnouncedTime,
    List<Guid> CorpsIds);
public record SetSeasonCorpsRequest(List<Guid> CorpsIds);
```

- [ ] **Step 2: Create AdminController.cs**

Create `DCF.Api/Controllers/AdminController.cs`:
```csharp
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(
    DcfDbContext db,
    ScrapeSchedulerService scrapeScheduler,
    IMqttPublisherService mqtt) : ControllerBase
{
    private async Task<bool> IsAdminAsync()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return await db.Users.AnyAsync(u => u.Auth0Sub == sub && u.IsAdmin);
    }

    // --- Seasons ---

    [HttpGet("seasons")]
    public async Task<IActionResult> GetSeasons()
    {
        if (!await IsAdminAsync()) return Forbid();
        var seasons = await db.Seasons.OrderByDescending(s => s.Year).ToListAsync();
        return Ok(seasons.Select(s => new { s.Id, s.Year, s.IsActive }));
    }

    [HttpPost("seasons")]
    public async Task<IActionResult> CreateSeason(CreateSeasonRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = req.Year };
        db.Seasons.Add(season);
        await db.SaveChangesAsync();
        return Ok(new { season.Id, season.Year, season.IsActive });
    }

    [HttpPut("seasons/{id}/activate")]
    public async Task<IActionResult> ActivateSeason(Guid id)
    {
        if (!await IsAdminAsync()) return Forbid();
        await db.Seasons.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await db.Seasons.Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        return NoContent();
    }

    // --- Corps ---

    [HttpGet("corps")]
    public async Task<IActionResult> GetCorps()
    {
        if (!await IsAdminAsync()) return Forbid();
        var corps = await db.Corps.OrderBy(c => c.Name).ToListAsync();
        return Ok(corps.Select(c => new { c.Id, c.Name }));
    }

    [HttpPost("corps")]
    public async Task<IActionResult> CreateCorps(CreateCorpsRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = req.Name };
        db.Corps.Add(corps);
        await db.SaveChangesAsync();
        return Ok(new { corps.Id, corps.Name });
    }

    [HttpPut("seasons/{seasonId}/corps")]
    public async Task<IActionResult> SetSeasonCorps(Guid seasonId, SetSeasonCorpsRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var existing = db.SeasonCorps.Where(sc => sc.SeasonId == seasonId);
        db.SeasonCorps.RemoveRange(existing);
        db.SeasonCorps.AddRange(req.CorpsIds.Select(cId =>
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = cId }));
        await db.SaveChangesAsync();
        return NoContent();
    }

    // --- Shows ---

    [HttpGet("seasons/{seasonId}/shows")]
    public async Task<IActionResult> GetShows(Guid seasonId)
    {
        if (!await IsAdminAsync()) return Forbid();
        var shows = await db.Shows
            .Where(s => s.SeasonId == seasonId)
            .Include(s => s.ShowCorps)
            .OrderBy(s => s.Date)
            .ToListAsync();
        return Ok(shows.Select(s => new
        {
            s.Id, s.Name, s.Url, s.Date, s.ScoresAnnouncedTime,
            CorpsIds = s.ShowCorps.Select(sc => sc.CorpsId)
        }));
    }

    [HttpPost("seasons/{seasonId}/shows")]
    public async Task<IActionResult> CreateShow(Guid seasonId, CreateShowRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = req.Name, Url = req.Url,
            Date = req.Date, ScoresAnnouncedTime = req.ScoresAnnouncedTime, SeasonId = seasonId
        };
        db.Shows.Add(show);
        db.ShowCorps.AddRange(req.CorpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));
        await db.SaveChangesAsync();
        scrapeScheduler.ScheduleScrape(show);
        return Ok(new { show.Id, show.Name });
    }

    [HttpPut("shows/{id}")]
    public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
    {
        if (!await IsAdminAsync()) return Forbid();
        var show = await db.Shows.FindAsync(id);
        if (show is null) return NotFound();
        show.Name = req.Name; show.Url = req.Url;
        show.Date = req.Date; show.ScoresAnnouncedTime = req.ScoresAnnouncedTime;
        var existing = db.ShowCorps.Where(sc => sc.ShowId == id);
        db.ShowCorps.RemoveRange(existing);
        db.ShowCorps.AddRange(req.CorpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = id, CorpsId = cId }));
        await db.SaveChangesAsync();
        scrapeScheduler.ScheduleScrape(show);
        return NoContent();
    }

    // --- Manual scrape trigger ---

    [HttpPost("shows/{id}/scrape")]
    public async Task<IActionResult> TriggerScrape(Guid id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var show = await db.Shows.Include(s => s.ShowCorps).FirstOrDefaultAsync(s => s.Id == id);
        if (show is null) return NotFound();
        await scrapeScheduler.ExecuteScrapeAsync(show);
        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = id });
        return Ok();
    }
}
```

- [ ] **Step 3: Build**

```
dotnet build DCF.Api/DCF.Api.csproj
```

- [ ] **Step 4: Commit**

```bash
git add DCF.Api/Controllers/AdminController.cs DCF.Api/Models/AdminRequests.cs
git commit -m "feat: add admin API endpoints for seasons, corps, and shows"
```

---

## Task 8: MqttPublisherService

**Files:**
- Create: `DCF.Api/Services/MqttPublisherService.cs`

- [ ] **Step 1: Create MqttPublisherService.cs**

Create `DCF.Api/Services/MqttPublisherService.cs`:
```csharp
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;

namespace DCF.Api.Services;

public interface IMqttPublisherService
{
    Task PublishAsync(string topic, object payload, CancellationToken ct = default);
}

public class MqttPublisherService : IMqttPublisherService, IHostedService
{
    private readonly IMqttClient _client;
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<MqttPublisherService> _logger;

    public MqttPublisherService(IConfiguration config, ILogger<MqttPublisherService> logger)
    {
        _host = config["Mqtt:Host"] ?? "localhost";
        _port = int.Parse(config["Mqtt:Port"] ?? "1883");
        _logger = logger;
        _client = new MqttFactory().CreateMqttClient();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_host, _port)
            .WithCleanSession()
            .Build();
        await _client.ConnectAsync(options, ct);
        _logger.LogInformation("MQTT connected to {Host}:{Port}", _host, _port);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
            await _client.DisconnectAsync(cancellationToken: ct);
    }

    public async Task PublishAsync(string topic, object payload, CancellationToken ct = default)
    {
        if (!_client.IsConnected) return;
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await _client.PublishAsync(message, ct);
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build DCF.Api/DCF.Api.csproj
```

- [ ] **Step 3: Commit**

```bash
git add DCF.Api/Services/MqttPublisherService.cs
git commit -m "feat: add MqttPublisherService using MQTTnet"
```

---

## Task 9: CorpsService from DB + ScrapeSchedulerService

**Files:**
- Create: `DCF.Api/Services/ScrapeSchedulerService.cs`

The CorpsService in DCF.ScoreScraper takes a list of Corps in its constructor. The scraper scheduler will build that list from the DB before calling into the scraper.

- [ ] **Step 1: Create ScrapeSchedulerService.cs**

Create `DCF.Api/Services/ScrapeSchedulerService.cs`:
```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.ScoreScraper.Models;
using DCF.ScoreScraper.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public class ScrapeSchedulerService(
    IServiceScopeFactory scopeFactory,
    IMqttPublisherService mqtt,
    IConfiguration config,
    ILogger<ScrapeSchedulerService> logger) : BackgroundService
{
    private readonly Dictionary<Guid, CancellationTokenSource> _scheduled = new();
    private readonly int _delayMinutes = int.Parse(config["Scraper:DelayMinutes"] ?? "5");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
        var shows = await db.Shows
            .Include(s => s.Season)
            .Include(s => s.ShowCorps)
            .Where(s => s.Season.IsActive && s.ScoresAnnouncedTime > DateTimeOffset.UtcNow)
            .ToListAsync(stoppingToken);

        foreach (var show in shows)
            ScheduleScrape(show);
    }

    public void ScheduleScrape(ShowEntity show)
    {
        if (_scheduled.TryGetValue(show.Id, out var existing))
        {
            existing.Cancel();
            _scheduled.Remove(show.Id);
        }

        var cts = new CancellationTokenSource();
        _scheduled[show.Id] = cts;

        _ = Task.Run(async () =>
        {
            var fireAt = show.ScoresAnnouncedTime.AddMinutes(_delayMinutes);
            var delay = fireAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            await ExecuteScrapeAsync(show);
            await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });
        }, cts.Token);
    }

    public async Task ExecuteScrapeAsync(ShowEntity show)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var showCorpsIds = show.ShowCorps.Select(sc => sc.CorpsId).ToHashSet();
        var corpsList = await db.Corps
            .Where(c => showCorpsIds.Contains(c.Id))
            .ToListAsync();

        var scraperCorps = corpsList.Select(c => new Corps(c.Id, c.Name));
        var scraperShow = new Show(show.Id, show.Name, show.Url, show.Date);

        var scraper = new DCF.ScoreScraper.Tasks.RecapScraperTask(
            new DCF.ScoreScraper.Services.CorpsService(scraperCorps),
            scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient());

        List<DCF.ScoreScraper.Models.Result> results;
        try
        {
            results = await scraper.ScrapeAsync(scraperShow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape failed for show {ShowId}", show.Id);
            return;
        }

        var scores = results
            .SelectMany(r => EnumerateScores(r))
            .Where(s => showCorpsIds.Contains(s.CorpsId));

        foreach (var score in scores)
        {
            var existing = await db.Scores.FirstOrDefaultAsync(s =>
                s.CorpsId == score.CorpsId &&
                s.ShowId == score.ShowId &&
                s.Caption == score.Caption &&
                s.Judge == score.Judge);

            if (existing is null)
                db.Scores.Add(score);
            else
            {
                existing.TotalScore = score.TotalScore;
                existing.RepertoireScore = score.RepertoireScore;
                existing.PerformanceScore = score.PerformanceScore;
                existing.TotalRank = score.TotalRank;
            }
        }

        await db.SaveChangesAsync();
    }

    private static IEnumerable<ScoreEntity> EnumerateScores(DCF.ScoreScraper.Models.Result r)
    {
        Score?[] scores =
        [
            r.GeneralEffect, r.GeneralEffectMusic1, r.GeneralEffectMusic2,
            r.GeneralEffectVisual1, r.GeneralEffectVisual2,
            r.VisualAnalysis, r.VisualProficiency, r.ColorGuard, r.Visual,
            r.Brass, r.MusicAnalysis, r.Percussion1, r.Percussion2, r.Music,
            r.SubTotal, r.Penalty, r.Total
        ];

        return scores
            .OfType<Score>()
            .Select(s => new ScoreEntity
            {
                Id = s.Id,
                CorpsId = r.Corps.Id,
                ShowId = r.Show.Id,
                Caption = s.Caption,
                Judge = s.Judge,
                RepertoireScore = s.RepertoireScore,
                PerformanceScore = s.PerformanceScore,
                TotalScore = s.TotalScore,
                RepertoireRank = s.RepertoireRank,
                PerformanceRank = s.PerformanceRank,
                TotalRank = s.TotalRank
            });
    }
}
```

- [ ] **Step 2: Add IHttpClientFactory to Program.cs**

Add before `var app = builder.Build();` in `DCF.Api/Program.cs`:
```csharp
builder.Services.AddHttpClient();
```

- [ ] **Step 3: Build**

```
dotnet build DCF.Api/DCF.Api.csproj
```

- [ ] **Step 4: Commit**

```bash
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Api/Program.cs
git commit -m "feat: add ScrapeSchedulerService with scheduled and manual scrape support"
```

---

## Task 10: StandingsService + DraftService (make tests pass)

**Files:**
- Create: `DCF.Api/Services/StandingsService.cs`
- Create: `DCF.Api/Services/DraftService.cs`

- [ ] **Step 1: Create StandingsService.cs**

Create `DCF.Api/Services/StandingsService.cs`:
```csharp
using DCF.Data;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score);

public class StandingsService(DcfDbContext db)
{
    public async Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        var standings = new List<MemberStanding>();

        foreach (var member in members)
        {
            double totalScore = 0;

            foreach (var caption in league.DraftableCaptions)
            {
                var picks = await db.DraftPicks
                    .Where(p => p.LeagueId == leagueId &&
                                p.UserId == member.UserId &&
                                p.Caption == caption)
                    .ToListAsync();

                var captionScores = new List<double>();

                foreach (var pick in picks)
                {
                    var latestScore = await db.Scores
                        .Include(s => s.Show)
                        .Where(s => s.CorpsId == pick.CorpsId && s.Caption == caption)
                        .OrderByDescending(s => s.Show.Date)
                        .Select(s => (double?)s.TotalScore)
                        .FirstOrDefaultAsync();

                    if (latestScore.HasValue)
                        captionScores.Add(latestScore.Value);
                }

                if (captionScores.Count > 0)
                    totalScore += captionScores.Average();
            }

            standings.Add(new MemberStanding(member.UserId, member.User.DisplayName, totalScore));
        }

        return standings.OrderByDescending(s => s.Score).ToList();
    }
}
```

- [ ] **Step 2: Create DraftService.cs**

Create `DCF.Api/Services/DraftService.cs`:
```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.ScoreScraper.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DCF.Api.Services;

public class DraftService(DcfDbContext db, IMqttPublisherService mqtt)
{
    public static string GetCurrentDrafter(string[] draftOrder, int currentPickNumber)
    {
        int n = draftOrder.Length;
        int round = currentPickNumber / n;
        int positionInRound = currentPickNumber % n;
        int index = round % 2 == 0 ? positionInRound : n - 1 - positionInRound;
        return draftOrder[index];
    }

    public async Task StartDraftAsync(Guid leagueId)
    {
        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        var shuffled = league.Members
            .Select(m => m.UserId.ToString())
            .OrderBy(_ => Guid.NewGuid())
            .ToArray();

        league.DraftOrderJson = JsonSerializer.Serialize(shuffled);
        league.CurrentPickNumber = 0;
        league.DraftStatus = DraftStatus.InProgress;
        await db.SaveChangesAsync();

        await PublishDraftStateAsync(league);
    }

    public async Task<DraftPickEntity> SubmitPickAsync(
        Guid leagueId, Guid userId, Guid corpsId, Caption caption)
    {
        var league = await db.Leagues
            .Include(l => l.Members)
            .Include(l => l.DraftPicks)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.DraftStatus != DraftStatus.InProgress)
            throw new InvalidOperationException("Draft is not in progress");

        var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson)!;
        var currentDrafterId = GetCurrentDrafter(draftOrder, league.CurrentPickNumber);

        if (currentDrafterId != userId.ToString())
            throw new InvalidOperationException("Not your turn");

        var alreadyPicked = await db.DraftPicks.AnyAsync(p =>
            p.LeagueId == leagueId && p.CorpsId == corpsId && p.Caption == caption);
        if (alreadyPicked)
            throw new InvalidOperationException("That corps+caption is already drafted in this league");

        int totalPicks = league.Members.Count *
            (await db.Leagues.Where(l => l.Id == leagueId)
                .Select(l => l.DraftableCaptions.Length).FirstAsync()) *
            league.CorpsPerCaption;

        int round = league.CurrentPickNumber / draftOrder.Length;
        var pick = new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = leagueId, UserId = userId,
            CorpsId = corpsId, Caption = caption,
            PickNumber = league.CurrentPickNumber, RoundNumber = round
        };
        db.DraftPicks.Add(pick);

        league.CurrentPickNumber++;
        if (league.CurrentPickNumber >= totalPicks)
            league.DraftStatus = DraftStatus.Completed;

        await db.SaveChangesAsync();
        await PublishDraftStateAsync(league);
        return pick;
    }

    public async Task SkipCurrentPickAsync(Guid leagueId, Guid commissionerUserId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != commissionerUserId)
            throw new InvalidOperationException("Only the commissioner can skip picks");

        if (league.DraftStatus != DraftStatus.InProgress)
            throw new InvalidOperationException("Draft is not in progress");

        league.CurrentPickNumber++;
        await db.SaveChangesAsync();
        await PublishDraftStateAsync(league);
    }

    private async Task PublishDraftStateAsync(LeagueEntity league)
    {
        var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson) ?? [];
        var picks = await db.DraftPicks
            .Include(p => p.Corps)
            .Include(p => p.User)
            .Where(p => p.LeagueId == league.Id)
            .OrderBy(p => p.PickNumber)
            .ToListAsync();

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == league.Id)
            .ToListAsync();

        string? currentDrafterId = league.DraftStatus == DraftStatus.InProgress && draftOrder.Length > 0
            ? GetCurrentDrafter(draftOrder, league.CurrentPickNumber)
            : null;

        var payload = new
        {
            Status = league.DraftStatus.ToString(),
            league.DraftStartTime,
            league.CurrentPickNumber,
            CurrentDrafterId = currentDrafterId,
            Members = members.Select(m => new { m.UserId, m.User.DisplayName }),
            Picks = picks.Select(p => new
            {
                p.PickNumber, p.RoundNumber,
                UserId = p.UserId, p.User.DisplayName,
                CorpsId = p.CorpsId, CorpsName = p.Corps.Name,
                Caption = p.Caption.ToString()
            })
        };

        await mqtt.PublishAsync($"dcf/leagues/{league.Id}/draft", payload);
    }
}
```

- [ ] **Step 3: Add DCF.Api reference to DCF.Tests**

Update `DCF.Tests/DCF.Tests.csproj` to add:
```xml
<ProjectReference Include="..\DCF.Api\DCF.Api.csproj" />
```

- [ ] **Step 4: Run tests — they should now pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Services/StandingsService.cs DCF.Api/Services/DraftService.cs DCF.Tests/DCF.Tests.csproj
git commit -m "feat: implement StandingsService and DraftService; all tests pass"
```

---

## Task 11: DraftSchedulerService + Draft & League API Endpoints

**Files:**
- Create: `DCF.Api/Services/DraftSchedulerService.cs`
- Create: `DCF.Api/Controllers/LeaguesController.cs`
- Create: `DCF.Api/Controllers/DraftController.cs`
- Create: `DCF.Api/Models/LeagueRequests.cs`

- [ ] **Step 1: Create DraftSchedulerService.cs**

Create `DCF.Api/Services/DraftSchedulerService.cs`:
```csharp
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public class DraftSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<DraftSchedulerService> logger) : BackgroundService
{
    private readonly Dictionary<Guid, CancellationTokenSource> _scheduled = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
        var leagues = await db.Leagues
            .Where(l => l.DraftStatus == DraftStatus.Scheduled &&
                        l.DraftStartTime != null &&
                        l.DraftStartTime > DateTimeOffset.UtcNow)
            .ToListAsync(stoppingToken);

        foreach (var league in leagues)
            ScheduleDraftStart(league.Id, league.DraftStartTime!.Value);
    }

    public void ScheduleDraftStart(Guid leagueId, DateTimeOffset startTime)
    {
        if (_scheduled.TryGetValue(leagueId, out var existing))
        {
            existing.Cancel();
            _scheduled.Remove(leagueId);
        }

        var cts = new CancellationTokenSource();
        _scheduled[leagueId] = cts;

        _ = Task.Run(async () =>
        {
            var delay = startTime - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            using var scope = scopeFactory.CreateScope();
            var draftService = scope.ServiceProvider.GetRequiredService<DraftService>();
            try { await draftService.StartDraftAsync(leagueId); }
            catch (Exception ex) { logger.LogError(ex, "Auto-start draft failed for league {Id}", leagueId); }
        }, cts.Token);
    }

    public void CancelScheduled(Guid leagueId)
    {
        if (_scheduled.TryGetValue(leagueId, out var cts))
        {
            cts.Cancel();
            _scheduled.Remove(leagueId);
        }
    }
}
```

- [ ] **Step 2: Create LeagueRequests.cs**

Create `DCF.Api/Models/LeagueRequests.cs`:
```csharp
using DCF.ScoreScraper.Models;

namespace DCF.Api.Models;

public record CreateLeagueRequest(
    string Name,
    bool IsPublic,
    int CorpsPerCaption,
    Caption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime);

public record JoinLeagueRequest(string? InviteCode);

public record SubmitPickRequest(Guid CorpsId, Caption Caption);
```

- [ ] **Step 3: Create LeaguesController.cs**

Create `DCF.Api/Controllers/LeaguesController.cs`:
```csharp
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/leagues")]
[Authorize]
public class LeaguesController(
    DcfDbContext db,
    DraftSchedulerService draftScheduler,
    StandingsService standingsService) : ControllerBase
{
    private async Task<UserEntity?> GetUserAsync() =>
        await db.Users.FirstOrDefaultAsync(u =>
            u.Auth0Sub == (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")));

    [HttpGet]
    public async Task<IActionResult> Browse()
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var myLeagueIds = await db.LeagueMembers
            .Where(m => m.UserId == user.Id)
            .Select(m => m.LeagueId)
            .ToListAsync();

        var leagues = await db.Leagues
            .Include(l => l.Season)
            .Where(l => l.IsPublic || myLeagueIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id, l.Name, l.IsPublic, l.DraftStatus, l.DraftStartTime,
                SeasonYear = l.Season.Year, IsMember = myLeagueIds.Contains(l.Id),
                MemberCount = l.Members.Count
            })
            .ToListAsync();

        return Ok(leagues);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateLeagueRequest req)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var activeSeason = await db.Seasons.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSeason is null) return BadRequest("No active season");

        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            SeasonId = activeSeason.Id,
            CommissionerUserId = user.Id,
            IsPublic = req.IsPublic,
            InviteCode = GenerateInviteCode(),
            CorpsPerCaption = req.CorpsPerCaption,
            DraftableCaptions = req.DraftableCaptions,
            DraftStatus = req.DraftStartTime.HasValue ? DraftStatus.Scheduled : DraftStatus.NotStarted,
            DraftStartTime = req.DraftStartTime
        };
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        if (req.DraftStartTime.HasValue)
            draftScheduler.ScheduleDraftStart(league.Id, req.DraftStartTime.Value);

        return Ok(new { league.Id, league.Name, league.InviteCode });
    }

    [HttpPost("{id}/join")]
    public async Task<IActionResult> Join(Guid id, JoinLeagueRequest req)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var league = await db.Leagues.FindAsync(id);
        if (league is null) return NotFound();

        if (!league.IsPublic)
        {
            if (req.InviteCode != league.InviteCode)
                return BadRequest("Invalid invite code");
        }

        var already = await db.LeagueMembers.AnyAsync(m => m.LeagueId == id && m.UserId == user.Id);
        if (!already)
        {
            db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = id, UserId = user.Id });
            await db.SaveChangesAsync();
        }

        return Ok();
    }

    [HttpGet("{id}/standings")]
    public async Task<IActionResult> Standings(Guid id)
    {
        var standings = await standingsService.GetStandingsAsync(id);
        return Ok(standings);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var league = await db.Leagues
            .Include(l => l.Members).ThenInclude(m => m.User)
            .Include(l => l.DraftPicks).ThenInclude(p => p.Corps)
            .Include(l => l.Season)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (league is null) return NotFound();

        return Ok(new
        {
            league.Id, league.Name, league.IsPublic, league.InviteCode,
            league.DraftStatus, league.DraftStartTime, league.CorpsPerCaption,
            DraftableCaptions = league.DraftableCaptions.Select(c => c.ToString()),
            SeasonYear = league.Season.Year,
            Members = league.Members.Select(m => new { m.UserId, m.User.DisplayName }),
            Picks = league.DraftPicks.Select(p => new
            {
                p.UserId, p.CorpsId, CorpsName = p.Corps.Name,
                Caption = p.Caption.ToString(), p.PickNumber, p.RoundNumber
            })
        });
    }

    private static string GenerateInviteCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(6);
        return Convert.ToBase64String(bytes)
            .Replace("+", "A").Replace("/", "B").Replace("=", "")[..8]
            .ToUpper();
    }
}
```

- [ ] **Step 4: Create DraftController.cs**

Create `DCF.Api/Controllers/DraftController.cs`:
```csharp
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/leagues/{leagueId}/draft")]
[Authorize]
public class DraftController(DcfDbContext db, DraftService draftService) : ControllerBase
{
    private async Task<UserEntity?> GetUserAsync() =>
        await db.Users.FirstOrDefaultAsync(u =>
            u.Auth0Sub == (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")));

    [HttpPost("start")]
    public async Task<IActionResult> Start(Guid leagueId)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        var league = await db.Leagues.FindAsync(leagueId);
        if (league is null) return NotFound();
        if (league.CommissionerUserId != user.Id) return Forbid();
        if (league.DraftStatus == DraftStatus.InProgress) return BadRequest("Draft already started");

        await draftService.StartDraftAsync(leagueId);
        return Ok();
    }

    [HttpPost("pick")]
    public async Task<IActionResult> Pick(Guid leagueId, SubmitPickRequest req)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        try
        {
            var pick = await draftService.SubmitPickAsync(leagueId, user.Id, req.CorpsId, req.Caption);
            return Ok(new { pick.Id, pick.PickNumber });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip(Guid leagueId)
    {
        var user = await GetUserAsync();
        if (user is null) return Unauthorized();

        try
        {
            await draftService.SkipCurrentPickAsync(leagueId, user.Id);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

- [ ] **Step 5: Build and run tests**

```
dotnet build DCF.Api/DCF.Api.csproj
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Services/DraftSchedulerService.cs DCF.Api/Controllers/ DCF.Api/Models/LeagueRequests.cs
git commit -m "feat: add league and draft API endpoints with scheduler"
```

---

## Task 12: React Project Setup (Vite + Auth0 + Router)

**Files:**
- Create: `DCF.Web/package.json`, `vite.config.ts`, `index.html`, `.env.example`
- Create: `DCF.Web/src/main.tsx`, `App.tsx`, `api/client.ts`, `types/api.ts`
- Create: `DCF.Web/src/components/ProtectedRoute.tsx`, `AdminRoute.tsx`

- [ ] **Step 1: Scaffold the Vite React TypeScript project**

```
npm create vite@latest DCF.Web -- --template react-ts
cd DCF.Web
npm install
```

- [ ] **Step 2: Install dependencies**

```
npm install @auth0/auth0-react mqtt react-router-dom
npm install -D @types/node
```

- [ ] **Step 3: Create .env.example**

Create `DCF.Web/.env.example`:
```
VITE_AUTH0_DOMAIN=your-tenant.auth0.com
VITE_AUTH0_CLIENT_ID=your_client_id
VITE_AUTH0_AUDIENCE=https://your-api-audience
VITE_API_URL=http://localhost:5000
VITE_MQTT_URL=ws://localhost:9001
```

Copy to `.env.local` and fill in real values.

- [ ] **Step 4: Create src/types/api.ts**

Create `DCF.Web/src/types/api.ts`:
```typescript
export type DraftStatus = 'NotStarted' | 'Scheduled' | 'InProgress' | 'Completed';

export interface League {
  id: string;
  name: string;
  isPublic: boolean;
  inviteCode?: string;
  commissionerUserId?: string;
  draftStatus: DraftStatus;
  draftStartTime?: string;
  corpsPerCaption: number;
  draftableCaptions: string[];
  seasonYear: number;
  isMember?: boolean;
  memberCount?: number;
  members?: Member[];
  picks?: DraftPick[];
}

export interface Member {
  userId: string;
  displayName: string;
}

export interface DraftPick {
  userId: string;
  displayName: string;
  corpsId: string;
  corpsName: string;
  caption: string;
  pickNumber: number;
  roundNumber: number;
}

export interface Standing {
  userId: string;
  displayName: string;
  score: number;
}

export interface Corps {
  id: string;
  name: string;
}

export interface DraftState {
  status: DraftStatus;
  draftStartTime?: string;
  currentPickNumber: number;
  currentDrafterId?: string;
  members: Member[];
  picks: DraftPick[];
}

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
}
```

- [ ] **Step 5: Create src/api/client.ts**

Create `DCF.Web/src/api/client.ts`:
```typescript
const API_URL = import.meta.env.VITE_API_URL as string;

let getToken: (() => Promise<string>) | null = null;

export function setTokenGetter(fn: () => Promise<string>) {
  getToken = fn;
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const token = getToken ? await getToken() : null;
  const res = await fetch(`${API_URL}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options?.headers,
    },
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json() as Promise<T>;
}

export const api = {
  upsertUser: () => request<import('../types/api').UserProfile>('/api/auth/me', { method: 'POST' }),
  getLeagues: () => request<import('../types/api').League[]>('/api/leagues'),
  getLeague: (id: string) => request<import('../types/api').League>(`/api/leagues/${id}`),
  createLeague: (body: object) => request('/api/leagues', { method: 'POST', body: JSON.stringify(body) }),
  joinLeague: (id: string, inviteCode?: string) =>
    request(`/api/leagues/${id}/join`, { method: 'POST', body: JSON.stringify({ inviteCode }) }),
  getStandings: (id: string) => request<import('../types/api').Standing[]>(`/api/leagues/${id}/standings`),
  startDraft: (leagueId: string) =>
    request(`/api/leagues/${leagueId}/draft/start`, { method: 'POST' }),
  submitPick: (leagueId: string, corpsId: string, caption: string) =>
    request(`/api/leagues/${leagueId}/draft/pick`, {
      method: 'POST', body: JSON.stringify({ corpsId, caption }),
    }),
  skipPick: (leagueId: string) =>
    request(`/api/leagues/${leagueId}/draft/skip`, { method: 'POST' }),
  adminGetCorps: () => request<import('../types/api').Corps[]>('/api/admin/corps'),
};
```

Note: fix the import path for `UserProfile` — move it from the return type to `import('../types/api').UserProfile`.

- [ ] **Step 6: Create src/mqtt/useMqtt.ts**

Create `DCF.Web/src/mqtt/useMqtt.ts`:
```typescript
import mqtt, { MqttClient } from 'mqtt';
import { useEffect, useRef, useState } from 'react';

const MQTT_URL = import.meta.env.VITE_MQTT_URL as string;

export function useMqtt<T>(topic: string) {
  const [message, setMessage] = useState<T | null>(null);
  const clientRef = useRef<MqttClient | null>(null);

  useEffect(() => {
    const client = mqtt.connect(MQTT_URL);
    clientRef.current = client;

    client.on('connect', () => client.subscribe(topic));
    client.on('message', (_topic, payload) => {
      try {
        setMessage(JSON.parse(payload.toString()) as T);
      } catch {
        // ignore malformed messages
      }
    });

    return () => { client.end(); };
  }, [topic]);

  return message;
}
```

- [ ] **Step 7: Create ProtectedRoute.tsx and AdminRoute.tsx**

Create `DCF.Web/src/components/ProtectedRoute.tsx`:
```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { Navigate } from 'react-router-dom';

export function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth0();
  if (isLoading) return <div>Loading...</div>;
  if (!isAuthenticated) return <Navigate to="/" replace />;
  return <>{children}</>;
}
```

Create `DCF.Web/src/components/AdminRoute.tsx`:
```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { Navigate } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { UserProfile } from '../types/api';

export function AdminRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth0();
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    if (!isAuthenticated) { setChecking(false); return; }
    api.upsertUser().then(p => { setProfile(p); setChecking(false); });
  }, [isAuthenticated]);

  if (isLoading || checking) return <div>Loading...</div>;
  if (!isAuthenticated || !profile?.isAdmin) return <Navigate to="/" replace />;
  return <>{children}</>;
}
```

- [ ] **Step 8: Create src/main.tsx**

Replace the Vite-generated `DCF.Web/src/main.tsx`:
```tsx
import { Auth0Provider } from '@auth0/auth0-react';
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import { setTokenGetter } from './api/client';

// Token getter is set inside App after Auth0Provider mounts
export { setTokenGetter };

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Auth0Provider
      domain={import.meta.env.VITE_AUTH0_DOMAIN}
      clientId={import.meta.env.VITE_AUTH0_CLIENT_ID}
      authorizationParams={{
        redirect_uri: window.location.origin,
        audience: import.meta.env.VITE_AUTH0_AUDIENCE,
      }}
    >
      <App />
    </Auth0Provider>
  </React.StrictMode>
);
```

- [ ] **Step 9: Create src/App.tsx**

Create `DCF.Web/src/App.tsx`:
```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useEffect } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { setTokenGetter } from './api/client';
import { AdminRoute } from './components/AdminRoute';
import { ProtectedRoute } from './components/ProtectedRoute';
import { Admin } from './pages/Admin';
import { DraftRoom } from './pages/DraftRoom';
import { Home } from './pages/Home';
import { LeagueCreate } from './pages/LeagueCreate';
import { LeagueDetail } from './pages/LeagueDetail';
import { Leagues } from './pages/Leagues';
import { Profile } from './pages/Profile';

export default function App() {
  const { getAccessTokenSilently } = useAuth0();

  useEffect(() => {
    setTokenGetter(() =>
      getAccessTokenSilently({
        authorizationParams: { audience: import.meta.env.VITE_AUTH0_AUDIENCE },
      })
    );
  }, [getAccessTokenSilently]);

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/leagues" element={<ProtectedRoute><Leagues /></ProtectedRoute>} />
        <Route path="/leagues/create" element={<ProtectedRoute><LeagueCreate /></ProtectedRoute>} />
        <Route path="/leagues/:id" element={<ProtectedRoute><LeagueDetail /></ProtectedRoute>} />
        <Route path="/leagues/:id/draft" element={<ProtectedRoute><DraftRoom /></ProtectedRoute>} />
        <Route path="/admin" element={<AdminRoute><Admin /></AdminRoute>} />
        <Route path="/profile" element={<ProtectedRoute><Profile /></ProtectedRoute>} />
      </Routes>
    </BrowserRouter>
  );
}
```

- [ ] **Step 10: Verify the React app compiles**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds (pages will be stubs at this point).

- [ ] **Step 11: Commit**

```bash
cd ..
git add DCF.Web/
git commit -m "feat: scaffold React app with Auth0, router, MQTT hook, and API client"
```

---

## Task 13: React Pages — Home, Leagues, League Detail

**Files:**
- Create: `DCF.Web/src/pages/Home.tsx`
- Create: `DCF.Web/src/pages/Leagues.tsx`
- Create: `DCF.Web/src/pages/LeagueCreate.tsx`
- Create: `DCF.Web/src/pages/LeagueDetail.tsx`

- [ ] **Step 1: Create Home.tsx**

Create `DCF.Web/src/pages/Home.tsx`:
```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { Link } from 'react-router-dom';

export function Home() {
  const { isAuthenticated, loginWithRedirect, isLoading } = useAuth0();

  if (isLoading) return <div>Loading...</div>;

  if (!isAuthenticated) {
    return (
      <div>
        <h1>DCF Fantasy</h1>
        <p>Fantasy leagues for Drum Corps International fans.</p>
        <button onClick={() => loginWithRedirect()}>Sign In</button>
      </div>
    );
  }

  return (
    <div>
      <h1>DCF Fantasy</h1>
      <nav>
        <Link to="/leagues">My Leagues</Link> |{' '}
        <Link to="/profile">Profile</Link>
      </nav>
    </div>
  );
}
```

- [ ] **Step 2: Create Leagues.tsx**

Create `DCF.Web/src/pages/Leagues.tsx`:
```tsx
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { League } from '../types/api';

export function Leagues() {
  const [leagues, setLeagues] = useState<League[]>([]);

  useEffect(() => { api.getLeagues().then(setLeagues); }, []);

  return (
    <div>
      <h2>Leagues</h2>
      <Link to="/leagues/create">+ Create League</Link>
      <ul>
        {leagues.map(l => (
          <li key={l.id}>
            <Link to={`/leagues/${l.id}`}>{l.name}</Link>
            {' '}— {l.seasonYear} — {l.draftStatus}
            {l.isMember && ' ✓'}
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 3: Create LeagueCreate.tsx**

Create `DCF.Web/src/pages/LeagueCreate.tsx`:
```tsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';

const ALL_CAPTIONS = [
  'GeneralEffect', 'Visual', 'ColorGuard', 'Brass', 'Percussion', 'Music'
];

export function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(true);
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [captions, setCaptions] = useState<string[]>(['GeneralEffect', 'Visual', 'ColorGuard', 'Brass', 'Percussion', 'Music']);
  const [draftStartTime, setDraftStartTime] = useState('');

  const toggle = (caption: string) =>
    setCaptions(prev =>
      prev.includes(caption) ? prev.filter(c => c !== caption) : [...prev, caption]
    );

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const league = await api.createLeague({
      name, isPublic, corpsPerCaption,
      draftableCaptions: captions,
      draftStartTime: draftStartTime || null,
    }) as { id: string };
    navigate(`/leagues/${league.id}`);
  };

  return (
    <form onSubmit={submit}>
      <h2>Create League</h2>
      <label>Name: <input value={name} onChange={e => setName(e.target.value)} required /></label>
      <label>Public: <input type="checkbox" checked={isPublic} onChange={e => setIsPublic(e.target.checked)} /></label>
      <label>Corps per caption: <input type="number" value={corpsPerCaption} min={1} max={10} onChange={e => setCorpsPerCaption(Number(e.target.value))} /></label>
      <fieldset>
        <legend>Draftable Captions</legend>
        {ALL_CAPTIONS.map(c => (
          <label key={c}>
            <input type="checkbox" checked={captions.includes(c)} onChange={() => toggle(c)} /> {c}
          </label>
        ))}
      </fieldset>
      <label>Draft Start Time (optional): <input type="datetime-local" value={draftStartTime} onChange={e => setDraftStartTime(e.target.value)} /></label>
      <button type="submit">Create</button>
    </form>
  );
}
```

- [ ] **Step 4: Create LeagueDetail.tsx**

Create `DCF.Web/src/pages/LeagueDetail.tsx`:
```tsx
import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth0 } from '@auth0/auth0-react';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import type { League, Standing, DraftState } from '../types/api';

export function LeagueDetail() {
  const { id } = useParams<{ id: string }>();
  const { user } = useAuth0();
  const [league, setLeague] = useState<League | null>(null);
  const [standings, setStandings] = useState<Standing[]>([]);
  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
  const scoresUpdated = useMqtt<{ showId: string }>('dcf/scores/updated');

  useEffect(() => {
    if (id) api.getLeague(id).then(setLeague);
  }, [id]);

  useEffect(() => {
    if (id) api.getStandings(id).then(setStandings);
  }, [id, scoresUpdated]);

  if (!league) return <div>Loading...</div>;

  const joinLeague = async () => {
    const code = league.isPublic ? undefined : prompt('Enter invite code:') ?? undefined;
    await api.joinLeague(league.id, code);
    window.location.reload();
  };

  return (
    <div>
      <h2>{league.name}</h2>
      <p>Season: {league.seasonYear} | Status: {league.draftStatus}</p>
      {league.inviteCode && <p>Invite code: <code>{league.inviteCode}</code></p>}

      <Link to={`/leagues/${id}/draft`}>Draft Room</Link>

      <h3>Standings</h3>
      <ol>
        {standings.map(s => (
          <li key={s.userId}>{s.displayName} — {s.score.toFixed(3)}</li>
        ))}
      </ol>

      <h3>Members ({league.members?.length ?? 0})</h3>
      <ul>
        {league.members?.map(m => <li key={m.userId}>{m.displayName}</li>)}
      </ul>
    </div>
  );
}
```

- [ ] **Step 5: Build**

```
cd DCF.Web && npm run build
```

Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
cd ..
git add DCF.Web/src/pages/
git commit -m "feat: add Home, Leagues, LeagueCreate, and LeagueDetail pages"
```

---

## Task 14: Draft Room Page

**Files:**
- Create: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Create DraftRoom.tsx**

Create `DCF.Web/src/pages/DraftRoom.tsx`:
```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import type { Corps, DraftState, League } from '../types/api';

export function DraftRoom() {
  const { id } = useParams<{ id: string }>();
  const { user } = useAuth0();
  const [league, setLeague] = useState<League | null>(null);
  const [corps, setCorps] = useState<Corps[]>([]);
  const [selectedCorps, setSelectedCorps] = useState('');
  const [selectedCaption, setSelectedCaption] = useState('');
  const [myProfile, setMyProfile] = useState<{ id: string } | null>(null);

  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);

  useEffect(() => {
    if (!id) return;
    api.getLeague(id).then(setLeague);
    api.adminGetCorps().then(setCorps);
    api.upsertUser().then(p => setMyProfile(p));
  }, [id]);

  if (!league) return <div>Loading...</div>;

  const isMyTurn = draftState?.status === 'InProgress' &&
    draftState.currentDrafterId === myProfile?.id;

  const isCommissioner = myProfile?.id !== undefined &&
    myProfile.id === league.commissionerUserId;

  const takenCombos = new Set(
    (draftState?.picks ?? []).map(p => `${p.corpsId}|${p.caption}`)
  );

  const availableCorps = corps.filter(c =>
    !league.draftableCaptions.every(cap => takenCombos.has(`${c.id}|${cap}`))
  );

  const submitPick = async () => {
    if (!id || !selectedCorps || !selectedCaption) return;
    await api.submitPick(id, selectedCorps, selectedCaption);
    setSelectedCorps('');
    setSelectedCaption('');
  };

  const skipPick = () => id && api.skipPick(id);
  const startDraft = () => id && api.startDraft(id);

  // Lobby view
  if (!draftState || draftState.status === 'NotStarted' || draftState.status === 'Scheduled') {
    return (
      <div>
        <h2>{league.name} — Draft Lobby</h2>
        {league.draftStartTime && (
          <p>Draft starts: {new Date(league.draftStartTime).toLocaleString()}</p>
        )}
        <h3>Members joined:</h3>
        <ul>
          {(draftState?.members ?? league.members ?? []).map(m => (
            <li key={m.userId}>{m.displayName}</li>
          ))}
        </ul>
        {isCommissioner && league.draftStatus === 'NotStarted' && (
          <button onClick={startDraft}>Start Draft Now</button>
        )}
      </div>
    );
  }

  // Completed view
  if (draftState.status === 'Completed') {
    return (
      <div>
        <h2>Draft Complete</h2>
        <ol>
          {draftState.picks.map(p => (
            <li key={p.pickNumber}>
              Pick {p.pickNumber + 1}: {p.displayName} → {p.corpsName} ({p.caption})
            </li>
          ))}
        </ol>
      </div>
    );
  }

  // In-progress draft view
  const currentDrafter = draftState.members.find(
    m => m.userId === draftState.currentDrafterId
  );

  return (
    <div>
      <h2>{league.name} — Live Draft</h2>
      <p>Now picking: <strong>{currentDrafter?.displayName ?? '...'}</strong></p>

      {isMyTurn && (
        <div>
          <h3>Your pick</h3>
          <select value={selectedCorps} onChange={e => setSelectedCorps(e.target.value)}>
            <option value="">Select corps...</option>
            {availableCorps.map(c => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
          <select value={selectedCaption} onChange={e => setSelectedCaption(e.target.value)}>
            <option value="">Select caption...</option>
            {league.draftableCaptions
              .filter(cap => !takenCombos.has(`${selectedCorps}|${cap}`))
              .map(cap => <option key={cap} value={cap}>{cap}</option>)
            }
          </select>
          <button onClick={submitPick} disabled={!selectedCorps || !selectedCaption}>
            Submit Pick
          </button>
        </div>
      )}

      {isCommissioner && !isMyTurn && (
        <button onClick={skipPick}>Skip Current Pick</button>
      )}

      <h3>Pick History</h3>
      <ol>
        {draftState.picks.map(p => (
          <li key={p.pickNumber}>
            {p.displayName} → {p.corpsName} ({p.caption})
          </li>
        ))}
      </ol>
    </div>
  );
}
```

- [ ] **Step 2: Build**

```
cd DCF.Web && npm run build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
cd ..
git add DCF.Web/src/pages/DraftRoom.tsx
git commit -m "feat: add DraftRoom page with lobby, live draft, and MQTT integration"
```

---

## Task 15: Admin Panel + Profile Pages

**Files:**
- Create: `DCF.Web/src/pages/Admin.tsx`
- Create: `DCF.Web/src/pages/Profile.tsx`

- [ ] **Step 1: Create Admin.tsx**

Create `DCF.Web/src/pages/Admin.tsx`:
```tsx
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { Corps } from '../types/api';

export function Admin() {
  const [corps, setCorps] = useState<Corps[]>([]);
  const [newCorpsName, setNewCorpsName] = useState('');
  const [scrapeShowId, setScrapeShowId] = useState('');
  const [message, setMessage] = useState('');

  useEffect(() => { api.adminGetCorps().then(setCorps); }, []);

  const addCorps = async (e: React.FormEvent) => {
    e.preventDefault();
    await fetch(`${import.meta.env.VITE_API_URL}/api/admin/corps`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: newCorpsName }),
    });
    api.adminGetCorps().then(setCorps);
    setNewCorpsName('');
  };

  const triggerScrape = async (e: React.FormEvent) => {
    e.preventDefault();
    await fetch(`${import.meta.env.VITE_API_URL}/api/admin/shows/${scrapeShowId}/scrape`, {
      method: 'POST',
    });
    setMessage(`Scrape triggered for show ${scrapeShowId}`);
  };

  return (
    <div>
      <h2>Admin Panel</h2>

      <section>
        <h3>Corps</h3>
        <ul>{corps.map(c => <li key={c.id}>{c.name}</li>)}</ul>
        <form onSubmit={addCorps}>
          <input value={newCorpsName} onChange={e => setNewCorpsName(e.target.value)} placeholder="Corps name" required />
          <button type="submit">Add Corps</button>
        </form>
      </section>

      <section>
        <h3>Manual Scrape</h3>
        <form onSubmit={triggerScrape}>
          <input value={scrapeShowId} onChange={e => setScrapeShowId(e.target.value)} placeholder="Show ID" required />
          <button type="submit">Trigger Scrape</button>
        </form>
        {message && <p>{message}</p>}
      </section>
    </div>
  );
}
```

- [ ] **Step 2: Create Profile.tsx**

Create `DCF.Web/src/pages/Profile.tsx`:
```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { UserProfile } from '../types/api';

export function Profile() {
  const { logout } = useAuth0();
  const [profile, setProfile] = useState<UserProfile | null>(null);

  useEffect(() => { api.upsertUser().then(setProfile); }, []);

  if (!profile) return <div>Loading...</div>;

  return (
    <div>
      <h2>Profile</h2>
      <p>Display name: {profile.displayName}</p>
      <p>Email: {profile.email}</p>
      {profile.isAdmin && <p>✓ Admin</p>}
      <button onClick={() => logout({ logoutParams: { returnTo: window.location.origin } })}>
        Sign Out
      </button>
    </div>
  );
}
```

- [ ] **Step 3: Build**

```
cd DCF.Web && npm run build
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
cd ..
git add DCF.Web/src/pages/Admin.tsx DCF.Web/src/pages/Profile.tsx
git commit -m "feat: add Admin panel and Profile pages"
```

---

## Task 16: DCF.Api Dockerfile + Final Docker Compose

**Files:**
- Create: `DCF.Api/Dockerfile`

- [ ] **Step 1: Create Dockerfile**

Create `DCF.Api/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DCF.ScoreScraper/DCF.ScoreScraper.csproj DCF.ScoreScraper/
COPY DCF.Data/DCF.Data.csproj DCF.Data/
COPY DCF.Api/DCF.Api.csproj DCF.Api/
RUN dotnet restore DCF.Api/DCF.Api.csproj
COPY DCF.ScoreScraper/ DCF.ScoreScraper/
COPY DCF.Data/ DCF.Data/
COPY DCF.Api/ DCF.Api/
WORKDIR /src/DCF.Api
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DCF.Api.dll"]
```

- [ ] **Step 2: Build the Docker image locally**

```
docker build -f DCF.Api/Dockerfile -t dcf-api .
```

Expected: Successfully tagged dcf-api:latest.

- [ ] **Step 3: Start full stack**

```
docker compose up -d
```

Expected: All three services (postgres, mosquitto, api) running.

- [ ] **Step 4: Test the API health**

```
curl http://localhost:5000/api/admin/corps
```

Expected: `[]` (empty array — no auth for this test, will return 401 — that's correct).

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Dockerfile
git commit -m "feat: add DCF.Api Dockerfile and complete Docker Compose stack"
```

---

## Final Verification Checklist

- [ ] `dotnet test DCF.Tests/DCF.Tests.csproj` — all tests pass
- [ ] `dotnet build` from repo root — all projects build
- [ ] `cd DCF.Web && npm run build` — React app builds
- [ ] `docker compose up -d` — all services start
- [ ] Auth0 tenant configured: Google, Discord, Facebook social connections + Passwordless OTP email enabled
- [ ] Auth0 API created with identifier matching `VITE_AUTH0_AUDIENCE` / `Auth0:Audience`
- [ ] `.env.local` in `DCF.Web/` filled with real Auth0 values
- [ ] `appsettings.json` in `DCF.Api/` updated with real Auth0 domain/audience (or use environment variables in docker-compose)
- [ ] Run `dotnet ef database update --project DCF.Data` against production PostgreSQL before first deploy
