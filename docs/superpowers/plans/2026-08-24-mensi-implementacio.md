# Mensi Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Self-hosted ciklus- és fogamzáskövető webapp (2 felhasználó, Cloudflare Access mögött): .NET 10 API + Bayes-predikciós motor + Nuxt 4 SPA + PostgreSQL, docker-compose deployjal.

**Architecture:** Monorepo — `Mensi.Core` (domain + predikciós motor + CF Access middleware), `Mensi.Server` (ASP.NET Core host: /api + statikus SPA), `Mensi.Tests` (xUnit), `mensi.client` (Nuxt 4 SPA, `ssr:false`). A predikció tiszta függvényekből áll (nincs I/O), a ciklus-tábla minden írás után determinisztikusan újraépül.

**Tech Stack:** .NET 10, EF Core + Npgsql 10.0.3, Serilog.AspNetCore 10.0.0, Microsoft.IdentityModel.JsonWebTokens 8.22.0, xUnit 2.9.3, Testcontainers.PostgreSql 4.8.0, Nuxt 4, Pinia, @fontsource/montserrat, Vitest, PostgreSQL 17.

**Referenciák (kötelező olvasmány a feladat előtt):**
- Spec: `docs/superpowers/specs/2026-08-24-mensi-design.md` — az API-alakok és a matek forrása
- Matek: `docs/ovulacio-terhesseg-predikcio-referencia.md`
- UI: `docs/design/mensi-care-prototipus-kicsomagolt.html` — a kicsomagolt prototípus; a markup inline style-jai és a `<script type="text/x-dc">` blokk render-logikája a vizuális igazság forrása

## Global Constraints

- Commit üzenetben/PR-ban SOHA nincs Claude/Anthropic említés, se Co-Authored-By sor (felhasználói szabály)
- .NET `TargetFramework`: `net10.0`; `RestorePackagesWithLockFile=true` minden csproj-ban, a `packages.lock.json` commitolva
- Csomag-pinek: `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`, `Microsoft.EntityFrameworkCore.Design 10.0.10`, `Microsoft.EntityFrameworkCore.Relational 10.0.10`, `Serilog.AspNetCore 10.0.0`, `Microsoft.IdentityModel.JsonWebTokens 8.22.0`, `Microsoft.AspNetCore.TestHost 10.0.10`, `Testcontainers.PostgreSql 4.8.0`, `xunit 2.9.3`, `xunit.runner.visualstudio 3.1.4`, `Microsoft.NET.Test.Sdk 17.14.1`, `coverlet.collector 6.0.4`
- JSON: camelCase kulcsok, enumok `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`-szel stringként, dátum `yyyy-MM-dd` (`DateOnly`)
- DB oszlop/tábla nevek snake_case-ben, explicit `ToTable`/`HasColumnName` mappinggel (nincs naming-convention csomag)
- A predikciós kód tiszta: se DbContext, se DateTime.Now/UtcNow, se I/O — minden bemenet paraméter
- UI szövegek magyarul, a prototípus szövegei szó szerint; szám-megjelenítés vesszős tizedessel a frontenden, a JSON-ban pont
- Frontend: nincs Google CDN — Montserrat a `@fontsource/montserrat` csomagból
- Teszt parancsok: `dotnet test` a repo gyökeréből; `npm run test` + `npm run type-check` a `mensi.client`-ből
- Integrációs tesztek valós Postgresszel (Testcontainers) futnak — Docker daemon kell hozzájuk

---

### Task 1: Solution scaffold (3 .NET projekt + smoke teszt)

**Files:**
- Create: `Mensi.slnx`, `Mensi.Core/Mensi.Core.csproj`, `Mensi.Server/Mensi.Server.csproj`, `Mensi.Server/Program.cs`, `Mensi.Server/Properties/launchSettings.json`, `Mensi.Tests/Mensi.Tests.csproj`, `Mensi.Tests/SmokeTests.cs`
- Modify: `.gitignore`

**Interfaces:**
- Produces: buildelhető solution; `GET /health` → `200 "OK"`; a Server dev URL-je `http://localhost:5080`

- [ ] **Step 1: .gitignore kiegészítése .NET bejegyzésekkel**

A meglévő Node-os `.gitignore` végére:

```gitignore

# .NET
bin/
obj/
*.user
```

- [ ] **Step 2: Projektfájlok létrehozása**

`Mensi.slnx`:

```xml
<Solution>
  <Project Path="Mensi.Core/Mensi.Core.csproj" />
  <Project Path="Mensi.Server/Mensi.Server.csproj" />
  <Project Path="Mensi.Tests/Mensi.Tests.csproj" />
</Solution>
```

`Mensi.Core/Mensi.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <!-- A Microsoft.NET.Sdk.Web implicit usingjait tükrözi, hogy a middleware és a
       startup-helperek hostprojekt nélkül is fordulnak. -->
  <ItemGroup>
    <Using Include="Microsoft.AspNetCore.Builder" />
    <Using Include="Microsoft.AspNetCore.Http" />
    <Using Include="Microsoft.Extensions.Configuration" />
    <Using Include="Microsoft.Extensions.DependencyInjection" />
    <Using Include="Microsoft.Extensions.Hosting" />
    <Using Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.10">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.22.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
  </ItemGroup>

</Project>
```

`Mensi.Server/Mensi.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Mensi.Core\Mensi.Core.csproj" />
  </ItemGroup>

</Project>
```

`Mensi.Server/Program.cs` (ideiglenes minimál — a Task 15 cseréli le a teljes wiringre):

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Text("OK"));

app.Run();

// A WebApplicationFactory-nak kell hivatkozási pont az integrációs tesztekhez.
public partial class Program;
```

`Mensi.Server/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "applicationUrl": "http://localhost:5080",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

`Mensi.Tests/Mensi.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.10" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.10" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.10" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.8.0" />
    <PackageReference Include="SSH.NET" Version="2024.2.0" NoWarn="NU1903" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Mensi.Core\Mensi.Core.csproj" />
    <ProjectReference Include="..\Mensi.Server\Mensi.Server.csproj" />
  </ItemGroup>

</Project>
```

`Mensi.Tests/SmokeTests.cs`:

```csharp
namespace Mensi.Tests;

public class SmokeTests
{
    [Fact]
    public void Solution_builds_and_tests_run() => Assert.True(true);
}
```

- [ ] **Step 3: Build + teszt futtatás**

Run: `dotnet restore /p:RestoreForceEvaluate=true && dotnet build && dotnet test`
Expected: build OK, 1 teszt PASS. A `packages.lock.json` fájlok létrejönnek.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: .NET solution scaffold (Core, Server, Tests)"
```

---

### Task 2: Cloudflare Access port a PortfolioCMS-ből

**Files:**
- Create: `Mensi.Core/Options/CloudflareAccessOptions.cs`, `Mensi.Core/Middleware/CloudflareAccessMiddleware.cs`, `Mensi.Core/Services/CloudflareAccessKeyStore.cs`, `Mensi.Core/Services/AccessIdentity.cs`
- Create: `Mensi.Tests/CloudflareAccessTestKit.cs`, `Mensi.Tests/CloudflareAccessMiddlewareTests.cs`, `Mensi.Tests/CloudflareAccessKeyStoreTests.cs`

**Interfaces:**
- Produces: `CloudflareAccessMiddleware` (constructor: `RequestDelegate, CloudflareAccessKeyStore, IOptions<CloudflareAccessOptions>, ILogger<>`), `CloudflareAccessOptions { SectionName="CloudflareAccess", TeamDomain, Audience, Issuer, CertsUrl, IsConfigured }`, `CloudflareAccessKeyStore.GetSigningKeysAsync(bool force, CancellationToken)`, `AccessIdentity.Of(HttpContext?) : string` (email vagy `"unknown"`), `AccessIdentity.EmailClaim = "email"`

- [ ] **Step 1: Forrásfájlok másolása namespace-cserével**

A PortfolioCMS repo helyben van (`/Users/xentinus/Development/PortfolioCMS`). Másolás + átnevezés:

```bash
mkdir -p Mensi.Core/Options Mensi.Core/Middleware Mensi.Core/Services
for pair in \
  "PortfolioCMS.Core/Options/CloudflareAccessOptions.cs:Mensi.Core/Options/CloudflareAccessOptions.cs" \
  "PortfolioCMS.Core/Middleware/CloudflareAccessMiddleware.cs:Mensi.Core/Middleware/CloudflareAccessMiddleware.cs" \
  "PortfolioCMS.Core/Services/CloudflareAccessKeyStore.cs:Mensi.Core/Services/CloudflareAccessKeyStore.cs" \
  "PortfolioCMS.Tests/CloudflareAccessTestKit.cs:Mensi.Tests/CloudflareAccessTestKit.cs" \
  "PortfolioCMS.Tests/CloudflareAccessMiddlewareTests.cs:Mensi.Tests/CloudflareAccessMiddlewareTests.cs" \
  "PortfolioCMS.Tests/CloudflareAccessKeyStoreTests.cs:Mensi.Tests/CloudflareAccessKeyStoreTests.cs" ; do
  src="/Users/xentinus/Development/PortfolioCMS/${pair%%:*}"; dst="${pair##*:}"
  sed -e 's/PortfolioCMS\.Core/Mensi.Core/g' -e 's/PortfolioCMS\.Tests/Mensi.Tests/g' "$src" > "$dst"
done
```

A middleware XML-kommentjében az admin-hostos mondatot cseréld Mensi-kontextusra (egyetlen host van, minden mögötte). A tesztfájlokban ha `AccessIdentity`-re hivatkozás van, az a Step 2-ben létrejön.

- [ ] **Step 2: AccessIdentity megírása**

`Mensi.Core/Services/AccessIdentity.cs`:

```csharp
using System.Security.Claims;

namespace Mensi.Core.Services;

/// <summary>
/// A kérés mögötti személy. A CloudflareAccessMiddleware validálja az assertiont és a
/// principal-ra teszi az identitást — az audit log innen olvassa vissza az emailt.
/// </summary>
public static class AccessIdentity
{
    /// <summary>Ebbe a claimbe teszi a Cloudflare Access a bejelentkezett email-címet.</summary>
    public const string EmailClaim = "email";

    /// <summary>
    /// Fejlesztésben, kikapcsolt Access mellett minden írás ezen a néven auditálódik.
    /// </summary>
    public const string DevFallback = "dev@localhost";

    public const string Unknown = "unknown";

    public static string Of(HttpContext? context)
    {
        var user = context?.User;
        if (user?.Identity?.IsAuthenticated != true) return Unknown;

        var email = user.FindFirst(EmailClaim)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? Unknown : email;
    }
}
```

- [ ] **Step 3: Tesztek futtatása**

Run: `dotnet test --filter "FullyQualifiedName~CloudflareAccess"`
Expected: minden portolt teszt PASS (elutasítás assertion nélkül, érvényes header/cookie átmegy, lejárt/rossz audience/rossz issuer/rossz kulcs elutasítva, kulcsrotáció-retry, keystore cache/throttle). Ha a portolt teszt `AccessIdentity.Of`-ot hív `/whoami`-n, az emailnek a token `email` claimjéből kell jönnie.

- [ ] **Step 4: Commit**

```bash
git add Mensi.Core Mensi.Tests
git commit -m "feat: Cloudflare Access JWT validáció (middleware + keystore + tesztek)"
```

---

### Task 3: Domain entitások + DbContext + InitialCreate migráció

**Files:**
- Create: `Mensi.Core/Domain/Enums.cs`, `Mensi.Core/Domain/DailyLog.cs`, `Mensi.Core/Domain/IntercourseEvent.cs`, `Mensi.Core/Domain/Cycle.cs`, `Mensi.Core/Domain/AuditEntry.cs`, `Mensi.Core/Data/MensiDbContext.cs`, `Mensi.Core/Data/Migrations/` (generált)
- Test: `Mensi.Tests/DbContextTests.cs`, `Mensi.Tests/PostgresFixture.cs`

**Interfaces:**
- Produces: `MensiDbContext { DbSet<DailyLog> DailyLogs, DbSet<IntercourseEvent> IntercourseEvents, DbSet<Cycle> Cycles, DbSet<AuditEntry> AuditEntries }`; entitás-property-k lentebb szó szerint; `PostgresFixture` (xUnit collection fixture, Testcontainers Postgres + migrált DbContext factory)

- [ ] **Step 1: Enumok és entitások**

`Mensi.Core/Domain/Enums.cs`:

```csharp
namespace Mensi.Core.Domain;

public enum CervicalMucus : short { Dry = 0, Sticky = 1, Creamy = 2, EggWhite = 3 }
public enum LhTest : short { Negative = 0, Positive = 1, Peak = 2 }
public enum CrampType : short { Abdomen = 0, Back = 1, Breast = 2 }
public enum FlowIntensity : short { None = 0, Spotting = 1, Light = 2, Medium = 3, Heavy = 4 }
public enum Mood : short { Cheerful = 0, Calm = 1, Irritable = 2, Tired = 3, Sad = 4, Anxious = 5, Longing = 6 }
public enum TimingLabel { Weak, Medium, Good }
public enum ConfidenceLevel { Low, Medium, High }
public enum DayCategory { PreCycle, Menstruation, Follicular, Fertile, Ovulation, Luteal, PredictedPeriod, Unknown }
```

`Mensi.Core/Domain/DailyLog.cs`:

```csharp
namespace Mensi.Core.Domain;

public class DailyLog
{
    public DateOnly Date { get; set; }
    public decimal? BbtCelsius { get; set; }
    public CervicalMucus? CervicalMucus { get; set; }
    public LhTest? LhTest { get; set; }
    public CrampType? CrampType { get; set; }
    public short? CrampSeverity { get; set; }
    public FlowIntensity? FlowIntensity { get; set; }
    public bool PeriodStart { get; set; }
    public List<Mood> Moods { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
    public List<IntercourseEvent> Intercourse { get; set; } = [];
}
```

`Mensi.Core/Domain/IntercourseEvent.cs`:

```csharp
namespace Mensi.Core.Domain;

public class IntercourseEvent
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public bool? Protected { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

`Mensi.Core/Domain/Cycle.cs`:

```csharp
namespace Mensi.Core.Domain;

public class Cycle
{
    public long Id { get; set; }
    public DateOnly StartDate { get; set; }
    public int? LengthDays { get; set; }
    public int? OvulationDayEstimated { get; set; }
    public int? OvulationDayConfirmed { get; set; }
    public int? LutealPhaseLength { get; set; }
    public bool Anovulatory { get; set; }
    /// <summary>A modell predikciója a ciklus indulásakor — egyszer íródik, nem számolódik újra.</summary>
    public int? PredictedLengthDays { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}
```

`Mensi.Core/Domain/AuditEntry.cs`:

```csharp
namespace Mensi.Core.Domain;

public class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Email { get; set; } = "";
    public string Action { get; set; } = "";
    public DateOnly EntryDate { get; set; }
    /// <summary>jsonb: mező → { "old": …, "new": … }</summary>
    public string ChangesJson { get; set; } = "{}";
}
```

- [ ] **Step 2: DbContext explicit snake_case mappinggel**

`Mensi.Core/Data/MensiDbContext.cs`:

```csharp
using Mensi.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Mensi.Core.Data;

public class MensiDbContext(DbContextOptions<MensiDbContext> options) : DbContext(options)
{
    public DbSet<DailyLog> DailyLogs => Set<DailyLog>();
    public DbSet<IntercourseEvent> IntercourseEvents => Set<IntercourseEvent>();
    public DbSet<Cycle> Cycles => Set<Cycle>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var moodsComparer = new ValueComparer<List<Mood>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (h, m) => HashCode.Combine(h, m)),
            v => v.ToList());

        modelBuilder.Entity<DailyLog>(e =>
        {
            e.ToTable("daily_log");
            e.HasKey(x => x.Date);
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.BbtCelsius).HasColumnName("bbt_celsius").HasColumnType("numeric(4,2)");
            e.Property(x => x.CervicalMucus).HasColumnName("cervical_mucus").HasConversion<short?>();
            e.Property(x => x.LhTest).HasColumnName("lh_test").HasConversion<short?>();
            e.Property(x => x.CrampType).HasColumnName("cramp_type").HasConversion<short?>();
            e.Property(x => x.CrampSeverity).HasColumnName("cramp_severity");
            e.Property(x => x.FlowIntensity).HasColumnName("flow_intensity").HasConversion<short?>();
            e.Property(x => x.PeriodStart).HasColumnName("period_start");
            e.Property(x => x.Moods).HasColumnName("moods")
                .HasConversion(
                    v => v.Select(m => (short)m).ToArray(),
                    v => v.Select(m => (Mood)m).ToList())
                .HasColumnType("smallint[]")
                .Metadata.SetValueComparer(moodsComparer);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.HasMany(x => x.Intercourse).WithOne().HasForeignKey(x => x.Date)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntercourseEvent>(e =>
        {
            e.ToTable("intercourse_event");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Protected).HasColumnName("protected");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.Date);
        });

        modelBuilder.Entity<Cycle>(e =>
        {
            e.ToTable("cycle");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StartDate).HasColumnName("start_date");
            e.HasIndex(x => x.StartDate).IsUnique();
            e.Property(x => x.LengthDays).HasColumnName("length_days");
            e.Property(x => x.OvulationDayEstimated).HasColumnName("ovulation_day_estimated");
            e.Property(x => x.OvulationDayConfirmed).HasColumnName("ovulation_day_confirmed");
            e.Property(x => x.LutealPhaseLength).HasColumnName("luteal_phase_length");
            e.Property(x => x.Anovulatory).HasColumnName("anovulatory");
            e.Property(x => x.PredictedLengthDays).HasColumnName("predicted_length_days");
            e.Property(x => x.ComputedAt).HasColumnName("computed_at");
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.At).HasColumnName("at");
            e.HasIndex(x => x.At);
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.EntryDate).HasColumnName("entry_date");
            e.Property(x => x.ChangesJson).HasColumnName("changes").HasColumnType("jsonb");
        });
    }
}
```

- [ ] **Step 3: Server bekötés a migrációhoz (design-time)**

`Mensi.Server/Program.cs` — a builder után, a `Build()` előtt szúrd be:

```csharp
builder.Services.AddDbContext<Mensi.Core.Data.MensiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=mensi;Username=mensi;Password=mensi"));
```

(A `using Microsoft.EntityFrameworkCore;` kell hozzá.)

- [ ] **Step 4: Migráció generálása**

Run:
```bash
dotnet tool install --global dotnet-ef 2>/dev/null || dotnet tool update --global dotnet-ef
dotnet ef migrations add InitialCreate --project Mensi.Core --startup-project Mensi.Server --output-dir Data/Migrations
dotnet build
```
Expected: `Mensi.Core/Data/Migrations/*_InitialCreate.cs` létrejön, build OK. Nézd át: a táblanevek `daily_log`, `intercourse_event`, `cycle`, `audit_log`; a `moods` oszlop `smallint[]`; a `changes` jsonb.

- [ ] **Step 5: Failing teszt — roundtrip Testcontainers Postgresszel**

`Mensi.Tests/PostgresFixture.cs`:

```csharp
using Mensi.Core.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Mensi.Tests;

/// <summary>
/// Egy megosztott, eldobható Postgres a teljes tesztfuttatásra; a migrációk egyszer futnak le.
/// A tesztek külön-külön takarítanak (TRUNCATE), így egymástól függetlenek maradnak.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public MensiDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MensiDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE daily_log, intercourse_event, cycle, audit_log RESTART IDENTITY CASCADE");
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

`Mensi.Tests/DbContextTests.cs`:

```csharp
using Mensi.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Tests;

[Collection("postgres")]
public class DbContextTests(PostgresFixture fixture)
{
    [Fact]
    public async Task DailyLog_roundtrips_with_moods_and_intercourse()
    {
        await fixture.ResetAsync();
        var date = new DateOnly(2026, 8, 23);
        await using (var db = fixture.CreateContext())
        {
            db.DailyLogs.Add(new DailyLog
            {
                Date = date,
                BbtCelsius = 36.36m,
                CervicalMucus = CervicalMucus.EggWhite,
                LhTest = LhTest.Positive,
                CrampType = CrampType.Abdomen,
                CrampSeverity = 2,
                FlowIntensity = FlowIntensity.None,
                PeriodStart = false,
                Moods = [Mood.Cheerful, Mood.Longing],
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = "a@b.hu",
                Intercourse = [new IntercourseEvent { Date = date, Protected = false, CreatedAt = DateTimeOffset.UtcNow }]
            });
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var log = await read.DailyLogs.Include(l => l.Intercourse).SingleAsync(l => l.Date == date);
        Assert.Equal(36.36m, log.BbtCelsius);
        Assert.Equal([Mood.Cheerful, Mood.Longing], log.Moods);
        Assert.Single(log.Intercourse);
        Assert.False(log.Intercourse[0].Protected);
    }
}
```

- [ ] **Step 6: Teszt futtatás**

Run: `dotnet test --filter "FullyQualifiedName~DbContextTests"`
Expected: PASS (Docker kell hozzá).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: domain entitások, MensiDbContext, InitialCreate migráció"
```

---

### Task 4: Patch<T> — részleges JSON upsert primitív

**Files:**
- Create: `Mensi.Core/Api/Patch.cs`
- Test: `Mensi.Tests/PatchTests.cs`

**Interfaces:**
- Produces: `Patch<T> { bool IsSet, T? Value }` + `PatchJsonConverterFactory` — JSON-ban jelen lévő kulcs (akár null) → `IsSet=true`; hiányzó kulcs → default `Patch<T>` (`IsSet=false`). A Task 15 request DTO-i erre épülnek.

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/PatchTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Mensi.Core.Api;

namespace Mensi.Tests;

public class PatchTests
{
    private sealed record Body
    {
        public Patch<decimal?> Bbt { get; init; } = new();
        public Patch<string?> Note { get; init; } = new();
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new PatchJsonConverterFactory(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void Missing_key_is_not_set()
    {
        var body = JsonSerializer.Deserialize<Body>("""{}""", Options)!;
        Assert.False(body.Bbt.IsSet);
    }

    [Fact]
    public void Null_value_is_set_with_null()
    {
        var body = JsonSerializer.Deserialize<Body>("""{"bbt":null}""", Options)!;
        Assert.True(body.Bbt.IsSet);
        Assert.Null(body.Bbt.Value);
    }

    [Fact]
    public void Value_is_set_with_value()
    {
        var body = JsonSerializer.Deserialize<Body>("""{"bbt":36.42}""", Options)!;
        Assert.True(body.Bbt.IsSet);
        Assert.Equal(36.42m, body.Bbt.Value);
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~PatchTests"`
Expected: FAIL (a `Patch<T>` még nem létezik → fordítási hiba).

- [ ] **Step 3: Implementáció**

`Mensi.Core/Api/Patch.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mensi.Core.Api;

/// <summary>
/// Részleges upsert mező: megkülönbözteti a "nem küldött" és a "null-ra állított" esetet.
/// JSON-ban jelen lévő kulcs → IsSet=true (Value lehet null is); hiányzó kulcs → IsSet=false.
/// </summary>
public sealed record Patch<T>
{
    public bool IsSet { get; init; }
    public T? Value { get; init; }

    public static Patch<T> Of(T? value) => new() { IsSet = true, Value = value };
}

public sealed class PatchJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Patch<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(PatchConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class PatchConverter<T> : JsonConverter<Patch<T>>
    {
        public override Patch<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Patch<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Patch<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~PatchTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Api/Patch.cs Mensi.Tests/PatchTests.cs
git commit -m "feat: Patch<T> részleges upsert JSON konverterrel"
```

---

### Task 5: CycleStats — ciklusstatisztika (EWMA, szórás, delay-percentilisek)

**Files:**
- Create: `Mensi.Core/Prediction/CycleStats.cs`
- Test: `Mensi.Tests/Prediction/CycleStatsTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace Mensi.Core.Prediction;

public sealed record ClosedCycleStat(
    DateOnly StartDate, int LengthDays, int? LutealLength, bool Anovulatory, int? PredictedLengthDays);

public sealed record CycleStatsResult(
    int ClosedCount, double EwmaLength, double MeanLength, double StdDevLength,
    double MedianLength, int MinLength, int MaxLength,
    double? MeanLuteal, double? StdDevLuteal, int ConfirmedLutealCount,
    (int P10, int P50, int P90)? Delay);

public static class CycleStats
{
    public const double Alpha = 0.27;
    public static CycleStatsResult? Compute(IReadOnlyList<ClosedCycleStat> cycles); // null, ha üres
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/CycleStatsTests.cs`:

```csharp
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class CycleStatsTests
{
    private static ClosedCycleStat C(int startOffset, int len, int? luteal = null,
        bool anov = false, int? predicted = null) =>
        new(new DateOnly(2026, 1, 1).AddDays(startOffset), len, luteal, anov, predicted);

    [Fact]
    public void Empty_returns_null() => Assert.Null(CycleStats.Compute([]));

    [Fact]
    public void Single_cycle_seeds_everything()
    {
        var s = CycleStats.Compute([C(0, 28)])!;
        Assert.Equal(1, s.ClosedCount);
        Assert.Equal(28, s.EwmaLength);
        Assert.Equal(28, s.MeanLength);
        Assert.Equal(0, s.StdDevLength); // n<2: definíció szerint 0
        Assert.Equal(28, s.MedianLength);
    }

    [Fact]
    public void Ewma_weights_the_latest_cycle_by_alpha()
    {
        // 0.27·30 + 0.73·28 = 28.54
        var s = CycleStats.Compute([C(0, 28), C(28, 30)])!;
        Assert.Equal(28.54, s.EwmaLength, 3);
        Assert.Equal(29, s.MeanLength);
        Assert.Equal(Math.Sqrt(2), s.StdDevLength, 6);
    }

    [Fact]
    public void Anovulatory_cycle_updates_ewma_with_half_weight()
    {
        // effektív alfa 0.135: 28 + 0.135·(30−28) = 28.27
        var s = CycleStats.Compute([C(0, 28), C(28, 30, anov: true)])!;
        Assert.Equal(28.27, s.EwmaLength, 3);
    }

    [Fact]
    public void Luteal_stats_use_only_confirmed_cycles()
    {
        var s = CycleStats.Compute([C(0, 28, 13), C(28, 30, 14), C(58, 27)])!;
        Assert.Equal(13.5, s.MeanLuteal!.Value, 3);
        Assert.Equal(2, s.ConfirmedLutealCount);
    }

    [Fact]
    public void Delay_percentiles_use_nearest_rank()
    {
        // delayek: 28−27=1, 28−28=0, 27−30=−3 → rendezve [−3, 0, 1]
        var s = CycleStats.Compute([C(0, 28, predicted: 27), C(28, 28, predicted: 28), C(56, 27, predicted: 30)])!;
        Assert.Equal((-3, 0, 1), s.Delay);
    }

    [Fact]
    public void Delay_is_null_without_predictions()
    {
        Assert.Null(CycleStats.Compute([C(0, 28)])!.Delay);
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~CycleStatsTests"`
Expected: FAIL (fordítási hiba, a típusok nem léteznek).

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/CycleStats.cs`:

```csharp
namespace Mensi.Core.Prediction;

public sealed record ClosedCycleStat(
    DateOnly StartDate, int LengthDays, int? LutealLength, bool Anovulatory, int? PredictedLengthDays);

public sealed record CycleStatsResult(
    int ClosedCount, double EwmaLength, double MeanLength, double StdDevLength,
    double MedianLength, int MinLength, int MaxLength,
    double? MeanLuteal, double? StdDevLuteal, int ConfirmedLutealCount,
    (int P10, int P50, int P90)? Delay);

public static class CycleStats
{
    public const double Alpha = 0.27;

    public static CycleStatsResult? Compute(IReadOnlyList<ClosedCycleStat> cycles)
    {
        if (cycles.Count == 0) return null;
        var ordered = cycles.OrderBy(c => c.StartDate).ToList();
        var lengths = ordered.Select(c => (double)c.LengthDays).ToList();

        // EWMA: az anovulatorikus ciklus fél súllyal frissít (kilógó, de nem eldobható adat).
        var ewma = lengths[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            var a = Alpha * (ordered[i].Anovulatory ? 0.5 : 1.0);
            ewma = a * lengths[i] + (1 - a) * ewma;
        }

        var mean = lengths.Average();
        var std = lengths.Count < 2
            ? 0
            : Math.Sqrt(lengths.Sum(l => (l - mean) * (l - mean)) / (lengths.Count - 1));

        var sortedLen = lengths.OrderBy(l => l).ToList();
        var median = sortedLen.Count % 2 == 1
            ? sortedLen[sortedLen.Count / 2]
            : (sortedLen[sortedLen.Count / 2 - 1] + sortedLen[sortedLen.Count / 2]) / 2;

        var luteals = ordered.Where(c => c.LutealLength is not null)
            .Select(c => (double)c.LutealLength!.Value).ToList();
        double? meanLut = luteals.Count > 0 ? luteals.Average() : null;
        double? stdLut = luteals.Count switch
        {
            0 => null,
            1 => 0,
            _ => Math.Sqrt(luteals.Sum(l => (l - meanLut!.Value) * (l - meanLut.Value)) / (luteals.Count - 1)),
        };

        var delays = ordered.Where(c => c.PredictedLengthDays is not null)
            .Select(c => c.LengthDays - c.PredictedLengthDays!.Value)
            .OrderBy(d => d).ToList();
        (int, int, int)? delay = delays.Count > 0
            ? (NearestRank(delays, 0.10), NearestRank(delays, 0.50), NearestRank(delays, 0.90))
            : null;

        return new CycleStatsResult(
            ordered.Count, ewma, mean, std, median,
            (int)sortedLen[0], (int)sortedLen[^1],
            meanLut, stdLut, luteals.Count, delay);
    }

    private static int NearestRank(IReadOnlyList<int> sorted, double p)
    {
        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~CycleStatsTests"`
Expected: 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction/CycleStats.cs Mensi.Tests/Prediction/CycleStatsTests.cs
git commit -m "feat: ciklusstatisztika (EWMA, szórás, medián, delay-percentilisek)"
```

---

### Task 6: Shrinkage — empirical Bayes normál-normál frissítés

**Files:**
- Create: `Mensi.Core/Prediction/Shrinkage.cs`
- Test: `Mensi.Tests/Prediction/ShrinkageTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class Shrinkage
{
    // Visszaadott Variance = posterior variancia + s²_within (prediktív, spec 4.2)
    public static (double Mean, double Variance) Apply(
        double popMean, double popVar, int n, double sampleMean, double sampleVar);
}
public static class PopulationPriors
{
    public const double CycleMean = 28, CycleVar = 16;   // Normal(28, 4²)
    public const double LutealMean = 14, LutealVar = 4;  // Normal(14, 2²)
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/ShrinkageTests.cs`:

```csharp
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class ShrinkageTests
{
    [Fact]
    public void Zero_samples_returns_population_prior()
    {
        var (mean, var) = Shrinkage.Apply(28, 16, 0, 0, 0);
        Assert.Equal(28, mean);
        Assert.Equal(16, var);
    }

    [Fact]
    public void Many_samples_converge_to_sample_mean()
    {
        var (mean, _) = Shrinkage.Apply(28, 16, 100, 26, 1);
        Assert.True(Math.Abs(mean - 26) < 0.1);
    }

    [Fact]
    public void Few_samples_land_between_population_and_sample()
    {
        var (mean, _) = Shrinkage.Apply(28, 16, 3, 26, 4);
        Assert.InRange(mean, 26, 28);
    }

    [Fact]
    public void Small_n_uses_population_variance_as_within()
    {
        // n<2-nél a mintavariancia nem értelmezhető: a populációsat használjuk s²-ként.
        var one = Shrinkage.Apply(28, 16, 1, 26, 0);
        var oneExplicit = Shrinkage.Apply(28, 16, 1, 26, 16);
        Assert.Equal(oneExplicit, one);
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~ShrinkageTests"`
Expected: FAIL (típus nincs).

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/Shrinkage.cs`:

```csharp
namespace Mensi.Core.Prediction;

public static class PopulationPriors
{
    public const double CycleMean = 28, CycleVar = 16;   // Normal(28, 4²)
    public const double LutealMean = 14, LutealVar = 4;  // Normal(14, 2²)
}

/// <summary>Normál-normál konjugált frissítés (spec 4.2): kevés saját ciklusnál a populációs
/// prior dominál, sok adatnál a személyes átlag. A Variance prediktív: posterior + s²_within.</summary>
public static class Shrinkage
{
    public static (double Mean, double Variance) Apply(
        double popMean, double popVar, int n, double sampleMean, double sampleVar)
    {
        if (n <= 0) return (popMean, popVar);
        var s2 = n < 2 || sampleVar <= 0 ? popVar : sampleVar;
        var precision = n / s2 + 1 / popVar;
        var mean = (n * sampleMean / s2 + popMean / popVar) / precision;
        return (mean, 1 / precision + s2);
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~ShrinkageTests"`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction/Shrinkage.cs Mensi.Tests/Prediction/ShrinkageTests.cs
git commit -m "feat: empirical Bayes shrinkage a személyes paraméterekhez"
```

---

### Task 7: BbtAnalyzer — outlier-kizárás, coverline, ovuláció-megerősítés

**Files:**
- Create: `Mensi.Core/Prediction/BbtAnalyzer.cs`
- Test: `Mensi.Tests/Prediction/BbtAnalyzerTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record BbtDay(int CycleDay, decimal? Value); // CycleDay 1-től, hézagmentes lista
public sealed record BbtAnalysis(
    decimal? Coverline,            // megerősítéskor a shift coverline-ja, egyébként provizórikus
    int? ConfirmedOvulationDay,    // a shift előtti utolsó "alacsony" nap ciklusnapja
    IReadOnlySet<int> OutlierDays,
    IReadOnlySet<int> MissingDays,
    IReadOnlySet<int> AboveCoverlineDays,
    int ValidCount);
public static class BbtAnalyzer
{
    public const decimal ShiftThreshold = 0.2m;
    public const decimal OutlierDelta = 0.3m;
    public static BbtAnalysis Analyze(IReadOnlyList<BbtDay> days);
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/BbtAnalyzerTests.cs`:

```csharp
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class BbtAnalyzerTests
{
    private static IReadOnlyList<BbtDay> Series(params decimal?[] values) =>
        values.Select((v, i) => new BbtDay(i + 1, v)).ToList();

    [Fact]
    public void Three_consecutive_highs_confirm_ovulation()
    {
        // 1..9 alacsony (utolsó 6 maximuma 36.42), 10..12 magas: mind > 36.62
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, 36.66m, 36.70m));
        Assert.Equal(36.42m, a.Coverline);
        Assert.Equal(9, a.ConfirmedOvulationDay);
        Assert.Equal([10, 11, 12], a.AboveCoverlineDays.Order());
    }

    [Fact]
    public void Two_highs_do_not_confirm_and_coverline_is_provisional()
    {
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, 36.66m));
        Assert.Null(a.ConfirmedOvulationDay);
        Assert.NotNull(a.Coverline); // provizórikus: az utolsó 6 érvényes érték maximuma
    }

    [Fact]
    public void Lone_spike_is_excluded_as_outlier()
    {
        // az 5. nap 36.85 kiugrás a 36.3x-os környezetben, a szomszédok nem trendtársak
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.85m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, 36.66m, 36.70m));
        Assert.Contains(5, a.OutlierDays);
        Assert.Equal(36.42m, a.Coverline);   // a kiugrás nem emeli meg a coverline-t
        Assert.Equal(9, a.ConfirmedOvulationDay);
    }

    [Fact]
    public void Rising_trend_is_not_an_outlier()
    {
        // a 10. nap magas, de a 11–12. is: trend része, nem magányos kiugrás
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.70m, 36.68m, 36.72m));
        Assert.DoesNotContain(10, a.OutlierDays);
    }

    [Fact]
    public void Missing_days_are_listed_and_do_not_break_confirmation()
    {
        // a 11. nap hiányzik; a 10., 12., 13. mérés így is 3 egymást követő ÉRVÉNYES érték
        var a = BbtAnalyzer.Analyze(Series(
            36.30m, 36.35m, 36.32m, 36.40m, 36.36m, 36.42m, 36.38m, 36.35m, 36.33m,
            36.65m, null, 36.66m, 36.70m));
        Assert.Equal([11], a.MissingDays.Order());
        Assert.Equal(9, a.ConfirmedOvulationDay);
    }

    [Fact]
    public void Too_few_values_yield_no_coverline()
    {
        var a = BbtAnalyzer.Analyze(Series(36.30m, 36.35m, null, 36.40m));
        Assert.Null(a.Coverline);
        Assert.Null(a.ConfirmedOvulationDay);
        Assert.Equal(3, a.ValidCount);
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~BbtAnalyzerTests"`
Expected: FAIL.

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/BbtAnalyzer.cs`:

```csharp
namespace Mensi.Core.Prediction;

public sealed record BbtDay(int CycleDay, decimal? Value);

public sealed record BbtAnalysis(
    decimal? Coverline,
    int? ConfirmedOvulationDay,
    IReadOnlySet<int> OutlierDays,
    IReadOnlySet<int> MissingDays,
    IReadOnlySet<int> AboveCoverlineDays,
    int ValidCount);

/// <summary>Coverline + change-point a spec 4.4 szerint. Hiányzó napot nem interpolál:
/// az "egymást követő" mindig az érvényes mérések sorrendjét jelenti.</summary>
public static class BbtAnalyzer
{
    public const decimal ShiftThreshold = 0.2m;
    public const decimal OutlierDelta = 0.3m;
    private const decimal TrendDelta = 0.15m;

    public static BbtAnalysis Analyze(IReadOnlyList<BbtDay> days)
    {
        var missing = days.Where(d => d.Value is null).Select(d => d.CycleDay).ToHashSet();
        var valid = days.Where(d => d.Value is not null)
            .OrderBy(d => d.CycleDay)
            .Select(d => (Day: d.CycleDay, Value: d.Value!.Value))
            .ToList();

        var outliers = FindOutliers(valid);
        var series = valid.Where(v => !outliers.Contains(v.Day)).ToList();

        decimal? coverline = null;
        int? confirmedDay = null;

        // Change-point: az első k pozíció, ahol az előtte lévő 6 érték maximuma fölött
        // legalább 0,2°C-kal van 3 egymást követő érvényes mérés.
        for (var k = 6; k + 2 < series.Count; k++)
        {
            var baseline = series.Skip(k - 6).Take(6).Max(v => v.Value);
            if (series[k].Value > baseline + ShiftThreshold
                && series[k + 1].Value > baseline + ShiftThreshold
                && series[k + 2].Value > baseline + ShiftThreshold)
            {
                coverline = baseline;
                confirmedDay = series[k - 1].Day;
                break;
            }
        }

        // Provizórikus coverline megerősítés előtt: az utolsó 6 érvényes, nem-kiugró érték maximuma.
        coverline ??= series.Count >= 6 ? series.TakeLast(6).Max(v => v.Value) : null;

        var above = coverline is null
            ? new HashSet<int>()
            : series.Where(v => v.Value > coverline.Value).Select(v => v.Day).ToHashSet();

        return new BbtAnalysis(coverline, confirmedDay, outliers, missing, above, valid.Count);
    }

    private static HashSet<int> FindOutliers(List<(int Day, decimal Value)> valid)
    {
        var outliers = new HashSet<int>();
        for (var i = 0; i < valid.Count; i++)
        {
            var (day, value) = valid[i];
            var neighbors = valid.Where(v => v.Day != day && Math.Abs(v.Day - day) <= 3)
                .OrderBy(v => Math.Abs(v.Day - day)).Take(5)
                .Select(v => v.Value).OrderBy(v => v).ToList();
            if (neighbors.Count < 2) continue;

            var median = neighbors[neighbors.Count / 2];
            var deviation = value - median;
            if (Math.Abs(deviation) < OutlierDelta) continue;

            // Trend része, ha a közvetlen előző vagy következő érvényes mérés is
            // ugyanabba az irányba tér el legalább 0,15°C-kal.
            var sameDirection = false;
            foreach (var j in new[] { i - 1, i + 1 })
            {
                if (j < 0 || j >= valid.Count) continue;
                var other = valid[j].Value - median;
                if (Math.Sign(other) == Math.Sign(deviation) && Math.Abs(other) >= TrendDelta)
                    sameDirection = true;
            }
            if (!sameDirection) outliers.Add(day);
        }
        return outliers;
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~BbtAnalyzerTests"`
Expected: 6 PASS. Ha a "Rising_trend" teszt bukik, ellenőrizd: a 10. nap szomszéd-mediánja a 4–9. és 11–12. napokból jön, a 11. nap eltérése (36.68 − medián) ≥ 0,15 kell legyen.

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction/BbtAnalyzer.cs Mensi.Tests/Prediction/BbtAnalyzerTests.cs
git commit -m "feat: BBT coverline, outlier-kizárás és ovuláció-megerősítés"
```

---

### Task 8: Posterior + OvulationPosterior — a Bayes-szűrő magja

**Files:**
- Create: `Mensi.Core/Prediction/Posterior.cs`, `Mensi.Core/Prediction/OvulationPosterior.cs`
- Test: `Mensi.Tests/Prediction/OvulationPosteriorTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class Posterior
{
    public const int GridMin = 6, GridMax = 40;
    public double this[int day] { get; }               // 0, ha a rácson kívül
    public int Quantile(double q);                     // legkisebb nap, ahol a kumulatív ≥ q
    public double Sum { get; }                         // ~1.0 (tesztekhez)
    public static Posterior FromNormal(double mean, double variance);
    public static Posterior FromPointMass(int day);
    public Posterior Reweighted(Func<int, double> factor); // normalizálva; ha minden ~0 → változatlan
}

public sealed record ObservedDay(
    int CycleDay, Mensi.Core.Domain.CervicalMucus? Mucus, Mensi.Core.Domain.LhTest? Lh,
    Mensi.Core.Domain.CrampType? CrampType, short? CrampSeverity);

public static class OvulationPosterior
{
    public static Posterior Compute(
        double priorMean, double priorVariance,
        IReadOnlyList<ObservedDay> observations, BbtAnalysis bbt);
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/OvulationPosteriorTests.cs`:

```csharp
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class OvulationPosteriorTests
{
    private static readonly BbtAnalysis NoBbt =
        new(null, null, new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), 0);

    [Fact]
    public void Prior_without_observations_centers_on_prior_mean()
    {
        var p = OvulationPosterior.Compute(14, 9, [], NoBbt);
        Assert.InRange(p.Quantile(0.5), 13, 15);
        Assert.Equal(1.0, p.Sum, 9);
    }

    [Fact]
    public void Lh_peak_narrows_and_pulls_the_posterior()
    {
        var prior = Posterior.FromNormal(16, 9);
        var priorWidth = prior.Quantile(0.85) - prior.Quantile(0.15);

        var p = OvulationPosterior.Compute(16, 9,
            [new ObservedDay(13, null, LhTest.Peak, null, null)], NoBbt);
        var width = p.Quantile(0.85) - p.Quantile(0.15);

        Assert.InRange(p.Quantile(0.5), 12, 15); // a csúcs d−o ∈ [−1,0]-t preferál → o ∈ [13,14]
        Assert.True(width < priorWidth);
    }

    [Fact]
    public void Confirmed_bbt_shift_dominates()
    {
        var bbt = new BbtAnalysis(36.42m, 12, new HashSet<int>(), new HashSet<int>(),
            new HashSet<int> { 13, 14, 15 }, 12);
        var p = OvulationPosterior.Compute(17, 9, [], bbt);
        Assert.InRange(p.Quantile(0.5), 11, 13);
    }

    [Fact]
    public void Egg_white_mucus_shifts_mass_before_the_day()
    {
        var p = OvulationPosterior.Compute(14, 9,
            [new ObservedDay(12, CervicalMucus.EggWhite, null, null, null)], NoBbt);
        // nyúlós nyák d−o ∈ [−3..0] → o ∈ [12..15] súlyozott
        Assert.InRange(p.Quantile(0.5), 12, 15);
    }

    [Fact]
    public void Point_mass_factory_is_degenerate()
    {
        var p = Posterior.FromPointMass(14);
        Assert.Equal(14, p.Quantile(0.15));
        Assert.Equal(14, p.Quantile(0.85));
        Assert.Equal(1.0, p[14], 9);
    }

    [Fact]
    public void All_zero_reweight_falls_back_to_unweighted()
    {
        var p = Posterior.FromPointMass(14).Reweighted(_ => 0);
        Assert.Equal(14, p.Quantile(0.5));
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~OvulationPosteriorTests"`
Expected: FAIL.

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/Posterior.cs`:

```csharp
namespace Mensi.Core.Prediction;

/// <summary>Diszkrét eloszlás az ovulációs nap fölött a [GridMin, GridMax] ciklusnap-rácson.</summary>
public sealed class Posterior
{
    public const int GridMin = 6, GridMax = 40;
    private readonly double[] _p; // index 0 = GridMin

    private Posterior(double[] p) => _p = p;

    public double this[int day] =>
        day < GridMin || day > GridMax ? 0 : _p[day - GridMin];

    public double Sum => _p.Sum();

    public int Quantile(double q)
    {
        double cum = 0;
        for (var i = 0; i < _p.Length; i++)
        {
            cum += _p[i];
            if (cum >= q) return GridMin + i;
        }
        return GridMax;
    }

    public static Posterior FromNormal(double mean, double variance)
    {
        var v = Math.Max(variance, 0.25); // degenerált prior ellen
        var p = new double[GridMax - GridMin + 1];
        for (var i = 0; i < p.Length; i++)
        {
            var x = GridMin + i - mean;
            p[i] = Math.Exp(-x * x / (2 * v));
        }
        return new Posterior(Normalize(p));
    }

    public static Posterior FromPointMass(int day)
    {
        var p = new double[GridMax - GridMin + 1];
        p[Math.Clamp(day, GridMin, GridMax) - GridMin] = 1;
        return new Posterior(p);
    }

    public Posterior Reweighted(Func<int, double> factor)
    {
        var p = new double[_p.Length];
        for (var i = 0; i < p.Length; i++) p[i] = _p[i] * factor(GridMin + i);
        // Ha minden jel kioltja egymást, a súlyozatlan eloszlás marad — a modell nem "hal meg".
        return p.Sum() < 1e-12 ? this : new Posterior(Normalize(p));
    }

    private static double[] Normalize(double[] p)
    {
        var sum = p.Sum();
        if (sum <= 0) return p;
        for (var i = 0; i < p.Length; i++) p[i] /= sum;
        return p;
    }
}
```

`Mensi.Core/Prediction/OvulationPosterior.cs`:

```csharp
using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public sealed record ObservedDay(
    int CycleDay, CervicalMucus? Mucus, LhTest? Lh, CrampType? CrampType, short? CrampSeverity);

/// <summary>Szekvenciális Bayes-frissítés: prior a naptár-statisztikából, likelihood a napi
/// jelekből (spec 4.3 táblázata). A szorzók a specifikáció részei — a tesztek ezekre épülnek.</summary>
public static class OvulationPosterior
{
    public static Posterior Compute(
        double priorMean, double priorVariance,
        IReadOnlyList<ObservedDay> observations, BbtAnalysis bbt)
    {
        var posterior = Posterior.FromNormal(priorMean, priorVariance);

        foreach (var obs in observations)
        {
            if (obs.Lh is not null)
                posterior = posterior.Reweighted(o => LhFactor(obs.Lh.Value, obs.CycleDay - o));
            if (obs.Mucus is not null)
                posterior = posterior.Reweighted(o => MucusFactor(obs.Mucus.Value, obs.CycleDay - o));
            if (obs is { CrampType: CrampType.Abdomen, CrampSeverity: >= 1, CycleDay: > 8 })
                posterior = posterior.Reweighted(o =>
                    obs.CycleDay - o is >= -1 and <= 1 ? 1.6 : 0.95);
        }

        if (bbt.ConfirmedOvulationDay is int confirmed)
            posterior = posterior.Reweighted(o =>
                Math.Abs(o - confirmed) <= 1 ? 4.0 : 0.25);

        return posterior;
    }

    private static double LhFactor(LhTest lh, int rel) => lh switch
    {
        LhTest.Positive => rel is >= -2 and <= 0 ? 6 : rel is -3 or 1 ? 2 : 0.3,
        LhTest.Peak => rel is >= -1 and <= 0 ? 12 : rel is -2 or 1 ? 2 : 0.15,
        _ => rel is >= -1 and <= 1 ? 0.6 : 1.1, // Negative
    };

    private static double MucusFactor(CervicalMucus mucus, int rel) => mucus switch
    {
        CervicalMucus.EggWhite => rel is >= -3 and <= 0 ? 3 : rel is -4 or 1 ? 1.5 : 0.5,
        CervicalMucus.Creamy => rel is >= -4 and <= -1 ? 1.8 : 0.8,
        _ => rel is >= -2 and <= 1 ? 0.55 : 1.15, // Dry, Sticky
    };
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~OvulationPosteriorTests"`
Expected: 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction/Posterior.cs Mensi.Core/Prediction/OvulationPosterior.cs Mensi.Tests/Prediction/OvulationPosteriorTests.cs
git commit -m "feat: ovuláció-posterior szekvenciális Bayes-frissítéssel"
```

---

### Task 9: PeriodDistribution + ConfidenceLevel

**Files:**
- Create: `Mensi.Core/Prediction/PeriodDistribution.cs`
- Test: `Mensi.Tests/Prediction/PeriodDistributionTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class PeriodDistribution
{
    public const int LutealMin = 9, LutealMax = 18;
    // ciklusnapban: P(period = t) = Σ_o post(o)·P_luteális(t−o)
    public static (int P15, int P50, int P85) NextPeriod(
        Posterior ovulation, double lutealMean, double lutealVariance);
}
public static class ConfidenceRule
{
    // width = ovuláció P85 − P15 napokban; ≤4 High, ≤7 Medium, egyébként Low;
    // 3-nál kevesebb lezárt ciklusnál legfeljebb Medium.
    public static Mensi.Core.Domain.ConfidenceLevel From(int widthDays, int closedCycleCount);
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/PeriodDistributionTests.cs`:

```csharp
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class PeriodDistributionTests
{
    [Fact]
    public void Point_mass_ovulation_and_tight_luteal_give_exact_period_day()
    {
        var (p15, p50, p85) = PeriodDistribution.NextPeriod(
            Posterior.FromPointMass(14), 14, 0.0001);
        Assert.Equal(28, p15);
        Assert.Equal(28, p50);
        Assert.Equal(28, p85);
    }

    [Fact]
    public void Wider_luteal_variance_widens_the_band()
    {
        var (p15, p50, p85) = PeriodDistribution.NextPeriod(
            Posterior.FromPointMass(14), 14, 4);
        Assert.True(p15 < p50 && p50 < p85);
        Assert.InRange(p50, 27, 29);
    }

    [Fact]
    public void Luteal_is_clamped_to_9_18()
    {
        // extrém átlag mellett is a [9,18] vágott tartomány érvényesül
        var (p15, _, p85) = PeriodDistribution.NextPeriod(Posterior.FromPointMass(14), 25, 1);
        Assert.InRange(p15, 14 + 9, 14 + 18);
        Assert.InRange(p85, 14 + 9, 14 + 18);
    }

    [Theory]
    [InlineData(3, 6, ConfidenceLevel.High)]
    [InlineData(3, 2, ConfidenceLevel.Medium)]  // kevés ciklus lehúzza
    [InlineData(6, 6, ConfidenceLevel.Medium)]
    [InlineData(9, 6, ConfidenceLevel.Low)]
    public void Confidence_rule(int width, int cycles, ConfidenceLevel expected) =>
        Assert.Equal(expected, ConfidenceRule.From(width, cycles));
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~PeriodDistributionTests"`
Expected: FAIL.

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/PeriodDistribution.cs`:

```csharp
using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public static class PeriodDistribution
{
    public const int LutealMin = 9, LutealMax = 18;

    public static (int P15, int P50, int P85) NextPeriod(
        Posterior ovulation, double lutealMean, double lutealVariance)
    {
        // Diszkretizált, [9,18]-ra vágott luteális eloszlás.
        var v = Math.Max(lutealVariance, 0.0001);
        var luteal = new double[LutealMax - LutealMin + 1];
        for (var i = 0; i < luteal.Length; i++)
        {
            var x = LutealMin + i - lutealMean;
            luteal[i] = Math.Exp(-x * x / (2 * v));
        }
        var lutealSum = luteal.Sum();

        var minDay = Posterior.GridMin + LutealMin;
        var maxDay = Posterior.GridMax + LutealMax;
        var period = new double[maxDay - minDay + 1];
        for (var o = Posterior.GridMin; o <= Posterior.GridMax; o++)
        {
            var po = ovulation[o];
            if (po <= 0) continue;
            for (var i = 0; i < luteal.Length; i++)
                period[o + LutealMin + i - minDay] += po * luteal[i] / lutealSum;
        }

        return (Quantile(0.15), Quantile(0.50), Quantile(0.85));

        int Quantile(double q)
        {
            var total = period.Sum();
            double cum = 0;
            for (var i = 0; i < period.Length; i++)
            {
                cum += period[i];
                if (cum >= q * total) return minDay + i;
            }
            return maxDay;
        }
    }
}

public static class ConfidenceRule
{
    public static ConfidenceLevel From(int widthDays, int closedCycleCount)
    {
        var level = widthDays <= 4 ? ConfidenceLevel.High
            : widthDays <= 7 ? ConfidenceLevel.Medium
            : ConfidenceLevel.Low;
        if (closedCycleCount < 3 && level == ConfidenceLevel.High) level = ConfidenceLevel.Medium;
        return level;
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~PeriodDistributionTests"`
Expected: 7 PASS (3 Fact + 4 Theory-eset).

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction/PeriodDistribution.cs Mensi.Tests/Prediction/PeriodDistributionTests.cs
git commit -m "feat: menstruáció-eloszlás konvolúcióval + konfidencia-szabály"
```

---

### Task 10: WilcoxKernel — fogamzási esély, minősítés, mit-ha tipp

**Files:**
- Create: `Mensi.Core/Prediction/WilcoxKernel.cs`
- Test: `Mensi.Tests/Prediction/WilcoxKernelTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class WilcoxKernel
{
    public static readonly double[] DailyP = [0.10, 0.16, 0.14, 0.27, 0.31, 0.33]; // d−o = −5…0
    public static double DayProbability(int dayMinusOvulation);
    public static double CycleChance(Posterior ovulation, IReadOnlyCollection<int> unprotectedDays);
    public static Mensi.Core.Domain.TimingLabel Label(double chance); // <0.08 Weak, ≤0.16 Medium, > Good
    public static double RetroChance(int ovulationDay, IReadOnlyCollection<int> unprotectedDays); // Normal(o,1.5²)
    public static string? WhatIfHint(Posterior ovulation, IReadOnlyCollection<int> unprotectedDays,
        int todayCycleDay, int fertileEndDay);
    public static string LabelHu(Mensi.Core.Domain.TimingLabel label); // Gyenge/Közepes/Jó
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/WilcoxKernelTests.cs`:

```csharp
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class WilcoxKernelTests
{
    [Theory]
    [InlineData(-5, 0.10)]
    [InlineData(-1, 0.31)]
    [InlineData(0, 0.33)]
    [InlineData(1, 0.0)]
    [InlineData(-6, 0.0)]
    public void Kernel_matches_published_values(int rel, double expected) =>
        Assert.Equal(expected, WilcoxKernel.DayProbability(rel), 9);

    [Fact]
    public void Single_intercourse_day_before_point_mass_ovulation()
    {
        var chance = WilcoxKernel.CycleChance(Posterior.FromPointMass(14), [13]);
        Assert.Equal(0.31, chance, 9);
        Assert.Equal(TimingLabel.Good, WilcoxKernel.Label(chance));
    }

    [Fact]
    public void Multiple_days_combine_with_complement_product()
    {
        // 1 − (1−0.31)(1−0.33) = 0.5377
        var chance = WilcoxKernel.CycleChance(Posterior.FromPointMass(14), [13, 14]);
        Assert.Equal(0.5377, chance, 4);
    }

    [Fact]
    public void No_intercourse_is_zero_and_weak()
    {
        Assert.Equal(0, WilcoxKernel.CycleChance(Posterior.FromPointMass(14), []));
        Assert.Equal(TimingLabel.Weak, WilcoxKernel.Label(0));
    }

    [Theory]
    [InlineData(0.079, TimingLabel.Weak)]
    [InlineData(0.08, TimingLabel.Medium)]
    [InlineData(0.16, TimingLabel.Medium)]
    [InlineData(0.161, TimingLabel.Good)]
    public void Label_thresholds(double chance, TimingLabel expected) =>
        Assert.Equal(expected, WilcoxKernel.Label(chance));

    [Fact]
    public void What_if_improving_today_and_tomorrow_names_both()
    {
        var hint = WilcoxKernel.WhatIfHint(Posterior.FromPointMass(14), [], 13, 15);
        Assert.Equal("Ha ma vagy holnap van együttlét, a minősítés Jó lesz.", hint);
    }

    [Fact]
    public void What_if_after_fertile_window_is_null()
    {
        Assert.Null(WilcoxKernel.WhatIfHint(Posterior.FromPointMass(14), [], 16, 15));
    }

    [Fact]
    public void Retro_chance_spreads_around_confirmed_day()
    {
        var exact = WilcoxKernel.CycleChance(Posterior.FromPointMass(14), [13]);
        var retro = WilcoxKernel.RetroChance(14, [13]);
        Assert.InRange(retro, exact * 0.5, exact); // szórt posterior kicsit kisebb esélyt ad
        Assert.True(retro > 0.15);
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~WilcoxKernelTests"`
Expected: FAIL.

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/WilcoxKernel.cs`:

```csharp
using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

/// <summary>Wilcox et al. 1995 (NEJM) napi fogamzási valószínűségei a 6 napos termékeny
/// ablakra, az ovuláció-posterior fölött várható értékkel (spec 4.5).</summary>
public static class WilcoxKernel
{
    public static readonly double[] DailyP = [0.10, 0.16, 0.14, 0.27, 0.31, 0.33]; // d−o = −5…0

    public static double DayProbability(int rel) =>
        rel is >= -5 and <= 0 ? DailyP[rel + 5] : 0;

    public static double CycleChance(Posterior ovulation, IReadOnlyCollection<int> unprotectedDays)
    {
        if (unprotectedDays.Count == 0) return 0;
        double expected = 0;
        for (var o = Posterior.GridMin; o <= Posterior.GridMax; o++)
        {
            var po = ovulation[o];
            if (po <= 0) continue;
            var miss = unprotectedDays.Aggregate(1.0, (acc, d) => acc * (1 - DayProbability(d - o)));
            expected += po * (1 - miss);
        }
        return expected;
    }

    public static TimingLabel Label(double chance) =>
        chance < 0.08 ? TimingLabel.Weak : chance <= 0.16 ? TimingLabel.Medium : TimingLabel.Good;

    public static string LabelHu(TimingLabel label) => label switch
    {
        TimingLabel.Weak => "Gyenge",
        TimingLabel.Medium => "Közepes",
        _ => "Jó",
    };

    /// <summary>Lezárt ciklus visszamenőleges minősítése a megerősített/becsült nap köré
    /// húzott Normal(o, 1.5²) posteriorral.</summary>
    public static double RetroChance(int ovulationDay, IReadOnlyCollection<int> unprotectedDays) =>
        CycleChance(Posterior.FromNormal(ovulationDay, 2.25), unprotectedDays);

    public static string? WhatIfHint(Posterior ovulation, IReadOnlyCollection<int> unprotectedDays,
        int todayCycleDay, int fertileEndDay)
    {
        if (todayCycleDay > fertileEndDay) return null;
        var current = Label(CycleChance(ovulation, unprotectedDays));

        var withToday = Label(CycleChance(ovulation, [.. unprotectedDays, todayCycleDay]));
        var tomorrow = todayCycleDay + 1;
        var withTomorrow = tomorrow <= fertileEndDay
            ? Label(CycleChance(ovulation, [.. unprotectedDays, tomorrow]))
            : current;

        if (withToday > current && withTomorrow > current && withToday == withTomorrow)
            return $"Ha ma vagy holnap van együttlét, a minősítés {LabelHu(withToday)} lesz.";
        if (withToday > current)
            return $"Ha ma van együttlét, a minősítés {LabelHu(withToday)} lesz.";
        if (withTomorrow > current)
            return $"Ha holnap van együttlét, a minősítés {LabelHu(withTomorrow)} lesz.";
        return null;
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~WilcoxKernelTests"`
Expected: minden PASS (5+4 Theory-eset + 5 Fact).

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction/WilcoxKernel.cs Mensi.Tests/Prediction/WilcoxKernelTests.cs
git commit -m "feat: Wilcox-kernel esély, minősítés és mit-ha tipp"
```

---

### Task 11: PredictionEngine — orchestrator, fázisok, headline, terhesség-jelzés

**Files:**
- Create: `Mensi.Core/Prediction/PredictionEngine.cs`, `Mensi.Core/Prediction/DayCategorizer.cs`
- Test: `Mensi.Tests/Prediction/PredictionEngineTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record DailyLogSnapshot(
    DateOnly Date, decimal? Bbt, CervicalMucus? Mucus, LhTest? Lh,
    CrampType? CrampType, short? CrampSeverity, FlowIntensity? Flow,
    bool PeriodStart, int IntercourseCount, int UnprotectedCount);

public sealed record EngineInput(
    IReadOnlyList<ClosedCycleStat> ClosedCycles,
    DateOnly CurrentCycleStart,
    IReadOnlyList<DailyLogSnapshot> CurrentCycleLogs, // csak az aktuális ciklus napjai
    DateOnly Today);

public sealed record PhaseInfo(DayCategory Key, string Label, int TotalDays, int ElapsedDays, int RemainingDays);

public sealed record CyclePrediction(
    DateOnly CycleStart, int CycleDay,
    DateOnly OvulationFrom, DateOnly OvulationP50, DateOnly OvulationTo,
    DateOnly PeriodFrom, DateOnly PeriodP50, DateOnly PeriodTo,
    DateOnly FertileFrom, DateOnly FertileTo,
    ConfidenceLevel Confidence, double Chance, TimingLabel Timing,
    string? WhatIfHint, string? PregnancyHint, string Headline,
    PhaseInfo Phase, BbtAnalysis Bbt, Posterior OvulationPosterior, int MenstruationEndDay)
{
    public DayCategory Categorize(DateOnly date); // aktuális ciklus napjaira (spec 4.7)
}

public static class PredictionEngine
{
    public static CyclePrediction? Evaluate(EngineInput input); // null, ha nincs lezárt ciklus
}

public static class DayCategorizer
{
    // lezárt ciklus napjaira, naptár-visszatekintéshez
    public static DayCategory CategorizeClosed(
        DateOnly date, DateOnly cycleStart, int lengthDays, int? ovulationDay,
        IReadOnlySet<DateOnly> flowDays);
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/PredictionEngineTests.cs`:

```csharp
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class PredictionEngineTests
{
    private static readonly DateOnly Start = new(2026, 8, 10);

    private static ClosedCycleStat Closed(int offsetDays, int len, int? luteal) =>
        new(Start.AddDays(offsetDays), len, luteal, false, null);

    /// <summary>3 lezárt ciklus (28/27/29, luteális 13/14/13) + nyitott ciklus a 14. napon,
    /// LH-csúccsal a 13. napon és egy védekezés nélküli együttléttel a 12. napon.</summary>
    private static EngineInput Input(DateOnly? today = null, bool lhPeak = true)
    {
        var logs = new List<DailyLogSnapshot>();
        for (var d = 1; d <= 14; d++)
        {
            logs.Add(new DailyLogSnapshot(
                Start.AddDays(d - 1),
                Bbt: 36.30m + (d % 3) * 0.03m,
                Mucus: d >= 11 ? CervicalMucus.EggWhite : null,
                Lh: lhPeak && d == 13 ? LhTest.Peak : null,
                CrampType: null, CrampSeverity: null,
                Flow: d <= 5 ? FlowIntensity.Medium : null,
                PeriodStart: d == 1,
                IntercourseCount: d == 12 ? 1 : 0,
                UnprotectedCount: d == 12 ? 1 : 0));
        }
        return new EngineInput(
            [Closed(-84, 28, 13), Closed(-56, 27, 14), Closed(-29, 29, 13)],
            Start, logs, today ?? Start.AddDays(13));
    }

    [Fact]
    public void No_closed_cycles_yields_null() =>
        Assert.Null(PredictionEngine.Evaluate(new EngineInput([], Start, [], Start)));

    [Fact]
    public void Windows_are_ordered_and_lh_peak_centers_ovulation()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.True(p.OvulationFrom <= p.OvulationP50);
        Assert.True(p.OvulationP50 <= p.OvulationTo);
        Assert.True(p.PeriodFrom <= p.PeriodTo);
        Assert.True(p.OvulationTo < p.PeriodFrom);
        // LH-csúcs a 13. napon → a medián a 12–15. ciklusnap környékén
        var p50Day = p.OvulationP50.DayNumber - Start.DayNumber + 1;
        Assert.InRange(p50Day, 12, 15);
        Assert.Equal(14, p.CycleDay);
    }

    [Fact]
    public void Timing_reflects_logged_intercourse()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.True(p.Chance > 0);
        Assert.NotEqual(TimingLabel.Weak, p.Timing);
    }

    [Fact]
    public void Categorize_maps_the_whole_cycle()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.Equal(DayCategory.Menstruation, p.Categorize(Start));            // 1. nap, flow
        Assert.Equal(DayCategory.Ovulation, p.Categorize(p.OvulationP50));
        Assert.Equal(DayCategory.PredictedPeriod, p.Categorize(p.PeriodP50));
        Assert.Equal(DayCategory.Luteal, p.Categorize(p.OvulationTo.AddDays(1)));
        Assert.Equal(5, p.MenstruationEndDay);
    }

    [Fact]
    public void Headline_and_phase_follow_todays_category()
    {
        var p = PredictionEngine.Evaluate(Input())!;
        Assert.False(string.IsNullOrWhiteSpace(p.Headline));
        Assert.Equal(p.Categorize(Start.AddDays(13)), p.Phase.Key);
        Assert.True(p.Phase.TotalDays >= p.Phase.ElapsedDays);
    }

    [Fact]
    public void No_pregnancy_hint_mid_cycle() =>
        Assert.Null(PredictionEngine.Evaluate(Input())!.PregnancyHint);

    [Fact]
    public void Pregnancy_hint_when_period_is_late_and_bbt_stays_high()
    {
        // nyitott ciklus 34. napja: 15. naptól magas BBT (megerősített shift), nincs vérzés
        var logs = new List<DailyLogSnapshot>();
        for (var d = 1; d <= 34; d++)
        {
            logs.Add(new DailyLogSnapshot(
                Start.AddDays(d - 1),
                Bbt: d <= 14 ? 36.35m : 36.70m,
                Mucus: null, Lh: null, CrampType: null, CrampSeverity: null,
                Flow: d <= 5 ? FlowIntensity.Medium : null,
                PeriodStart: d == 1, IntercourseCount: 0, UnprotectedCount: 0));
        }
        var input = new EngineInput(
            [Closed(-84, 28, 13), Closed(-56, 27, 14), Closed(-29, 29, 13)],
            Start, logs, Start.AddDays(33));
        var p = PredictionEngine.Evaluate(input)!;
        Assert.NotNull(p.PregnancyHint);
    }

    [Fact]
    public void Closed_cycle_categorizer_uses_flow_and_ovulation_day()
    {
        var flow = new HashSet<DateOnly> { Start, Start.AddDays(1), Start.AddDays(2) };
        Assert.Equal(DayCategory.Menstruation,
            DayCategorizer.CategorizeClosed(Start, Start, 28, 14, flow));
        Assert.Equal(DayCategory.Ovulation,
            DayCategorizer.CategorizeClosed(Start.AddDays(13), Start, 28, 14, flow)); // 14. nap
        Assert.Equal(DayCategory.Fertile,
            DayCategorizer.CategorizeClosed(Start.AddDays(9), Start, 28, 14, flow));  // 10. nap
        Assert.Equal(DayCategory.Luteal,
            DayCategorizer.CategorizeClosed(Start.AddDays(20), Start, 28, 14, flow));
        Assert.Equal(DayCategory.Follicular,
            DayCategorizer.CategorizeClosed(Start.AddDays(5), Start, 28, 14, flow));
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~PredictionEngineTests"`
Expected: FAIL.

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/DayCategorizer.cs`:

```csharp
using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public static class DayCategorizer
{
    /// <summary>Lezárt ciklus napjának kategóriája: vérzésnapok a logból, az ovuláció köré
    /// ±1 nap ablak, előtte 4 termékeny nap — visszatekintő nézetekhez.</summary>
    public static DayCategory CategorizeClosed(
        DateOnly date, DateOnly cycleStart, int lengthDays, int? ovulationDay,
        IReadOnlySet<DateOnly> flowDays)
    {
        var day = date.DayNumber - cycleStart.DayNumber + 1;
        if (day < 1 || day > lengthDays) return DayCategory.Unknown;
        if (flowDays.Contains(date)) return DayCategory.Menstruation;
        if (ovulationDay is not int o) return DayCategory.Follicular;
        return day switch
        {
            _ when day >= o - 1 && day <= o + 1 => DayCategory.Ovulation,
            _ when day >= o - 5 && day <= o - 2 => DayCategory.Fertile,
            _ when day > o + 1 => DayCategory.Luteal,
            _ => DayCategory.Follicular,
        };
    }
}
```

`Mensi.Core/Prediction/PredictionEngine.cs`:

```csharp
using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public sealed record DailyLogSnapshot(
    DateOnly Date, decimal? Bbt, CervicalMucus? Mucus, LhTest? Lh,
    CrampType? CrampType, short? CrampSeverity, FlowIntensity? Flow,
    bool PeriodStart, int IntercourseCount, int UnprotectedCount);

public sealed record EngineInput(
    IReadOnlyList<ClosedCycleStat> ClosedCycles,
    DateOnly CurrentCycleStart,
    IReadOnlyList<DailyLogSnapshot> CurrentCycleLogs,
    DateOnly Today);

public sealed record PhaseInfo(DayCategory Key, string Label, int TotalDays, int ElapsedDays, int RemainingDays);

public sealed record CyclePrediction(
    DateOnly CycleStart, int CycleDay,
    DateOnly OvulationFrom, DateOnly OvulationP50, DateOnly OvulationTo,
    DateOnly PeriodFrom, DateOnly PeriodP50, DateOnly PeriodTo,
    DateOnly FertileFrom, DateOnly FertileTo,
    ConfidenceLevel Confidence, double Chance, TimingLabel Timing,
    string? WhatIfHint, string? PregnancyHint, string Headline,
    PhaseInfo Phase, BbtAnalysis Bbt, Posterior OvulationPosterior, int MenstruationEndDay)
{
    public DayCategory Categorize(DateOnly date)
    {
        var day = date.DayNumber - CycleStart.DayNumber + 1;
        if (day < 1) return DayCategory.Unknown;
        if (day <= MenstruationEndDay) return DayCategory.Menstruation;
        if (date >= OvulationFrom && date <= OvulationTo) return DayCategory.Ovulation;
        if (date >= FertileFrom && date < OvulationFrom) return DayCategory.Fertile;
        if (date >= PeriodFrom && date <= PeriodTo) return DayCategory.PredictedPeriod;
        if (date > OvulationTo && date < PeriodFrom) return DayCategory.Luteal;
        if (date > PeriodTo) return DayCategory.Unknown;
        return DayCategory.Follicular;
    }
}

public static class PredictionEngine
{
    public static CyclePrediction? Evaluate(EngineInput input)
    {
        var stats = CycleStats.Compute(input.ClosedCycles);
        if (stats is null) return null;

        var start = input.CurrentCycleStart;
        var today = input.Today;
        var cycleDay = today.DayNumber - start.DayNumber + 1;

        // Személyes paraméterek shrinkage-dzsel (spec 4.2–4.3).
        var (cycleMean, cycleVar) = Shrinkage.Apply(
            PopulationPriors.CycleMean, PopulationPriors.CycleVar,
            stats.ClosedCount, stats.EwmaLength, stats.StdDevLength * stats.StdDevLength);
        var (lutealMean, lutealVar) = Shrinkage.Apply(
            PopulationPriors.LutealMean, PopulationPriors.LutealVar,
            stats.ConfirmedLutealCount, stats.MeanLuteal ?? PopulationPriors.LutealMean,
            (stats.StdDevLuteal ?? 0) * (stats.StdDevLuteal ?? 0));

        var bbt = BbtAnalyzer.Analyze(BuildBbtDays(input.CurrentCycleLogs, start, cycleDay));
        var observations = input.CurrentCycleLogs
            .Select(l => new ObservedDay(
                l.Date.DayNumber - start.DayNumber + 1, l.Mucus, l.Lh, l.CrampType, l.CrampSeverity))
            .ToList();

        var posterior = OvulationPosterior.Compute(
            cycleMean - lutealMean, cycleVar + lutealVar, observations, bbt);

        var ovuFromDay = posterior.Quantile(0.15);
        var ovuP50Day = posterior.Quantile(0.50);
        var ovuToDay = posterior.Quantile(0.85);
        var (perFromDay, perP50Day, perToDay) =
            PeriodDistribution.NextPeriod(posterior, lutealMean, lutealVar);

        var fertileFromDay = Math.Min(ovuP50Day - 5, ovuFromDay);

        DateOnly D(int day) => start.AddDays(day - 1);

        var unprotectedDays = input.CurrentCycleLogs
            .Where(l => l.UnprotectedCount > 0)
            .Select(l => l.Date.DayNumber - start.DayNumber + 1).ToList();
        var chance = WilcoxKernel.CycleChance(posterior, unprotectedDays);
        var whatIf = WilcoxKernel.WhatIfHint(posterior, unprotectedDays, cycleDay, ovuToDay);

        var mensEnd = MenstruationEnd(input.CurrentCycleLogs, start);
        var confidence = ConfidenceRule.From(ovuToDay - ovuFromDay, stats.ClosedCount);
        var pregnancy = PregnancyHint(input, bbt, D(perToDay), lutealMean, start, today);

        var prediction = new CyclePrediction(
            start, cycleDay,
            D(ovuFromDay), D(ovuP50Day), D(ovuToDay),
            D(perFromDay), D(perP50Day), D(perToDay),
            D(fertileFromDay), D(ovuToDay),
            confidence, chance, WilcoxKernel.Label(chance),
            whatIf, pregnancy, "", PhaseOf(DayCategory.Unknown, 1, 1, 0), bbt, posterior, mensEnd);

        var phase = BuildPhase(prediction, today);
        return prediction with { Phase = phase, Headline = Headline(prediction, phase, today) };
    }

    private static IReadOnlyList<BbtDay> BuildBbtDays(
        IReadOnlyList<DailyLogSnapshot> logs, DateOnly start, int cycleDay)
    {
        var byDay = logs.ToDictionary(l => l.Date.DayNumber - start.DayNumber + 1);
        return Enumerable.Range(1, Math.Max(cycleDay, 1))
            .Select(d => new BbtDay(d, byDay.TryGetValue(d, out var l) ? l.Bbt : null))
            .ToList();
    }

    private static int MenstruationEnd(IReadOnlyList<DailyLogSnapshot> logs, DateOnly start)
    {
        var byDay = logs.ToDictionary(l => l.Date.DayNumber - start.DayNumber + 1);
        var end = 1; // az 1. nap definíció szerint menstruáció (period_start jelölte ki)
        for (var d = 1; byDay.TryGetValue(d, out var l) && l.Flow >= FlowIntensity.Light; d++)
            end = d;
        return end;
    }

    private static PhaseInfo BuildPhase(CyclePrediction p, DateOnly today)
    {
        var category = p.Categorize(today);
        var (from, to) = category switch
        {
            DayCategory.Menstruation => (p.CycleStart, p.CycleStart.AddDays(p.MenstruationEndDay - 1)),
            DayCategory.Follicular => (p.CycleStart.AddDays(p.MenstruationEndDay), p.FertileFrom.AddDays(-1)),
            DayCategory.Fertile => (p.FertileFrom, p.OvulationFrom.AddDays(-1)),
            DayCategory.Ovulation => (p.OvulationFrom, p.OvulationTo),
            DayCategory.Luteal => (p.OvulationTo.AddDays(1), p.PeriodFrom.AddDays(-1)),
            DayCategory.PredictedPeriod => (p.PeriodFrom, p.PeriodTo),
            _ => (today, today),
        };
        var total = Math.Max(to.DayNumber - from.DayNumber + 1, 1);
        var elapsed = Math.Clamp(today.DayNumber - from.DayNumber + 1, 0, total);
        return PhaseOf(category, total, elapsed, total - elapsed);
    }

    private static PhaseInfo PhaseOf(DayCategory key, int total, int elapsed, int remaining) =>
        new(key, key switch
        {
            DayCategory.Menstruation => "Menstruáció",
            DayCategory.Follicular => "Folliculáris szakasz",
            DayCategory.Fertile => "Termékeny ablak",
            DayCategory.Ovulation => "Ovulációs ablak",
            DayCategory.Luteal => "Luteális fázis",
            DayCategory.PredictedPeriod => "Becsült menstruáció",
            _ => "Cikluson túl",
        }, total, elapsed, remaining);

    private static string Headline(CyclePrediction p, PhaseInfo phase, DateOnly today) =>
        phase.Key switch
        {
            DayCategory.Menstruation => $"Menstruáció — a ciklus {p.CycleDay}. napja.",
            DayCategory.Follicular =>
                $"Follikuláris szakasz — a termékeny ablak {p.FertileFrom.DayNumber - today.DayNumber} nap múlva kezdődik.",
            DayCategory.Fertile =>
                $"Termékeny ablakban vagy — az ovuláció {p.OvulationTo.DayNumber - today.DayNumber} napon belül várható.",
            DayCategory.Ovulation => "Ovulációs ablakban vagy — most a legnagyobb az esély.",
            DayCategory.Luteal =>
                $"Luteális fázis — a következő menstruáció {p.PeriodTo.DayNumber - today.DayNumber} napon belül várható.",
            DayCategory.PredictedPeriod => "A menstruáció ezekben a napokban várható.",
            _ => "A ciklus a becsült hossznál hosszabb — ha nincs vérzés, érdemes tesztet fontolóra venni.",
        };

    private static string? PregnancyHint(
        EngineInput input, BbtAnalysis bbt, DateOnly periodTo,
        double lutealMean, DateOnly start, DateOnly today)
    {
        var noFlowSincePredicted = !input.CurrentCycleLogs.Any(l =>
            l.Date >= periodTo.AddDays(-(int)lutealMean) && l.Flow >= FlowIntensity.Light
            && l.Date.DayNumber - start.DayNumber + 1 > 6);

        // 1. szabály: a predikció felső határa elmúlt, a BBT az utolsó 3 mérésben coverline fölött.
        if (today > periodTo && bbt.Coverline is not null && noFlowSincePredicted)
        {
            var lastThree = input.CurrentCycleLogs
                .Where(l => l.Bbt is not null).OrderBy(l => l.Date).TakeLast(3).ToList();
            if (lastThree.Count == 3 && lastThree.All(l => l.Bbt > bbt.Coverline))
                return "A menstruáció a becsült ablakhoz képest késik, és a testhő emelkedett maradt "
                     + "— érdemes hCG-tesztet végezni.";
        }

        // 2. szabály: megerősített ovuláció után a luteális + 3 napnál tovább magas a BBT.
        if (bbt.ConfirmedOvulationDay is int o)
        {
            var daysSinceOvu = today.DayNumber - start.AddDays(o - 1).DayNumber;
            if (daysSinceOvu > lutealMean + 3 && noFlowSincePredicted
                && bbt.AboveCoverlineDays.Count >= 3)
                return "A luteális fázis a szokásosnál hosszabb, a testhő emelkedett és nincs vérzés "
                     + "— érdemes hCG-tesztet végezni.";
        }
        return null;
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~PredictionEngineTests"`
Expected: 8 PASS. Ha a `Pregnancy_hint` teszt bukik, ellenőrizd: a fixture BBT-je a 15. naptól magas → a coverline-shift megerősül, a 34. napi today túl van a period P85-ön (28–29 nap + s).

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction Mensi.Tests/Prediction
git commit -m "feat: predikciós orchestrator fázisokkal, headline-nal, terhesség-jelzéssel"
```

---

### Task 12: CycleDeriver + LengthPredictor + CycleRecomputeService

**Files:**
- Create: `Mensi.Core/Prediction/CycleDeriver.cs`, `Mensi.Core/Services/CycleRecomputeService.cs`
- Test: `Mensi.Tests/Prediction/CycleDeriverTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record DerivedCycle(
    DateOnly Start, int? LengthDays, int? OvulationConfirmed, int? OvulationEstimated,
    int? LutealLength, bool Anovulatory);

public static class CycleDeriver
{
    // allLogs: minden napi log időrendben; a period_start=true napok a ciklushatárok
    public static List<DerivedCycle> Derive(IReadOnlyList<DailyLogSnapshot> allLogs);
}

public static class LengthPredictor
{
    // round(shrinkelt EWMA); null, ha nincs lezárt ciklus
    public static int? Predict(IReadOnlyList<ClosedCycleStat> closedCycles);
}

public sealed class CycleRecomputeService(Mensi.Core.Data.MensiDbContext db, TimeProvider clock)
{
    public Task RecomputeAsync(CancellationToken ct = default);
    // a cycle táblát szinkronizálja a Derive eredményével; PredictedLengthDays-t csak
    // ÚJ sor kap (LengthPredictor a korábbi lezártakból), meglévő sorét nem írja át
}
```

- [ ] **Step 1: Failing tesztek**

`Mensi.Tests/Prediction/CycleDeriverTests.cs`:

```csharp
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests.Prediction;

public class CycleDeriverTests
{
    private static DailyLogSnapshot Day(DateOnly date, bool periodStart = false,
        decimal? bbt = null, FlowIntensity? flow = null) =>
        new(date, bbt, null, null, null, null, flow, periodStart, 0, 0);

    private static readonly DateOnly C1 = new(2026, 6, 1);

    [Fact]
    public void Period_start_days_split_cycles_and_last_stays_open()
    {
        var logs = new List<DailyLogSnapshot>
        {
            Day(C1, periodStart: true, flow: FlowIntensity.Heavy),
            Day(C1.AddDays(28), periodStart: true, flow: FlowIntensity.Medium),
            Day(C1.AddDays(28 + 27), periodStart: true, flow: FlowIntensity.Medium),
        };
        var cycles = CycleDeriver.Derive(logs);
        Assert.Equal(3, cycles.Count);
        Assert.Equal(28, cycles[0].LengthDays);
        Assert.Equal(27, cycles[1].LengthDays);
        Assert.Null(cycles[2].LengthDays); // nyitott
    }

    [Fact]
    public void Confirmed_bbt_shift_sets_luteal_and_estimate()
    {
        var logs = new List<DailyLogSnapshot> { Day(C1, periodStart: true, flow: FlowIntensity.Heavy) };
        // 2..13. nap alacsony, 14..16. nap magas → ovuláció a 13. napon
        for (var d = 2; d <= 13; d++) logs.Add(Day(C1.AddDays(d - 1), bbt: 36.30m + (d % 4) * 0.03m));
        for (var d = 14; d <= 27; d++) logs.Add(Day(C1.AddDays(d - 1), bbt: 36.70m));
        logs.Add(Day(C1.AddDays(27), periodStart: true, flow: FlowIntensity.Medium)); // 28 napos ciklus

        var cycles = CycleDeriver.Derive(logs);
        Assert.Equal(13, cycles[0].OvulationConfirmed);
        Assert.Equal(13, cycles[0].OvulationEstimated);
        Assert.Equal(28 - 13, cycles[0].LutealLength);
        Assert.False(cycles[0].Anovulatory);
    }

    [Fact]
    public void Enough_bbt_without_shift_marks_anovulatory()
    {
        var logs = new List<DailyLogSnapshot> { Day(C1, periodStart: true, flow: FlowIntensity.Heavy) };
        for (var d = 2; d <= 27; d++) logs.Add(Day(C1.AddDays(d - 1), bbt: 36.35m)); // sima, nincs shift
        logs.Add(Day(C1.AddDays(27), periodStart: true, flow: FlowIntensity.Medium));

        var c = CycleDeriver.Derive(logs)[0];
        Assert.True(c.Anovulatory);
        Assert.Null(c.LutealLength);
        Assert.Equal(28 - 14, c.OvulationEstimated); // fallback: hossz − 14
    }

    [Fact]
    public void Sparse_bbt_is_not_judged_anovulatory()
    {
        var logs = new List<DailyLogSnapshot>
        {
            Day(C1, periodStart: true, flow: FlowIntensity.Heavy),
            Day(C1.AddDays(5), bbt: 36.40m),
            Day(C1.AddDays(28), periodStart: true, flow: FlowIntensity.Medium),
        };
        Assert.False(CycleDeriver.Derive(logs)[0].Anovulatory);
    }

    [Fact]
    public void Length_predictor_rounds_the_shrunken_ewma()
    {
        // EWMA(28, 30) = 28.54; shrink(pop 28/16, n=2, s²=2) ≈ 28.51 → 29
        var predicted = LengthPredictor.Predict([
            new ClosedCycleStat(C1, 28, null, false, null),
            new ClosedCycleStat(C1.AddDays(28), 30, null, false, null)]);
        Assert.Equal(29, predicted);
        Assert.Null(LengthPredictor.Predict([]));
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~CycleDeriverTests"`
Expected: FAIL.

- [ ] **Step 3: Implementáció**

`Mensi.Core/Prediction/CycleDeriver.cs`:

```csharp
using Mensi.Core.Domain;

namespace Mensi.Core.Prediction;

public sealed record DerivedCycle(
    DateOnly Start, int? LengthDays, int? OvulationConfirmed, int? OvulationEstimated,
    int? LutealLength, bool Anovulatory);

public static class CycleDeriver
{
    /// <summary>Ennyi érvényes BBT-mérés alatt nem ítélünk anovulatorikusnak egy ciklust —
    /// az adat hiánya nem a bifázisos mintázat hiánya.</summary>
    public const int MinBbtForAnovulatory = 10;

    public static List<DerivedCycle> Derive(IReadOnlyList<DailyLogSnapshot> allLogs)
    {
        var ordered = allLogs.OrderBy(l => l.Date).ToList();
        var starts = ordered.Where(l => l.PeriodStart).Select(l => l.Date).ToList();
        var result = new List<DerivedCycle>();

        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            DateOnly? next = i + 1 < starts.Count ? starts[i + 1] : null;
            int? length = next is null ? null : next.Value.DayNumber - start.DayNumber;

            var cycleLogs = ordered
                .Where(l => l.Date >= start && (next is null || l.Date < next.Value))
                .ToList();
            var lastDay = cycleLogs.Count == 0
                ? 1
                : cycleLogs[^1].Date.DayNumber - start.DayNumber + 1;
            var byDay = cycleLogs.ToDictionary(l => l.Date.DayNumber - start.DayNumber + 1);
            var bbtDays = Enumerable.Range(1, Math.Max(length ?? lastDay, 1))
                .Select(d => new BbtDay(d, byDay.TryGetValue(d, out var l) ? l.Bbt : null))
                .ToList();

            var bbt = BbtAnalyzer.Analyze(bbtDays);
            int? confirmed = bbt.ConfirmedOvulationDay;
            int? luteal = length is int len && confirmed is int o ? len - o : null;
            var anovulatory = length is not null && confirmed is null
                && bbt.ValidCount >= MinBbtForAnovulatory;
            int? estimated = confirmed ?? (length is int l2 ? Math.Max(l2 - 14, 1) : null);

            result.Add(new DerivedCycle(start, length, confirmed, estimated, luteal, anovulatory));
        }
        return result;
    }
}

public static class LengthPredictor
{
    public static int? Predict(IReadOnlyList<ClosedCycleStat> closedCycles)
    {
        var stats = CycleStats.Compute(closedCycles);
        if (stats is null) return null;
        var (mean, _) = Shrinkage.Apply(
            PopulationPriors.CycleMean, PopulationPriors.CycleVar,
            stats.ClosedCount, stats.EwmaLength, stats.StdDevLength * stats.StdDevLength);
        return (int)Math.Round(mean, MidpointRounding.AwayFromZero);
    }
}
```

`Mensi.Core/Services/CycleRecomputeService.cs`:

```csharp
using Mensi.Core.Data;
using Mensi.Core.Domain;
using Mensi.Core.Prediction;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Core.Services;

/// <summary>Minden írás után a cycle tábla determinisztikusan újraépül a napi logokból.
/// Egyetlen kivétel a PredictedLengthDays: azt a sor születésekor írjuk (az akkori modell
/// becslése), és soha nem számoljuk újra — ebből lesz a "késés" statisztika.</summary>
public sealed class CycleRecomputeService(MensiDbContext db, TimeProvider clock)
{
    public async Task RecomputeAsync(CancellationToken ct = default)
    {
        var logs = await db.DailyLogs.AsNoTracking().OrderBy(l => l.Date).ToListAsync(ct);
        var snapshots = logs.Select(l => new DailyLogSnapshot(
            l.Date, l.BbtCelsius, l.CervicalMucus, l.LhTest, l.CrampType, l.CrampSeverity,
            l.FlowIntensity, l.PeriodStart, 0, 0)).ToList();

        var derived = CycleDeriver.Derive(snapshots);
        var existing = await db.Cycles.ToDictionaryAsync(c => c.StartDate, ct);
        var now = clock.GetUtcNow();

        foreach (var stale in existing.Values.Where(c => derived.All(d => d.Start != c.StartDate)))
            db.Cycles.Remove(stale);

        for (var i = 0; i < derived.Count; i++)
        {
            var d = derived[i];
            if (!existing.TryGetValue(d.Start, out var row))
            {
                // Új ciklus: az addig lezárt ciklusokból számolt predikció egyszer íródik be.
                var prior = derived.Take(i)
                    .Where(p => p.LengthDays is not null)
                    .Select(p => new ClosedCycleStat(
                        p.Start, p.LengthDays!.Value, p.LutealLength, p.Anovulatory, null))
                    .ToList();
                row = new Cycle { StartDate = d.Start, PredictedLengthDays = LengthPredictor.Predict(prior) };
                db.Cycles.Add(row);
            }
            row.LengthDays = d.LengthDays;
            row.OvulationDayConfirmed = d.OvulationConfirmed;
            row.OvulationDayEstimated = d.OvulationEstimated;
            row.LutealPhaseLength = d.LutealLength;
            row.Anovulatory = d.Anovulatory;
            row.ComputedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~CycleDeriverTests"`
Expected: 5 PASS. (A CycleRecomputeService-t a Task 15 integrációs tesztjei fedik.)

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Prediction/CycleDeriver.cs Mensi.Core/Services/CycleRecomputeService.cs Mensi.Tests/Prediction/CycleDeriverTests.cs
git commit -m "feat: ciklus-levezetés a logokból + hossz-predikció + recompute szolgáltatás"
```

---

### Task 13: AuditWriter + AuditRetentionService

**Files:**
- Create: `Mensi.Core/Services/AuditWriter.cs`, `Mensi.Core/Services/AuditRetentionService.cs`, `Mensi.Core/Options/AuditOptions.cs`
- Test: `Mensi.Tests/AuditWriterTests.cs`

**Interfaces:**
- Produces:

```csharp
public class AuditOptions
{
    public const string SectionName = "Audit";
    public int RetentionDays { get; set; } = 365; // 0 = örökre
}
public sealed class AuditWriter(Mensi.Core.Data.MensiDbContext db, TimeProvider clock)
{
    // Hozzáadja a sort a change trackerhez; a hívó SaveChanges-e írja ki (egy tranzakció az adattal).
    public void Add(string email, string action, DateOnly entryDate,
        IReadOnlyDictionary<string, (object? Old, object? New)> changes);
    public static string BuildChangesJson(IReadOnlyDictionary<string, (object? Old, object? New)> changes);
}
public sealed class AuditRetentionService : BackgroundService; // naponta töröl RetentionDays-nél régebbit
```

- [ ] **Step 1: Failing teszt a JSON-alakra**

`Mensi.Tests/AuditWriterTests.cs`:

```csharp
using Mensi.Core.Services;

namespace Mensi.Tests;

public class AuditWriterTests
{
    [Fact]
    public void Changes_json_is_camel_case_old_new_pairs()
    {
        var json = AuditWriter.BuildChangesJson(new Dictionary<string, (object?, object?)>
        {
            ["bbtCelsius"] = (36.40m, 36.42m),
            ["cervicalMucus"] = (null, Mensi.Core.Domain.CervicalMucus.EggWhite),
        });
        Assert.Equal(
            """{"bbtCelsius":{"old":36.40,"new":36.42},"cervicalMucus":{"old":null,"new":"eggWhite"}}""",
            json);
    }
}
```

- [ ] **Step 2: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~AuditWriterTests"`
Expected: FAIL.

- [ ] **Step 3: Implementáció**

`Mensi.Core/Options/AuditOptions.cs`:

```csharp
namespace Mensi.Core.Options;

public class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Napokban; 0 = örökre. Az audit sor emailt tartalmaz, ezért szabály is van rá,
    /// nem csak kézi törlés.</summary>
    public int RetentionDays { get; set; } = 365;
}
```

`Mensi.Core/Services/AuditWriter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Mensi.Core.Data;
using Mensi.Core.Domain;

namespace Mensi.Core.Services;

public sealed class AuditWriter(MensiDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public void Add(string email, string action, DateOnly entryDate,
        IReadOnlyDictionary<string, (object? Old, object? New)> changes) =>
        db.AuditEntries.Add(new AuditEntry
        {
            At = clock.GetUtcNow(),
            Email = email,
            Action = action,
            EntryDate = entryDate,
            ChangesJson = BuildChangesJson(changes),
        });

    public static string BuildChangesJson(
        IReadOnlyDictionary<string, (object? Old, object? New)> changes)
    {
        var shaped = changes.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<string, object?> { ["old"] = kv.Value.Old, ["new"] = kv.Value.New });
        return JsonSerializer.Serialize(shaped, Json);
    }
}
```

`Mensi.Core/Services/AuditRetentionService.cs`:

```csharp
using Mensi.Core.Data;
using Mensi.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mensi.Core.Services;

public sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditOptions> options,
    TimeProvider clock,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.RetentionDays <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24), clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MensiDbContext>();
                var cutoff = clock.GetUtcNow().AddDays(-options.Value.RetentionDays);
                var deleted = await db.AuditEntries.Where(a => a.At < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Audit retention: {Count} sor törölve", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Az audit retention futása elhasalt");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

- [ ] **Step 4: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~AuditWriterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Mensi.Core/Services/AuditWriter.cs Mensi.Core/Services/AuditRetentionService.cs Mensi.Core/Options/AuditOptions.cs Mensi.Tests/AuditWriterTests.cs
git commit -m "feat: adatváltozás-audit jsonb diff-fel és retention takarítással"
```

---

### Task 14: DTO-k + ReadModelBuilder — nézetenkénti válaszok (pure)

**Files:**
- Create: `Mensi.Core/Api/Dtos.cs`, `Mensi.Core/Api/ReadModelBuilder.cs`
- Test: `Mensi.Tests/ReadModelBuilderTests.cs`

**Interfaces:**
- Consumes: `PredictionEngine.Evaluate`, `CycleDeriver`, `BbtAnalyzer`, `WilcoxKernel`, `DayCategorizer` (Task 5–12 szignatúrái)
- Produces: a spec 5.1–5.2 JSON-alakjait tükröző rekordok + a négy builder:

```csharp
public sealed record ModelInput(
    IReadOnlyList<Mensi.Core.Domain.DailyLog> Logs,   // Intercourse betöltve, dátum szerint rendezve
    IReadOnlyList<Mensi.Core.Domain.Cycle> Cycles,    // start_date szerint rendezve
    DateOnly Today);

public static class ReadModelBuilder
{
    public static OverviewDto BuildOverview(ModelInput input);
    public static TrendsDto BuildTrends(ModelInput input);
    public static CalendarDto BuildCalendar(ModelInput input, int year, int month);
    public static ChanceDto BuildChance(ModelInput input);
    public static IReadOnlyList<DailyLogDto> MapRange(ModelInput input, DateOnly from, DateOnly to);
    public static DailyLogDto MapOne(ModelInput input, DateOnly date); // hiányzó napra üres DTO
}
```

- [ ] **Step 1: DTO rekordok**

`Mensi.Core/Api/Dtos.cs`:

```csharp
using Mensi.Core.Domain;

namespace Mensi.Core.Api;

public sealed record IntercourseDto(long Id, bool? Protected);

public sealed record DailyLogDto(
    DateOnly Date, decimal? BbtCelsius, bool BbtOutlier,
    CervicalMucus? CervicalMucus, LhTest? LhTest,
    CrampType? CrampType, short? CrampSeverity, FlowIntensity? FlowIntensity,
    bool PeriodStart, IReadOnlyList<Mood> Moods, IReadOnlyList<IntercourseDto> Intercourse,
    DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record WindowDto(DateOnly From, DateOnly To);
public sealed record CycleInfoDto(int Day, DateOnly StartDate);
public sealed record PhaseDto(DayCategory Key, string Label, int TotalDays, int ElapsedDays, int RemainingDays);
public sealed record StripDayDto(DateOnly Date, int? CycleDay, DayCategory Category, bool IsToday);
public sealed record StripDto(DateOnly From, DateOnly To, IReadOnlyList<StripDayDto> Days);
public sealed record TimingDayDto(DateOnly Date, int CycleDay, int IntercourseCount, bool IsOvulationWindow, bool IsFuture);
public sealed record TimingDto(TimingLabel Label, double ChancePercent, int DaysRemaining,
    int IntercourseTotal, IReadOnlyList<TimingDayDto> WindowDays);

public sealed record OverviewDto(
    DateOnly Today, bool IsEmpty, CycleInfoDto? Cycle, PhaseDto? Phase, string? Headline,
    WindowDto? OvulationWindow, WindowDto? NextPeriodWindow, ConfidenceLevel? Confidence,
    string? PregnancyHint, StripDto? Strip, TimingDto? Timing,
    DailyLogDto? TodayLog, DailyLogDto? YesterdayLog);

public sealed record TimingSummaryDto(TimingLabel Label, double ChancePercent);
public sealed record TrendsStatsDto(double AverageLength, int MinLength, int MaxLength,
    double StdDev, double? AverageLuteal, int LoggedPercent);
public sealed record TrendCycleDto(DateOnly StartDate, int LengthDays, int DeviationFromAverage,
    int? LutealLength, bool Anovulatory, TimingSummaryDto Timing);
public sealed record BbtMarksDto(CervicalMucus? CervicalMucus, LhTest? LhTest);
public sealed record BbtRowDto(DateOnly Date, int CycleDay, decimal? Value, decimal? DeltaFromCoverline,
    bool IsOutlier, bool AboveCoverline, BbtMarksDto Marks);
public sealed record TrendsBbtDto(decimal? Coverline, bool OvulationConfirmed,
    DateOnly? ConfirmedOvulationDate, int ExcludedOutlierCount, int MissingDayCount,
    IReadOnlyList<BbtRowDto> Rows);
public sealed record TrendsDto(TrendsStatsDto? Stats, IReadOnlyList<TrendCycleDto> Cycles, TrendsBbtDto? Bbt);

public sealed record MonthRangeDto(string FirstMonth, string LastMonth);
public sealed record CalendarDayDto(DateOnly Date, int? CycleDay, DayCategory Category,
    bool HasBbt, int IntercourseCount, bool HasAnyEntry, bool IsToday);
public sealed record CalendarDto(string Month, MonthRangeDto Range, int? CycleDayOfToday,
    bool HasData, IReadOnlyList<CalendarDayDto> Days);

public sealed record FertileDayDto(DateOnly Date, int CycleDay, int IntercourseCount, bool IsFuture, bool IsToday);
public sealed record FertileWindowDto(int DaysRemaining, int OvulationWindowTotal,
    int OvulationWindowElapsed, IReadOnlyList<FertileDayDto> Days);
public sealed record ChanceHistoryCycleDto(DateOnly StartDate, TimingSummaryDto Timing);
public sealed record ChanceHistoryDto(int GoodCount, int TotalCount, IReadOnlyList<ChanceHistoryCycleDto> Cycles);
public sealed record ChanceDto(bool IsEmpty, TimingSummaryDto? Timing, string? Explanation,
    string? ConfidenceNote, FertileWindowDto? FertileWindow, string? WhatIfHint, ChanceHistoryDto? History);
```

- [ ] **Step 2: Failing tesztek**

`Mensi.Tests/ReadModelBuilderTests.cs`:

```csharp
using Mensi.Core.Api;
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Tests;

public class ReadModelBuilderTests
{
    private static readonly DateOnly CurStart = new(2026, 8, 10);
    private static readonly DateOnly Today = new(2026, 8, 23); // ciklus 14. napja

    /// <summary>2 lezárt ciklus (28 nap, ovuláció ~14. nap, BBT-vel megerősítve) + nyitott
    /// ciklus 14 nappal: LH-csúcs a 13., nyúlós nyák a 11–14., együttlét a 12. napon.</summary>
    private static ModelInput Fixture()
    {
        var logs = new List<DailyLog>();
        void AddCycle(DateOnly start, int? length)
        {
            var days = length ?? (Today.DayNumber - start.DayNumber + 1);
            for (var d = 1; d <= days; d++)
            {
                var date = start.AddDays(d - 1);
                var log = new DailyLog
                {
                    Date = date,
                    PeriodStart = d == 1,
                    FlowIntensity = d <= 5 ? FlowIntensity.Medium : null,
                    BbtCelsius = length is null
                        ? 36.30m + (d % 3) * 0.03m                      // nyitott: még alacsony
                        : d <= 14 ? 36.30m + (d % 3) * 0.03m : 36.70m,  // lezárt: shift a 15. naptól
                    CervicalMucus = length is null && d >= 11 ? CervicalMucus.EggWhite : null,
                    LhTest = length is null && d == 13 ? LhTest.Peak : null,
                    UpdatedBy = "a@b.hu",
                };
                if (length is null && d == 12)
                    log.Intercourse.Add(new IntercourseEvent { Date = date, Protected = false });
                logs.Add(log);
            }
        }
        AddCycle(CurStart.AddDays(-56), 28);
        AddCycle(CurStart.AddDays(-28), 28);
        AddCycle(CurStart, null);

        // A cycle tábla tartalmát a deriver adja — a builder-teszt a teljes láncot fedi.
        var snapshots = logs.Select(l => new DailyLogSnapshot(
            l.Date, l.BbtCelsius, l.CervicalMucus, l.LhTest, l.CrampType, l.CrampSeverity,
            l.FlowIntensity, l.PeriodStart, l.Intercourse.Count,
            l.Intercourse.Count(i => i.Protected != true))).ToList();
        var cycles = CycleDeriver.Derive(snapshots).Select(d => new Cycle
        {
            StartDate = d.Start, LengthDays = d.LengthDays,
            OvulationDayConfirmed = d.OvulationConfirmed, OvulationDayEstimated = d.OvulationEstimated,
            LutealPhaseLength = d.LutealLength, Anovulatory = d.Anovulatory,
        }).ToList();

        return new ModelInput(logs, cycles, Today);
    }

    [Fact]
    public void Overview_has_all_view_state()
    {
        var o = ReadModelBuilder.BuildOverview(Fixture());
        Assert.False(o.IsEmpty);
        Assert.Equal(14, o.Cycle!.Day);
        Assert.NotNull(o.Headline);
        Assert.True(o.OvulationWindow!.From <= o.OvulationWindow.To);
        Assert.True(o.NextPeriodWindow!.From > o.OvulationWindow.To);
        Assert.Equal(35, o.Strip!.Days.Count);
        Assert.Equal(DayOfWeek.Monday, o.Strip.From.DayOfWeek);
        Assert.Contains(o.Strip.Days, d => d.IsToday);
        Assert.Contains(o.Timing!.WindowDays, d => d.IntercourseCount == 1);
        Assert.True(o.Timing.ChancePercent > 0);
        Assert.Equal(Today, o.TodayLog!.Date);
        Assert.Equal(Today.AddDays(-1), o.YesterdayLog!.Date);
    }

    [Fact]
    public void Overview_without_closed_cycles_is_empty_state()
    {
        var logs = new List<DailyLog>
        {
            new() { Date = Today.AddDays(-2), PeriodStart = true, FlowIntensity = FlowIntensity.Heavy },
        };
        var input = new ModelInput(logs,
            [new Cycle { StartDate = Today.AddDays(-2) }], Today);
        var o = ReadModelBuilder.BuildOverview(input);
        Assert.True(o.IsEmpty);
        Assert.Null(o.OvulationWindow);
        Assert.NotNull(o.TodayLog); // a sheet előtöltéséhez üresen is jár
    }

    [Fact]
    public void Trends_stats_history_and_bbt_rows()
    {
        var t = ReadModelBuilder.BuildTrends(Fixture());
        Assert.Equal(28, t.Stats!.AverageLength, 3);
        Assert.Equal(2, t.Cycles.Count);
        Assert.True(t.Cycles[0].StartDate > t.Cycles[1].StartDate); // legújabb elöl
        Assert.All(t.Cycles, c => Assert.Equal(14, c.LutealLength));
        Assert.InRange(t.Stats.LoggedPercent, 1, 100);
        Assert.Equal(14, t.Bbt!.Rows.Count);              // nyitott ciklus 14 napja
        Assert.False(t.Bbt.OvulationConfirmed);           // a nyitott ciklusban még nincs shift
        Assert.All(t.Bbt.Rows, r => Assert.Equal(r.Date.DayNumber - CurStart.DayNumber + 1, r.CycleDay));
    }

    [Fact]
    public void Calendar_categorizes_past_and_future()
    {
        var c = ReadModelBuilder.BuildCalendar(Fixture(), 2026, 8);
        Assert.Equal("2026-08", c.Month);
        Assert.True(c.HasData);
        Assert.Equal(14, c.CycleDayOfToday);
        Assert.Equal(31, c.Days.Count);
        Assert.Equal(DayCategory.Menstruation, c.Days.Single(d => d.Date == CurStart).Category);
        Assert.Contains(c.Days, d => d.Category is DayCategory.Ovulation or DayCategory.Fertile);
        Assert.Equal("2026-06", c.Range.FirstMonth);
        Assert.Equal("2026-09", c.Range.LastMonth);
    }

    [Fact]
    public void Chance_explains_and_lists_history()
    {
        var ch = ReadModelBuilder.BuildChance(Fixture());
        Assert.False(ch.IsEmpty);
        Assert.NotNull(ch.Timing);
        Assert.Contains("együttlét", ch.Explanation!);
        Assert.Equal(2, ch.History!.TotalCount);
        Assert.True(ch.FertileWindow!.Days.Count >= 6);
        Assert.Contains(ch.FertileWindow.Days, d => d.IntercourseCount == 1);
    }

    [Fact]
    public void Map_one_returns_empty_dto_for_missing_day()
    {
        var dto = ReadModelBuilder.MapOne(Fixture(), Today.AddDays(-100));
        Assert.Null(dto.BbtCelsius);
        Assert.Empty(dto.Intercourse);
        Assert.False(dto.PeriodStart);
    }
}
```

- [ ] **Step 3: Futtatás — bukjon**

Run: `dotnet test --filter "FullyQualifiedName~ReadModelBuilderTests"`
Expected: FAIL.

- [ ] **Step 4: Builder implementáció**

`Mensi.Core/Api/ReadModelBuilder.cs`:

```csharp
using Mensi.Core.Domain;
using Mensi.Core.Prediction;

namespace Mensi.Core.Api;

public sealed record ModelInput(
    IReadOnlyList<DailyLog> Logs,
    IReadOnlyList<Cycle> Cycles,
    DateOnly Today);

public static class ReadModelBuilder
{
    public const string ConfidenceNote =
        "A becslés a Wilcox-féle napi valószínűségeken és az ovuláció-posterioron alapul; "
        + "a sáv a lezárt ciklusok számával szűkül.";

    // ---- közös segédek -------------------------------------------------

    private static DailyLogSnapshot Snapshot(DailyLog l) => new(
        l.Date, l.BbtCelsius, l.CervicalMucus, l.LhTest, l.CrampType, l.CrampSeverity,
        l.FlowIntensity, l.PeriodStart, l.Intercourse.Count,
        l.Intercourse.Count(i => i.Protected != true));

    private static bool HasAnyEntry(DailyLog l) =>
        l.BbtCelsius is not null || l.CervicalMucus is not null || l.LhTest is not null
        || l.CrampSeverity is not null || l.FlowIntensity is not null || l.PeriodStart
        || l.Moods.Count > 0 || l.Intercourse.Count > 0;

    private static Cycle? CurrentCycle(ModelInput input) =>
        input.Cycles.LastOrDefault(c => c.StartDate <= input.Today);

    private static CyclePrediction? Predict(ModelInput input)
    {
        var current = CurrentCycle(input);
        if (current is null) return null;
        var closed = input.Cycles.Where(c => c.LengthDays is not null)
            .Select(c => new ClosedCycleStat(c.StartDate, c.LengthDays!.Value,
                c.LutealPhaseLength, c.Anovulatory, c.PredictedLengthDays))
            .ToList();
        var logs = input.Logs
            .Where(l => l.Date >= current.StartDate && l.Date <= input.Today)
            .Select(Snapshot).ToList();
        return PredictionEngine.Evaluate(
            new EngineInput(closed, current.StartDate, logs, input.Today));
    }

    /// <summary>Ciklusonkénti BBT-elemzés a kiugró-flageléshez: nap → outlier.</summary>
    private static Dictionary<DateOnly, bool> OutlierMap(ModelInput input)
    {
        var map = new Dictionary<DateOnly, bool>();
        foreach (var cycle in input.Cycles)
        {
            var end = cycle.LengthDays is int len
                ? cycle.StartDate.AddDays(len - 1)
                : input.Today;
            var cycleLogs = input.Logs
                .Where(l => l.Date >= cycle.StartDate && l.Date <= end).ToList();
            if (cycleLogs.Count == 0) continue;
            var lastDay = end.DayNumber - cycle.StartDate.DayNumber + 1;
            var byDay = cycleLogs.ToDictionary(
                l => l.Date.DayNumber - cycle.StartDate.DayNumber + 1);
            var analysis = BbtAnalyzer.Analyze(Enumerable.Range(1, lastDay)
                .Select(d => new BbtDay(d, byDay.TryGetValue(d, out var l) ? l.BbtCelsius : null))
                .ToList());
            foreach (var l in cycleLogs)
                map[l.Date] = analysis.OutlierDays.Contains(
                    l.Date.DayNumber - cycle.StartDate.DayNumber + 1);
        }
        return map;
    }

    private static DailyLogDto Map(DailyLog l, bool outlier) => new(
        l.Date, l.BbtCelsius, outlier, l.CervicalMucus, l.LhTest, l.CrampType,
        l.CrampSeverity, l.FlowIntensity, l.PeriodStart, l.Moods,
        l.Intercourse.OrderBy(i => i.Id).Select(i => new IntercourseDto(i.Id, i.Protected)).ToList(),
        l.UpdatedAt == default ? null : l.UpdatedAt, l.UpdatedBy == "" ? null : l.UpdatedBy);

    private static DailyLogDto Empty(DateOnly date) =>
        new(date, null, false, null, null, null, null, null, false, [], [], null, null);

    public static IReadOnlyList<DailyLogDto> MapRange(ModelInput input, DateOnly from, DateOnly to)
    {
        var outliers = OutlierMap(input);
        return input.Logs.Where(l => l.Date >= from && l.Date <= to)
            .Select(l => Map(l, outliers.GetValueOrDefault(l.Date))).ToList();
    }

    public static DailyLogDto MapOne(ModelInput input, DateOnly date)
    {
        var log = input.Logs.FirstOrDefault(l => l.Date == date);
        return log is null ? Empty(date) : Map(log, OutlierMap(input).GetValueOrDefault(date));
    }

    private static DayCategory Categorize(ModelInput input, CyclePrediction? prediction, DateOnly date)
    {
        var firstLog = input.Logs.Count > 0 ? input.Logs[0].Date : (DateOnly?)null;
        if (firstLog is null || date < firstLog) return DayCategory.PreCycle;

        var cycle = input.Cycles.LastOrDefault(c => c.StartDate <= date);
        if (cycle is null) return DayCategory.PreCycle;

        if (cycle.LengthDays is int len)
        {
            var flowDays = input.Logs
                .Where(l => l.FlowIntensity >= FlowIntensity.Light
                            && l.Date >= cycle.StartDate && l.Date < cycle.StartDate.AddDays(len))
                .Select(l => l.Date).ToHashSet();
            return DayCategorizer.CategorizeClosed(date, cycle.StartDate, len,
                cycle.OvulationDayConfirmed ?? cycle.OvulationDayEstimated, flowDays);
        }
        return prediction?.Categorize(date) ?? DayCategory.Unknown;
    }

    private static int? CycleDayOf(ModelInput input, DateOnly date)
    {
        var cycle = input.Cycles.LastOrDefault(c => c.StartDate <= date);
        if (cycle is null) return null;
        var day = date.DayNumber - cycle.StartDate.DayNumber + 1;
        return cycle.LengthDays is int len && day > len ? null : day;
    }

    private static double Percent(double chance) => Math.Round(chance * 100, 1);

    // ---- overview -------------------------------------------------------

    public static OverviewDto BuildOverview(ModelInput input)
    {
        var outliers = OutlierMap(input);
        DailyLogDto MapDay(DateOnly d)
        {
            var log = input.Logs.FirstOrDefault(l => l.Date == d);
            return log is null ? Empty(d) : Map(log, outliers.GetValueOrDefault(d));
        }
        var todayLog = MapDay(input.Today);
        var yesterdayLog = MapDay(input.Today.AddDays(-1));

        var prediction = Predict(input);
        if (prediction is null)
            return new OverviewDto(input.Today, true, null, null, null, null, null, null,
                null, null, null, todayLog, yesterdayLog);

        var monday = input.Today.AddDays(-(((int)input.Today.DayOfWeek + 6) % 7));
        var stripFrom = monday.AddDays(-14);
        var stripDays = Enumerable.Range(0, 35).Select(i =>
        {
            var date = stripFrom.AddDays(i);
            return new StripDayDto(date, CycleDayOf(input, date),
                Categorize(input, prediction, date), date == input.Today);
        }).ToList();

        var countByDate = input.Logs.ToDictionary(l => l.Date, l => l.Intercourse.Count);
        var windowDays = new List<TimingDayDto>();
        for (var date = prediction.FertileFrom; date <= prediction.FertileTo; date = date.AddDays(1))
            windowDays.Add(new TimingDayDto(date,
                date.DayNumber - prediction.CycleStart.DayNumber + 1,
                countByDate.GetValueOrDefault(date),
                date >= prediction.OvulationFrom, date > input.Today));

        var timing = new TimingDto(prediction.Timing, Percent(prediction.Chance),
            Math.Max(prediction.FertileTo.DayNumber - input.Today.DayNumber, 0),
            windowDays.Sum(d => d.IntercourseCount), windowDays);

        return new OverviewDto(input.Today, false,
            new CycleInfoDto(prediction.CycleDay, prediction.CycleStart),
            new PhaseDto(prediction.Phase.Key, prediction.Phase.Label,
                prediction.Phase.TotalDays, prediction.Phase.ElapsedDays, prediction.Phase.RemainingDays),
            prediction.Headline,
            new WindowDto(prediction.OvulationFrom, prediction.OvulationTo),
            new WindowDto(prediction.PeriodFrom, prediction.PeriodTo),
            prediction.Confidence, prediction.PregnancyHint,
            new StripDto(stripFrom, stripFrom.AddDays(34), stripDays),
            timing, todayLog, yesterdayLog);
    }

    // ---- trends ---------------------------------------------------------

    private static TimingSummaryDto ClosedTiming(ModelInput input, Cycle cycle)
    {
        var len = cycle.LengthDays!.Value;
        var days = input.Logs
            .Where(l => l.Date >= cycle.StartDate && l.Date < cycle.StartDate.AddDays(len)
                        && l.Intercourse.Any(i => i.Protected != true))
            .Select(l => l.Date.DayNumber - cycle.StartDate.DayNumber + 1).ToList();
        var ovu = cycle.OvulationDayConfirmed ?? cycle.OvulationDayEstimated ?? Math.Max(len - 14, 1);
        var chance = WilcoxKernel.RetroChance(ovu, days);
        return new TimingSummaryDto(WilcoxKernel.Label(chance), Percent(chance));
    }

    public static TrendsDto BuildTrends(ModelInput input)
    {
        var closed = input.Cycles.Where(c => c.LengthDays is not null).ToList();
        TrendsStatsDto? stats = null;
        if (closed.Count > 0)
        {
            var s = CycleStats.Compute(closed.Select(c => new ClosedCycleStat(
                c.StartDate, c.LengthDays!.Value, c.LutealPhaseLength, c.Anovulatory,
                c.PredictedLengthDays)).ToList())!;
            var window = closed.TakeLast(6).ToList();
            var totalDays = window.Sum(c => c.LengthDays!.Value);
            var loggedDays = window.Sum(c => input.Logs.Count(l =>
                l.Date >= c.StartDate && l.Date < c.StartDate.AddDays(c.LengthDays!.Value)
                && HasAnyEntry(l)));
            stats = new TrendsStatsDto(
                Math.Round(s.MeanLength, 1), s.MinLength, s.MaxLength,
                Math.Round(s.StdDevLength, 1),
                s.MeanLuteal is null ? null : Math.Round(s.MeanLuteal.Value, 1),
                totalDays == 0 ? 0 : (int)Math.Round(100.0 * loggedDays / totalDays));
        }

        var cycles = closed
            .OrderByDescending(c => c.StartDate)
            .Select(c => new TrendCycleDto(c.StartDate, c.LengthDays!.Value,
                stats is null ? 0 : (int)Math.Round(c.LengthDays.Value - stats.AverageLength),
                c.LutealPhaseLength, c.Anovulatory, ClosedTiming(input, c)))
            .ToList();

        TrendsBbtDto? bbt = null;
        var prediction = Predict(input);
        var current = CurrentCycle(input);
        if (prediction is not null && current is not null)
        {
            var a = prediction.Bbt;
            var byDay = input.Logs
                .Where(l => l.Date >= current.StartDate && l.Date <= input.Today)
                .ToDictionary(l => l.Date.DayNumber - current.StartDate.DayNumber + 1);
            var rows = Enumerable.Range(1, prediction.CycleDay).Select(d =>
            {
                byDay.TryGetValue(d, out var log);
                var value = log?.BbtCelsius;
                return new BbtRowDto(current.StartDate.AddDays(d - 1), d, value,
                    value is not null && a.Coverline is not null ? value - a.Coverline : null,
                    a.OutlierDays.Contains(d), a.AboveCoverlineDays.Contains(d),
                    new BbtMarksDto(log?.CervicalMucus, log?.LhTest));
            }).ToList();
            bbt = new TrendsBbtDto(a.Coverline, a.ConfirmedOvulationDay is not null,
                a.ConfirmedOvulationDay is int o ? current.StartDate.AddDays(o - 1) : null,
                a.OutlierDays.Count, a.MissingDays.Count, rows);
        }

        return new TrendsDto(stats, cycles, bbt);
    }

    // ---- calendar ---------------------------------------------------------

    public static CalendarDto BuildCalendar(ModelInput input, int year, int month)
    {
        var prediction = Predict(input);
        var first = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var logByDate = input.Logs.ToDictionary(l => l.Date);

        var days = Enumerable.Range(0, daysInMonth).Select(i =>
        {
            var date = first.AddDays(i);
            logByDate.TryGetValue(date, out var log);
            return new CalendarDayDto(date, CycleDayOf(input, date),
                Categorize(input, prediction, date),
                log?.BbtCelsius is not null, log?.Intercourse.Count ?? 0,
                log is not null && HasAnyEntry(log), date == input.Today);
        }).ToList();

        var firstMonth = input.Logs.Count > 0 ? input.Logs[0].Date : input.Today;
        var lastMonth = input.Today.AddMonths(1);
        return new CalendarDto(
            $"{year:D4}-{month:D2}",
            new MonthRangeDto($"{firstMonth.Year:D4}-{firstMonth.Month:D2}",
                $"{lastMonth.Year:D4}-{lastMonth.Month:D2}"),
            input.Today.Year == year && input.Today.Month == month ? CycleDayOf(input, input.Today) : null,
            days.Any(d => d.HasAnyEntry), days);
    }

    // ---- chance -----------------------------------------------------------

    public static ChanceDto BuildChance(ModelInput input)
    {
        var prediction = Predict(input);
        if (prediction is null)
            return new ChanceDto(true, null, null, null, null, null, null);

        var fertileLogs = input.Logs
            .Where(l => l.Date >= prediction.FertileFrom && l.Date <= prediction.FertileTo)
            .ToList();
        var unprotectedDays = fertileLogs
            .Where(l => l.Intercourse.Any(i => i.Protected != true))
            .Select(l => l.Date).OrderBy(d => d).ToList();
        var eventCount = fertileLogs.Sum(l => l.Intercourse.Count(i => i.Protected != true));

        string explanation;
        if (eventCount == 0)
        {
            explanation = "Ebben a ciklusban még nincs együttlét a termékeny ablakban.";
        }
        else
        {
            var rels = unprotectedDays
                .Select(d => prediction.OvulationP50.DayNumber - d.DayNumber)
                .Distinct().OrderByDescending(r => r).ToList();
            explanation = rels.All(r => r > 0)
                ? $"{eventCount} együttlét esik a termékeny ablakba, {unprotectedDays.Count} külön napon "
                  + $"— a becsült ovuláció előtt {string.Join(" és ", rels)} nappal."
                : $"{eventCount} együttlét esik a termékeny ablakba, {unprotectedDays.Count} külön napon.";
        }

        var countByDate = input.Logs.ToDictionary(l => l.Date, l => l.Intercourse.Count);
        var days = new List<FertileDayDto>();
        for (var date = prediction.FertileFrom; date <= prediction.FertileTo; date = date.AddDays(1))
            days.Add(new FertileDayDto(date,
                date.DayNumber - prediction.CycleStart.DayNumber + 1,
                countByDate.GetValueOrDefault(date), date > input.Today, date == input.Today));

        var ovuTotal = prediction.OvulationTo.DayNumber - prediction.OvulationFrom.DayNumber + 1;
        var ovuElapsed = Math.Clamp(
            input.Today.DayNumber - prediction.OvulationFrom.DayNumber + 1, 0, ovuTotal);

        var closed = input.Cycles.Where(c => c.LengthDays is not null)
            .OrderByDescending(c => c.StartDate)
            .Select(c => new ChanceHistoryCycleDto(c.StartDate, ClosedTiming(input, c)))
            .ToList();

        return new ChanceDto(false,
            new TimingSummaryDto(prediction.Timing, Percent(prediction.Chance)),
            explanation, ConfidenceNote,
            new FertileWindowDto(
                Math.Max(prediction.FertileTo.DayNumber - input.Today.DayNumber, 0),
                ovuTotal, ovuElapsed, days),
            prediction.WhatIfHint,
            new ChanceHistoryDto(closed.Count(c => c.Timing.Label == TimingLabel.Good),
                closed.Count, closed));
    }
}
```

- [ ] **Step 5: Futtatás — menjen át**

Run: `dotnet test --filter "FullyQualifiedName~ReadModelBuilderTests"`
Expected: 6 PASS. Tipikus buktató: a fixture lezárt ciklusaiban a BBT-shiftnek meg kell erősödnie (15. naptól 36.70), különben a luteális 14 assert bukik — ellenőrizd a BbtAnalyzer bemenetét (28 napos rács).

- [ ] **Step 6: Commit**

```bash
git add Mensi.Core/Api Mensi.Tests/ReadModelBuilderTests.cs
git commit -m "feat: nézetenkénti read model (overview, trends, calendar, chance)"
```

---

### Task 15: API endpointok + teljes Program.cs + integrációs tesztek

**Files:**
- Create: `Mensi.Core/Options/DisplayOptions.cs`, `Mensi.Core/Services/TodayProvider.cs`, `Mensi.Core/Services/CurrentUser.cs`, `Mensi.Server/Api/Requests.cs`, `Mensi.Server/Api/ApiEndpoints.cs`, `Mensi.Server/appsettings.json`
- Modify: `Mensi.Server/Program.cs` (teljes csere)
- Test: `Mensi.Tests/ApiTests.cs`

**Interfaces:**
- Consumes: Task 2 (middleware, `AccessIdentity`), Task 3 (`MensiDbContext`), Task 4 (`Patch<T>`), Task 12 (`CycleRecomputeService`), Task 13 (`AuditWriter`), Task 14 (`ReadModelBuilder`, DTO-k)
- Produces: a spec 5.2 szerinti HTTP felület; `TodayProvider.Today : DateOnly`; `CurrentUser.Email : string`

- [ ] **Step 1: Options + segéd-szolgáltatások**

`Mensi.Core/Options/DisplayOptions.cs`:

```csharp
namespace Mensi.Core.Options;

public class DisplayOptions
{
    public const string SectionName = "Display";

    /// <summary>A "ma" e szerint az időzóna szerint értendő (a dátumok naptári napok).</summary>
    public string TimeZone { get; set; } = "Europe/Budapest";
}
```

`Mensi.Core/Services/TodayProvider.cs`:

```csharp
using Mensi.Core.Options;
using Microsoft.Extensions.Options;

namespace Mensi.Core.Services;

public sealed class TodayProvider(TimeProvider clock, IOptions<DisplayOptions> options)
{
    public DateOnly Today
    {
        get
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        }
    }
}
```

`Mensi.Core/Services/CurrentUser.cs`:

```csharp
using Mensi.Core.Options;
using Microsoft.Extensions.Options;

namespace Mensi.Core.Services;

/// <summary>Az audit sorok szerzője. Élesben mindig az Access-claimből jön; fejlesztésben,
/// kikapcsolt Access mellett fix fallback, hogy az audit ott se legyen üres.</summary>
public sealed class CurrentUser(
    IHttpContextAccessor accessor,
    IOptions<CloudflareAccessOptions> access,
    IHostEnvironment environment)
{
    public string Email
    {
        get
        {
            var email = AccessIdentity.Of(accessor.HttpContext);
            if (email != AccessIdentity.Unknown) return email;
            return !access.Value.IsConfigured && environment.IsDevelopment()
                ? AccessIdentity.DevFallback
                : AccessIdentity.Unknown;
        }
    }
}
```

- [ ] **Step 2: Request típusok**

`Mensi.Server/Api/Requests.cs`:

```csharp
using Mensi.Core.Api;
using Mensi.Core.Domain;

namespace Mensi.Server.Api;

/// <summary>Mezőnkénti részleges upsert: jelen lévő kulcs null-lal = törlés, hiányzó = érintetlen.</summary>
public sealed record UpdateLogRequest
{
    public Patch<decimal?> BbtCelsius { get; init; } = new();
    public Patch<CervicalMucus?> CervicalMucus { get; init; } = new();
    public Patch<LhTest?> LhTest { get; init; } = new();
    public Patch<CrampType?> CrampType { get; init; } = new();
    public Patch<short?> CrampSeverity { get; init; } = new();
    public Patch<FlowIntensity?> FlowIntensity { get; init; } = new();
    public Patch<bool> PeriodStart { get; init; } = new();
    public Patch<List<Mood>?> Moods { get; init; } = new();
}

public sealed record IntercourseEventRequest(bool? Protected);
public sealed record SetIntercourseRequest(List<IntercourseEventRequest> Events);
```

- [ ] **Step 3: Endpointok**

`Mensi.Server/Api/ApiEndpoints.cs`:

```csharp
using Mensi.Core.Api;
using Mensi.Core.Data;
using Mensi.Core.Domain;
using Mensi.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Server.Api;

public static class ApiEndpoints
{
    public static void MapMensiApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/overview", async (MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.BuildOverview(await LoadAsync(db, today))));

        api.MapGet("/trends", async (MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.BuildTrends(await LoadAsync(db, today))));

        api.MapGet("/calendar", async (int year, int month, MensiDbContext db, TodayProvider today) =>
            year is < 2000 or > 2100 || month is < 1 or > 12
                ? Problem("Érvénytelen év vagy hónap.")
                : Results.Ok(ReadModelBuilder.BuildCalendar(await LoadAsync(db, today), year, month)));

        api.MapGet("/chance", async (MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.BuildChance(await LoadAsync(db, today))));

        api.MapGet("/logs", async (DateOnly from, DateOnly to, MensiDbContext db, TodayProvider today) =>
        {
            if (to < from) return Problem("A tartomány vége nem lehet a kezdete előtt.");
            if (to.DayNumber - from.DayNumber > 400) return Problem("Legfeljebb 400 napos tartomány kérhető.");
            var input = await LoadAsync(db, today);
            return Results.Ok(new { Days = ReadModelBuilder.MapRange(input, from, to) });
        });

        api.MapGet("/logs/{date}", async (DateOnly date, MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.MapOne(await LoadAsync(db, today), date)));

        api.MapPut("/logs/{date}", UpsertLogAsync);
        api.MapPut("/logs/{date}/intercourse", SetIntercourseAsync);
    }

    private static async Task<ModelInput> LoadAsync(MensiDbContext db, TodayProvider today)
    {
        var logs = await db.DailyLogs.AsNoTracking().Include(l => l.Intercourse)
            .OrderBy(l => l.Date).ToListAsync();
        var cycles = await db.Cycles.AsNoTracking().OrderBy(c => c.StartDate).ToListAsync();
        return new ModelInput(logs, cycles, today.Today);
    }

    private static IResult Problem(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static async Task<IResult> UpsertLogAsync(
        DateOnly date, UpdateLogRequest request, MensiDbContext db, TodayProvider todayProvider,
        CurrentUser user, AuditWriter audit, CycleRecomputeService recompute, TimeProvider clock)
    {
        var today = todayProvider.Today;
        if (date > today) return Problem("Jövőbeli napra nem rögzíthető bejegyzés.");
        if (request.BbtCelsius is { IsSet: true, Value: not null and (< 35.00m or > 38.99m) })
            return Problem("A testhő 35,00 és 38,99 °C között adható meg.");
        if (request.CrampSeverity is { IsSet: true, Value: not null and (< 0 or > 3) })
            return Problem("A görcs erőssége 0 és 3 között adható meg.");
        if (request.Moods is { IsSet: true, Value: not null } moods
            && moods.Value!.Any(m => !Enum.IsDefined(m)))
            return Problem("Ismeretlen hangulat-érték.");
        foreach (var invalid in new[]
        {
            request.CervicalMucus is { IsSet: true, Value: not null } cm && !Enum.IsDefined(cm.Value!.Value),
            request.LhTest is { IsSet: true, Value: not null } lh && !Enum.IsDefined(lh.Value!.Value),
            request.CrampType is { IsSet: true, Value: not null } ct && !Enum.IsDefined(ct.Value!.Value),
            request.FlowIntensity is { IsSet: true, Value: not null } fi && !Enum.IsDefined(fi.Value!.Value),
        })
            if (invalid) return Problem("Ismeretlen enum-érték.");

        var log = await db.DailyLogs.Include(l => l.Intercourse)
            .SingleOrDefaultAsync(l => l.Date == date);
        var created = log is null;
        log ??= new DailyLog { Date = date, CreatedAt = clock.GetUtcNow() };

        var changes = new Dictionary<string, (object? Old, object? New)>();
        void Apply<T>(Patch<T> patch, string name, Func<DailyLog, T?> get, Action<DailyLog, T?> set)
        {
            if (!patch.IsSet) return;
            var old = get(log);
            if (Equals(old, patch.Value)) return;
            changes[name] = (old, patch.Value);
            set(log, patch.Value);
        }

        Apply(request.BbtCelsius, "bbtCelsius", l => l.BbtCelsius, (l, v) => l.BbtCelsius = v);
        Apply(request.CervicalMucus, "cervicalMucus", l => l.CervicalMucus, (l, v) => l.CervicalMucus = v);
        Apply(request.LhTest, "lhTest", l => l.LhTest, (l, v) => l.LhTest = v);
        Apply(request.CrampType, "crampType", l => l.CrampType, (l, v) => l.CrampType = v);
        Apply(request.CrampSeverity, "crampSeverity", l => l.CrampSeverity, (l, v) => l.CrampSeverity = v);
        Apply(request.FlowIntensity, "flowIntensity", l => l.FlowIntensity, (l, v) => l.FlowIntensity = v);
        if (request.PeriodStart.IsSet && log.PeriodStart != request.PeriodStart.Value)
        {
            changes["periodStart"] = (log.PeriodStart, request.PeriodStart.Value);
            log.PeriodStart = request.PeriodStart.Value;
        }
        if (request.Moods.IsSet)
        {
            var next = request.Moods.Value ?? [];
            if (!log.Moods.SequenceEqual(next))
            {
                changes["moods"] = (log.Moods.ToList(), next);
                log.Moods = next;
            }
        }

        // Konzisztencia: "nincs görcs" mellett nincs hely; hely csak erősséggel együtt értelmes.
        if (log.CrampSeverity is 0 && log.CrampType is not null)
        {
            changes["crampType"] = (log.CrampType, null);
            log.CrampType = null;
        }

        if (created && changes.Count == 0) return Results.Ok(ReadModelBuilder.MapOne(
            await LoadAsyncFor(db, date, todayProvider), date));

        if (changes.Count > 0)
        {
            log.UpdatedAt = clock.GetUtcNow();
            log.UpdatedBy = user.Email;
            if (created) db.DailyLogs.Add(log);
            audit.Add(user.Email, "log.upsert", date, changes);
            await db.SaveChangesAsync();
            await recompute.RecomputeAsync();
        }

        return Results.Ok(ReadModelBuilder.MapOne(await LoadAsyncFor(db, date, todayProvider), date));
    }

    private static async Task<IResult> SetIntercourseAsync(
        DateOnly date, SetIntercourseRequest request, MensiDbContext db, TodayProvider todayProvider,
        CurrentUser user, AuditWriter audit, CycleRecomputeService recompute, TimeProvider clock)
    {
        var today = todayProvider.Today;
        if (date > today) return Problem("Jövőbeli napra nem rögzíthető bejegyzés.");
        if (request.Events.Count > 6) return Problem("Naponta legfeljebb 6 esemény rögzíthető.");

        var log = await db.DailyLogs.Include(l => l.Intercourse)
            .SingleOrDefaultAsync(l => l.Date == date);
        if (log is null)
        {
            log = new DailyLog { Date = date, CreatedAt = clock.GetUtcNow() };
            db.DailyLogs.Add(log);
        }

        var old = log.Intercourse.Select(i => i.Protected).ToList();
        log.Intercourse.Clear();
        foreach (var ev in request.Events)
            log.Intercourse.Add(new IntercourseEvent
            {
                Date = date, Protected = ev.Protected, CreatedAt = clock.GetUtcNow(),
            });

        log.UpdatedAt = clock.GetUtcNow();
        log.UpdatedBy = user.Email;
        audit.Add(user.Email, "intercourse.set", date, new Dictionary<string, (object?, object?)>
        {
            ["intercourse"] = (old, request.Events.Select(e => e.Protected).ToList()),
        });
        await db.SaveChangesAsync();
        await recompute.RecomputeAsync();

        return Results.Ok(ReadModelBuilder.MapOne(await LoadAsyncFor(db, date, todayProvider), date));
    }

    private static async Task<ModelInput> LoadAsyncFor(
        MensiDbContext db, DateOnly around, TodayProvider today)
    {
        // A MapOne outlier-flagjéhez a nap ciklusának környezete kell — a teljes betöltés
        // egyszerű és ezen az adatméreten (évi ~365 sor) olcsó.
        var logs = await db.DailyLogs.AsNoTracking().Include(l => l.Intercourse)
            .OrderBy(l => l.Date).ToListAsync();
        var cycles = await db.Cycles.AsNoTracking().OrderBy(c => c.StartDate).ToListAsync();
        return new ModelInput(logs, cycles, today.Today);
    }
}
```

- [ ] **Step 4: Teljes Program.cs (csere)**

`Mensi.Server/Program.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Mensi.Core.Api;
using Mensi.Core.Data;
using Mensi.Core.Middleware;
using Mensi.Core.Options;
using Mensi.Core.Services;
using Mensi.Server.Api;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddDbContext<MensiDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=mensi;Username=mensi;Password=mensi"));

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.SerializerOptions.Converters.Add(new PatchJsonConverterFactory());
    });

    builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection(DisplayOptions.SectionName));
    builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TodayProvider>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<CurrentUser>();
    builder.Services.AddScoped<AuditWriter>();
    builder.Services.AddScoped<CycleRecomputeService>();
    builder.Services.AddHostedService<AuditRetentionService>();

    // Cloudflare Access: élesben kötelező — verifikáció nélkül a host nem indulhat el.
    var accessOptions = builder.Configuration.GetSection(CloudflareAccessOptions.SectionName)
        .Get<CloudflareAccessOptions>() ?? new CloudflareAccessOptions();
    if (!accessOptions.IsConfigured && !builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "CloudflareAccess:TeamDomain és CloudflareAccess:Audience kötelező Development-en kívül "
            + "(CF_ACCESS_* env változók, ld. .env.example).");
    builder.Services.Configure<CloudflareAccessOptions>(
        builder.Configuration.GetSection(CloudflareAccessOptions.SectionName));
    builder.Services.AddHttpClient(CloudflareAccessKeyStore.HttpClientName);
    builder.Services.AddSingleton<CloudflareAccessKeyStore>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // A /health az Access előtt: a konténer belülről, assertion nélkül ellenőrzi magát.
    app.MapGet("/health", () => Results.Text("OK"));

    if (accessOptions.IsConfigured)
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/health"),
            gated => gated.UseMiddleware<CloudflareAccessMiddleware>());
    else
        app.Logger.LogWarning("Cloudflare Access ellenőrzés KIKAPCSOLVA (nincs konfigurálva) — csak Development!");

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapMensiApi();

    // Ismeretlen /api útvonal 404, nem az SPA index — a hiányzó endpoint ne adjon 200 HTML-t.
    app.MapFallback("/api/{**rest}", () => Results.NotFound());
    if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
        app.MapFallbackToFile("index.html");

    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<MensiDbContext>().Database.MigrateAsync();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "A host indítása elhasalt");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
```

`Mensi.Server/appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    }
  },
  "Display": { "TimeZone": "Europe/Budapest" },
  "Audit": { "RetentionDays": 365 },
  "AllowedHosts": "*"
}
```

- [ ] **Step 5: Failing integrációs tesztek**

`Mensi.Tests/ApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Tests;

public sealed class MensiApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // Access kikapcsolva, dev fallback identitás
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }
}

[Collection("postgres")]
public class ApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private MensiApiFactory _factory = null!;
    private HttpClient _client = null!;
    private static readonly DateOnly Today =
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest")).DateTime);

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _factory = new MensiApiFactory(fixture.ConnectionString);
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    private async Task<HttpResponseMessage> PutLog(DateOnly date, object body) =>
        await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}", body);

    [Fact]
    public async Task Health_is_reachable_without_assertion()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Partial_upsert_touches_only_sent_fields_and_audits()
    {
        var date = Today.AddDays(-1);
        Assert.Equal(HttpStatusCode.OK, (await PutLog(date, new { bbtCelsius = 36.42 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutLog(date, new { cervicalMucus = "eggWhite" })).StatusCode);

        var log = await _client.GetFromJsonAsync<JsonElement>($"/api/logs/{date:yyyy-MM-dd}");
        Assert.Equal(36.42m, log.GetProperty("bbtCelsius").GetDecimal());
        Assert.Equal("eggWhite", log.GetProperty("cervicalMucus").GetString());

        await using var db = fixture.CreateContext();
        var audits = await db.AuditEntries.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("dev@localhost", a.Email));
        Assert.Contains("bbtCelsius", audits[0].ChangesJson);
    }

    [Fact]
    public async Task Null_clears_a_field()
    {
        var date = Today.AddDays(-1);
        await PutLog(date, new { bbtCelsius = 36.42 });
        await PutLog(date, new { bbtCelsius = (decimal?)null });
        var log = await _client.GetFromJsonAsync<JsonElement>($"/api/logs/{date:yyyy-MM-dd}");
        Assert.Equal(JsonValueKind.Null, log.GetProperty("bbtCelsius").ValueKind);
    }

    [Fact]
    public async Task Cramp_severity_zero_clears_type()
    {
        var date = Today.AddDays(-1);
        await PutLog(date, new { crampType = "abdomen", crampSeverity = 2 });
        await PutLog(date, new { crampSeverity = 0 });
        var log = await _client.GetFromJsonAsync<JsonElement>($"/api/logs/{date:yyyy-MM-dd}");
        Assert.Equal(JsonValueKind.Null, log.GetProperty("crampType").ValueKind);
    }

    [Fact]
    public async Task Period_start_builds_cycles_and_writes_prediction_once()
    {
        var first = Today.AddDays(-56);
        await PutLog(first, new { periodStart = true, flowIntensity = "medium" });
        await PutLog(first.AddDays(28), new { periodStart = true, flowIntensity = "medium" });

        await using var db = fixture.CreateContext();
        var cycles = await db.Cycles.OrderBy(c => c.StartDate).ToListAsync();
        Assert.Equal(2, cycles.Count);
        Assert.Equal(28, cycles[0].LengthDays);
        Assert.Null(cycles[0].PredictedLengthDays);   // az elsőnek nincs előzménye
        Assert.Equal(28, cycles[1].PredictedLengthDays); // shrink(28,16,n=1,x̄=28) = 28
        Assert.Null(cycles[1].LengthDays);
    }

    [Theory]
    [InlineData("""{"bbtCelsius": 34.2}""")]
    [InlineData("""{"crampSeverity": 5}""")]
    [InlineData("""{"cervicalMucus": "vaporous"}""")]
    public async Task Invalid_values_are_rejected(string body)
    {
        var date = Today.AddDays(-1);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PutAsync($"/api/logs/{date:yyyy-MM-dd}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Future_date_is_rejected()
    {
        var response = await PutLog(Today.AddDays(2), new { bbtCelsius = 36.42 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Intercourse_put_replaces_the_day_and_caps_at_six()
    {
        var date = Today.AddDays(-1);
        var two = await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}/intercourse",
            new { events = new[] { new { @protected = (bool?)false }, new { @protected = (bool?)true } } });
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);

        var one = await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}/intercourse",
            new { events = new[] { new { @protected = (bool?)null } } });
        var log = await one.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, log.GetProperty("intercourse").GetArrayLength());

        var seven = await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}/intercourse",
            new { events = Enumerable.Repeat(new { @protected = (bool?)false }, 7).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, seven.StatusCode);
    }

    [Fact]
    public async Task Overview_goes_from_empty_to_predicted()
    {
        var empty = await _client.GetFromJsonAsync<JsonElement>("/api/overview");
        Assert.True(empty.GetProperty("isEmpty").GetBoolean());

        var first = Today.AddDays(-56);
        await PutLog(first, new { periodStart = true, flowIntensity = "medium" });
        await PutLog(first.AddDays(28), new { periodStart = true, flowIntensity = "medium" });
        await PutLog(first.AddDays(56), new { periodStart = true, flowIntensity = "medium" });

        var full = await _client.GetFromJsonAsync<JsonElement>("/api/overview");
        Assert.False(full.GetProperty("isEmpty").GetBoolean());
        Assert.Equal(35, full.GetProperty("strip").GetProperty("days").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, full.GetProperty("ovulationWindow").ValueKind);

        var trends = await _client.GetFromJsonAsync<JsonElement>("/api/trends");
        Assert.Equal(2, trends.GetProperty("cycles").GetArrayLength());

        var calendar = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/calendar?year={Today.Year}&month={Today.Month}");
        Assert.True(calendar.GetProperty("hasData").GetBoolean());

        var chance = await _client.GetFromJsonAsync<JsonElement>("/api/chance");
        Assert.False(chance.GetProperty("isEmpty").GetBoolean());
    }

    [Fact]
    public async Task Unknown_api_path_is_404_not_spa()
    {
        var response = await _client.GetAsync("/api/nincs-ilyen");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 6: Futtatás — bukjon, majd wiring-javítás után menjen át**

Run: `dotnet test --filter "FullyQualifiedName~ApiTests"`
Expected: első futásra tipikusan a JSON-opciók vagy a DI hiánya miatt FAIL; a Step 1–4 fájlokkal PASS. Ellenőrizd: a `Period_start_builds_cycles` predikció-asszertje 28 (egy 28-as ciklusnál a shrinkage pontosan 28-at ad, mert a minta és a populációs átlag egybeesik).

- [ ] **Step 7: Teljes tesztfuttatás + commit**

Run: `dotnet test`
Expected: minden zöld.

```bash
git add -A
git commit -m "feat: REST API mezőnkénti upserttel, audittal és nézet-endpointokkal"
```

---

### Task 16: Nuxt scaffold — projekt, tokenek, layout, API-réteg, formázók

**Files:**
- Create: `mensi.client/package.json`, `mensi.client/nuxt.config.ts`, `mensi.client/tsconfig.json`, `mensi.client/app/app.vue`, `mensi.client/app/assets/css/main.css`, `mensi.client/app/layouts/default.vue`, `mensi.client/app/types/api.ts`, `mensi.client/app/utils/format.ts`, `mensi.client/app/utils/labels.ts`, `mensi.client/app/composables/useApi.ts`, `mensi.client/app/stores/app.ts`, `mensi.client/vitest.config.ts`, `mensi.client/tests/format.test.ts`

**Interfaces:**
- Produces: `useApi()` (typed fetch), `useAppStore()` (overview + mentés + undo + sheet/toast állapot), formázók (`formatTemp`, `formatDateShort`, `formatDateLong`, `formatRange`, `formatDelta`, `monthTitle`), címke-térképek (`MUCUS_LABELS`, `LH_LABELS`, `CRAMP_TYPE_LABELS`, `CRAMP_SEVERITY_LABELS`, `FLOW_LABELS`, `MOOD_LABELS`, `MOOD_EMOJI`, `TIMING_LABELS`, `CONFIDENCE_LABELS`, `STRIP_COLORS`, `CAL_COLORS`, `FIELD_ORDER`), CSS tokenek/utility-osztályok (`.card`, `.chip`, `.btn`, `.btn-primary`, `.btn-ghost`)
- A vizuális részletek forrása: `docs/design/mensi-care-prototipus-kicsomagolt.html`

- [ ] **Step 1: Projektfájlok**

`mensi.client/package.json`:

```json
{
  "name": "mensi.client",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "nuxt dev",
    "generate": "nuxt generate",
    "type-check": "nuxt prepare && vue-tsc --noEmit",
    "test": "vitest run"
  },
  "dependencies": {
    "@fontsource/montserrat": "^5.2.8",
    "@pinia/nuxt": "^0.11.2",
    "nuxt": "^4.2.0",
    "pinia": "^3.0.4",
    "vue": "^3.5.38"
  },
  "devDependencies": {
    "typescript": "^5.9.0",
    "vitest": "^3.2.0",
    "vue-tsc": "^3.1.0"
  }
}
```

`mensi.client/nuxt.config.ts`:

```typescript
export default defineNuxtConfig({
  ssr: false,
  modules: ['@pinia/nuxt'],
  css: [
    '@fontsource/montserrat/400.css',
    '@fontsource/montserrat/500.css',
    '@fontsource/montserrat/600.css',
    '@fontsource/montserrat/700.css',
    '~/assets/css/main.css',
  ],
  app: {
    head: {
      title: 'Mensi',
      htmlAttrs: { lang: 'hu' },
      meta: [{ name: 'viewport', content: 'width=device-width, initial-scale=1' }],
    },
  },
  nitro: {
    devProxy: { '/api': { target: 'http://localhost:5080/api', changeOrigin: true } },
  },
  typescript: { strict: true },
  compatibilityDate: '2026-08-24',
})
```

`mensi.client/tsconfig.json`:

```json
{
  "extends": "./.nuxt/tsconfig.json"
}
```

`mensi.client/vitest.config.ts`:

```typescript
import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  resolve: {
    alias: { '~': fileURLToPath(new URL('./app', import.meta.url)) },
  },
  test: { include: ['tests/**/*.test.ts'] },
})
```

`mensi.client/app/app.vue`:

```vue
<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
</template>
```

- [ ] **Step 2: Design tokenek és utility-osztályok**

`mensi.client/app/assets/css/main.css` (a prototípus konstansaiból):

```css
:root {
  --primary: #5a5cd6;
  --primary-hover: #4a4cbd;
  --primary-deep: #3a3c9e;
  --primary-ink: #2c2d63;
  --tint: #eef1ff;
  --bg: #f6f7ff;
  --surface: #f5f7fe;
  --ink: #21243d;
  --ink-2: #464b6b;
  --ink-3: #545a7a;
  --ink-4: #626884;
  --muted: #8f96b5;
  --plum: #6f71d6;
  --plum-ink: #3a3c9e;
  --lavender: #b1b2ff;
  --light-blue: #c6d6ff;
  --line: #eef1ff;
  --shadow-card: 0 1px 2px rgba(33, 36, 61, .05), 0 6px 20px rgba(33, 36, 61, .04);
  --radius-card: 18px;
}

* { margin: 0; padding: 0; box-sizing: border-box; }

body {
  font-family: 'Montserrat', system-ui, sans-serif;
  background: var(--bg);
  color: var(--ink);
  -webkit-font-smoothing: antialiased;
}

.card {
  background: #ffffff;
  border-radius: var(--radius-card);
  padding: 18px;
  box-shadow: var(--shadow-card);
}

.chip {
  display: inline-block;
  border-radius: 99px;
  padding: 5px 11px;
  font-size: 11px;
  font-weight: 600;
}

.btn {
  display: block;
  width: 100%;
  border: 0;
  border-radius: 99px;
  font: 700 13.5px 'Montserrat', sans-serif;
  padding: 17px 0;
  cursor: pointer;
  text-align: center;
}

.btn-primary { background: var(--primary); color: #ffffff; }
.btn-primary:hover { background: var(--primary-hover); }
.btn-ghost { background: var(--bg); color: var(--ink-2); font-weight: 600; }
.btn-ghost:hover { background: var(--tint); }

.section-title { font-size: 13px; font-weight: 700; }
.muted { color: var(--ink-3); }

.noscroll { scrollbar-width: none; }
.noscroll::-webkit-scrollbar { display: none; }
```

- [ ] **Step 3: API típusok**

`mensi.client/app/types/api.ts` (a Task 14 DTO-inak tükre):

```typescript
export type CervicalMucus = 'dry' | 'sticky' | 'creamy' | 'eggWhite'
export type LhTest = 'negative' | 'positive' | 'peak'
export type CrampType = 'abdomen' | 'back' | 'breast'
export type FlowIntensity = 'none' | 'spotting' | 'light' | 'medium' | 'heavy'
export type Mood = 'cheerful' | 'calm' | 'irritable' | 'tired' | 'sad' | 'anxious' | 'longing'
export type TimingLabel = 'weak' | 'medium' | 'good'
export type ConfidenceLevel = 'low' | 'medium' | 'high'
export type DayCategory =
  | 'preCycle' | 'menstruation' | 'follicular' | 'fertile'
  | 'ovulation' | 'luteal' | 'predictedPeriod' | 'unknown'

export interface IntercourseEvent { id: number; protected: boolean | null }

export interface DailyLog {
  date: string
  bbtCelsius: number | null
  bbtOutlier: boolean
  cervicalMucus: CervicalMucus | null
  lhTest: LhTest | null
  crampType: CrampType | null
  crampSeverity: number | null
  flowIntensity: FlowIntensity | null
  periodStart: boolean
  moods: Mood[]
  intercourse: IntercourseEvent[]
  updatedAt: string | null
  updatedBy: string | null
}

export interface LogPatch {
  bbtCelsius?: number | null
  cervicalMucus?: CervicalMucus | null
  lhTest?: LhTest | null
  crampType?: CrampType | null
  crampSeverity?: number | null
  flowIntensity?: FlowIntensity | null
  periodStart?: boolean
  moods?: Mood[] | null
}

export interface DateWindow { from: string; to: string }
export interface Phase { key: DayCategory; label: string; totalDays: number; elapsedDays: number; remainingDays: number }
export interface StripDay { date: string; cycleDay: number | null; category: DayCategory; isToday: boolean }
export interface TimingDay { date: string; cycleDay: number; intercourseCount: number; isOvulationWindow: boolean; isFuture: boolean }
export interface Timing {
  label: TimingLabel; chancePercent: number; daysRemaining: number
  intercourseTotal: number; windowDays: TimingDay[]
}

export interface Overview {
  today: string
  isEmpty: boolean
  cycle: { day: number; startDate: string } | null
  phase: Phase | null
  headline: string | null
  ovulationWindow: DateWindow | null
  nextPeriodWindow: DateWindow | null
  confidence: ConfidenceLevel | null
  pregnancyHint: string | null
  strip: { from: string; to: string; days: StripDay[] } | null
  timing: Timing | null
  todayLog: DailyLog | null
  yesterdayLog: DailyLog | null
}

export interface TimingSummary { label: TimingLabel; chancePercent: number }
export interface TrendCycle {
  startDate: string; lengthDays: number; deviationFromAverage: number
  lutealLength: number | null; anovulatory: boolean; timing: TimingSummary
}
export interface BbtRow {
  date: string; cycleDay: number; value: number | null; deltaFromCoverline: number | null
  isOutlier: boolean; aboveCoverline: boolean
  marks: { cervicalMucus: CervicalMucus | null; lhTest: LhTest | null }
}
export interface Trends {
  stats: {
    averageLength: number; minLength: number; maxLength: number
    stdDev: number; averageLuteal: number | null; loggedPercent: number
  } | null
  cycles: TrendCycle[]
  bbt: {
    coverline: number | null; ovulationConfirmed: boolean; confirmedOvulationDate: string | null
    excludedOutlierCount: number; missingDayCount: number; rows: BbtRow[]
  } | null
}

export interface CalendarDay {
  date: string; cycleDay: number | null; category: DayCategory
  hasBbt: boolean; intercourseCount: number; hasAnyEntry: boolean; isToday: boolean
}
export interface CalendarMonth {
  month: string
  range: { firstMonth: string; lastMonth: string }
  cycleDayOfToday: number | null
  hasData: boolean
  days: CalendarDay[]
}

export interface FertileDay { date: string; cycleDay: number; intercourseCount: number; isFuture: boolean; isToday: boolean }
export interface Chance {
  isEmpty: boolean
  timing: TimingSummary | null
  explanation: string | null
  confidenceNote: string | null
  fertileWindow: {
    daysRemaining: number; ovulationWindowTotal: number; ovulationWindowElapsed: number
    days: FertileDay[]
  } | null
  whatIfHint: string | null
  history: { goodCount: number; totalCount: number; cycles: { startDate: string; timing: TimingSummary }[] } | null
}
```

- [ ] **Step 4: Failing formázó-tesztek**

`mensi.client/tests/format.test.ts`:

```typescript
import { describe, expect, it } from 'vitest'
import { formatDateLong, formatDateShort, formatDelta, formatRange, formatTemp, monthTitle } from '~/utils/format'

describe('format', () => {
  it('temp uses comma and °C', () => {
    expect(formatTemp(36.4)).toBe('36,40 °C')
    expect(formatTemp(null)).toBeNull()
  })
  it('short date is hungarian abbreviation', () => {
    expect(formatDateShort('2026-08-23')).toBe('aug. 23.')
    expect(formatDateShort('2026-03-05')).toBe('márc. 5.')
  })
  it('long date includes weekday', () => {
    expect(formatDateLong('2026-08-23')).toBe('aug. 23., vasárnap')
  })
  it('range collapses within a month and spells across months', () => {
    expect(formatRange('2026-08-23', '2026-08-27')).toBe('aug. 23–27.')
    expect(formatRange('2026-08-30', '2026-09-03')).toBe('aug. 30. – szept. 3.')
  })
  it('delta is signed with comma', () => {
    expect(formatDelta(0.21)).toBe('+0,21')
    expect(formatDelta(-0.06)).toBe('−0,06')
  })
  it('month title', () => {
    expect(monthTitle('2026-08')).toBe('2026. augusztus')
  })
})
```

- [ ] **Step 5: Futtatás — bukjon**

Run: `cd mensi.client && npm install && npm run test`
Expected: FAIL (a `~/utils/format` nem létezik).

- [ ] **Step 6: Formázók és címkék**

`mensi.client/app/utils/format.ts`:

```typescript
const MONTHS = ['jan.', 'febr.', 'márc.', 'ápr.', 'máj.', 'jún.', 'júl.', 'aug.', 'szept.', 'okt.', 'nov.', 'dec.']
const MONTHS_FULL = ['január', 'február', 'március', 'április', 'május', 'június', 'július',
  'augusztus', 'szeptember', 'október', 'november', 'december']
const WEEKDAYS = ['vasárnap', 'hétfő', 'kedd', 'szerda', 'csütörtök', 'péntek', 'szombat']

const comma = (n: number, digits: number) => n.toFixed(digits).replace('.', ',')

export function formatTemp(value: number | null): string | null {
  return value === null ? null : `${comma(value, 2)} °C`
}

export function formatDateShort(iso: string): string {
  const d = new Date(`${iso}T00:00:00`)
  return `${MONTHS[d.getMonth()]} ${d.getDate()}.`
}

export function formatDateLong(iso: string): string {
  const d = new Date(`${iso}T00:00:00`)
  return `${MONTHS[d.getMonth()]} ${d.getDate()}., ${WEEKDAYS[d.getDay()]}`
}

export function formatRange(fromIso: string, toIso: string): string {
  const from = new Date(`${fromIso}T00:00:00`)
  const to = new Date(`${toIso}T00:00:00`)
  if (from.getMonth() === to.getMonth())
    return `${MONTHS[from.getMonth()]} ${from.getDate()}–${to.getDate()}.`
  return `${formatDateShort(fromIso)} – ${formatDateShort(toIso)}`
}

export function formatDelta(value: number): string {
  const sign = value >= 0 ? '+' : '−'
  return `${sign}${comma(Math.abs(value), 2)}`
}

export function monthTitle(yearMonth: string): string {
  const [year, month] = yearMonth.split('-').map(Number)
  return `${year}. ${MONTHS_FULL[(month ?? 1) - 1]}`
}

export function formatPercent(value: number): string {
  return `${comma(value, value < 10 ? 1 : 0)}%`
}

export function addDays(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00`)
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}
```

`mensi.client/app/utils/labels.ts`:

```typescript
import type { CervicalMucus, ConfidenceLevel, CrampType, DayCategory, FlowIntensity, LhTest, Mood, TimingLabel } from '~/types/api'

export const MUCUS_LABELS: Record<CervicalMucus, string> =
  { dry: 'Száraz', sticky: 'Ragadós', creamy: 'Nedves', eggWhite: 'Nyúlós' }
export const MUCUS_ORDER: CervicalMucus[] = ['dry', 'sticky', 'creamy', 'eggWhite']

export const LH_LABELS: Record<LhTest, string> = { negative: 'Negatív', positive: 'Pozitív', peak: 'Csúcs' }
export const LH_ORDER: LhTest[] = ['negative', 'positive', 'peak']
export const LH_NOTES: Record<LhTest, string> = {
  negative: 'halvány vagy nincs csík', positive: 'a tesztcsík látható', peak: 'a legsötétebb eddig',
}

export const CRAMP_TYPE_LABELS: Record<CrampType, string> = { abdomen: 'Alhas', back: 'Derék', breast: 'Mell' }
export const CRAMP_TYPE_ORDER: CrampType[] = ['abdomen', 'back', 'breast']
export const CRAMP_SEVERITY_LABELS = ['Nincs', 'Enyhe', 'Közepes', 'Erős'] as const

export const FLOW_LABELS: Record<FlowIntensity, string> =
  { none: 'Nincs', spotting: 'Pecsételő', light: 'Enyhe', medium: 'Közepes', heavy: 'Erős' }
export const FLOW_ORDER: FlowIntensity[] = ['none', 'spotting', 'light', 'medium', 'heavy']

export const MOOD_LABELS: Record<Mood, string> = {
  cheerful: 'Vidám', calm: 'Nyugodt', irritable: 'Ingerlékeny', tired: 'Fáradt',
  sad: 'Szomorú', anxious: 'Szorongó', longing: 'Vágyakozó',
}
export const MOOD_EMOJI: Record<Mood, string> = {
  cheerful: '😊', calm: '😌', irritable: '😠', tired: '😴', sad: '😢', anxious: '😟', longing: '😍',
}
export const MOOD_ORDER: Mood[] = ['cheerful', 'calm', 'irritable', 'tired', 'sad', 'anxious', 'longing']

export const TIMING_LABELS: Record<TimingLabel, string> = { weak: 'Gyenge', medium: 'Közepes', good: 'Jó' }
export const CONFIDENCE_LABELS: Record<ConfidenceLevel, string> = { low: 'alacsony', medium: 'közepes', high: 'magas' }

/** Az 5 hetes Ma-sáv cellaszínei (prototípus: maPanel.cycleDays). */
export const STRIP_COLORS: Record<DayCategory, { bg: string; fg: string }> = {
  preCycle: { bg: '#f7f8fd', fg: '#9aa0bd' },
  menstruation: { bg: '#6f71d6', fg: '#ffffff' },
  follicular: { bg: '#f0f2fb', fg: '#6a7095' },
  fertile: { bg: '#c6d6ff', fg: '#26365f' },
  ovulation: { bg: '#5a5cd6', fg: '#ffffff' },
  luteal: { bg: '#e8eaf6', fg: '#4a4f75' },
  predictedPeriod: { bg: '#dcdef4', fg: '#4a4f75' },
  unknown: { bg: '#f0f2fb', fg: '#6a7095' },
}

/** A havi naptár cellaszínei (prototípus: calCells). */
export const CAL_COLORS: Record<DayCategory, { bg: string; fg: string }> = {
  preCycle: { bg: '#f7f8fd', fg: '#545a7a' },
  menstruation: { bg: '#8386e6', fg: '#20214d' },
  follicular: { bg: '#f5f7fe', fg: '#464b6b' },
  fertile: { bg: '#cfdcff', fg: '#26365f' },
  ovulation: { bg: '#b1b2ff', fg: '#2c2d63' },
  luteal: { bg: '#eaecf4', fg: '#464b6b' },
  predictedPeriod: { bg: '#dcdef4', fg: '#4a4f75' },
  unknown: { bg: '#f7f8fd', fg: '#545a7a' },
}

export const CATEGORY_LEGEND: { key: DayCategory; label: string }[] = [
  { key: 'menstruation', label: 'Menstruáció' },
  { key: 'fertile', label: 'Termékeny' },
  { key: 'ovulation', label: 'Ovuláció' },
  { key: 'luteal', label: 'Luteális' },
  { key: 'predictedPeriod', label: 'Becsült mens' },
]

/** A napló mezősorrendje — a sheet lépései és a listák közös vázát adja. */
export const FIELD_ORDER = ['bbt', 'mucus', 'lh', 'cramp', 'flow', 'intercourse', 'mood'] as const
export type FieldKey = (typeof FIELD_ORDER)[number]
export const FIELD_LABELS: Record<FieldKey, string> = {
  bbt: 'Testhő', mucus: 'Nyák', lh: 'LH-teszt', cramp: 'Görcs',
  flow: 'Folyás', intercourse: 'Együttlét', mood: 'Hangulat',
}
```

- [ ] **Step 7: Futtatás — menjen át**

Run: `cd mensi.client && npm run test`
Expected: 6 PASS.

- [ ] **Step 8: API composable + store + layout**

`mensi.client/app/composables/useApi.ts`:

```typescript
import type { CalendarMonth, Chance, DailyLog, IntercourseEvent, LogPatch, Overview, Trends } from '~/types/api'

export function useApi() {
  return {
    overview: () => $fetch<Overview>('/api/overview'),
    trends: () => $fetch<Trends>('/api/trends'),
    calendar: (year: number, month: number) =>
      $fetch<CalendarMonth>('/api/calendar', { query: { year, month } }),
    chance: () => $fetch<Chance>('/api/chance'),
    logs: (from: string, to: string) =>
      $fetch<{ days: DailyLog[] }>('/api/logs', { query: { from, to } }),
    log: (date: string) => $fetch<DailyLog>(`/api/logs/${date}`),
    saveLog: (date: string, patch: LogPatch) =>
      $fetch<DailyLog>(`/api/logs/${date}`, { method: 'PUT', body: patch }),
    saveIntercourse: (date: string, events: { protected: boolean | null }[]) =>
      $fetch<DailyLog>(`/api/logs/${date}/intercourse`, { method: 'PUT', body: { events } }),
  }
}

export function eventsOf(log: DailyLog | null): { protected: boolean | null }[] {
  return (log?.intercourse ?? []).map((e: IntercourseEvent) => ({ protected: e.protected }))
}
```

`mensi.client/app/stores/app.ts`:

```typescript
import { defineStore } from 'pinia'
import type { DailyLog, LogPatch, Overview } from '~/types/api'

interface UndoPayload {
  date: string
  patch: LogPatch | null
  events: { protected: boolean | null }[] | null
}

export const useAppStore = defineStore('app', {
  state: () => ({
    overview: null as Overview | null,
    loading: false,
    sheetOpen: false,
    sheetDate: null as string | null,
    sheetStep: 0,
    sheetSingle: false,
    toastVisible: false,
    undoPayload: null as UndoPayload | null,
    toastTimer: null as ReturnType<typeof setTimeout> | null,
    refreshTick: 0, // a nézetek erre figyelnek: mentés után újratöltenek
  }),
  actions: {
    async loadOverview() {
      this.loading = true
      try { this.overview = await useApi().overview() }
      finally { this.loading = false }
    },
    openSheet(date: string, step = 0, single = false) {
      this.sheetDate = date
      this.sheetStep = step
      this.sheetSingle = single
      this.sheetOpen = true
    },
    closeSheet() { this.sheetOpen = false },

    /** Mentés + undo-payload építés: a patch kulcsaihoz a mentés ELŐTTI értékek. */
    async saveLog(date: string, patch: LogPatch, before: DailyLog | null) {
      const inverse: LogPatch = {}
      for (const key of Object.keys(patch) as (keyof LogPatch)[]) {
        // @ts-expect-error kulcsonként azonos típus a két oldalon
        inverse[key] = before ? (before[key === 'moods' ? 'moods' : key] ?? null) : null
      }
      if ('periodStart' in patch) inverse.periodStart = before?.periodStart ?? false
      const saved = await useApi().saveLog(date, patch)
      this.showToast({ date, patch: inverse, events: null })
      this.refresh()
      return saved
    },

    async saveIntercourse(date: string, events: { protected: boolean | null }[], before: DailyLog | null) {
      const saved = await useApi().saveIntercourse(date, events)
      this.showToast({ date, patch: null, events: eventsOf(before) })
      this.refresh()
      return saved
    },

    async undo() {
      const payload = this.undoPayload
      if (!payload) return
      this.hideToast()
      if (payload.patch) await useApi().saveLog(payload.date, payload.patch)
      if (payload.events) await useApi().saveIntercourse(payload.date, payload.events)
      this.refresh()
    },

    showToast(payload: UndoPayload) {
      if (this.toastTimer) clearTimeout(this.toastTimer)
      this.undoPayload = payload
      this.toastVisible = true
      this.toastTimer = setTimeout(() => this.hideToast(), 3400)
    },
    hideToast() {
      this.toastVisible = false
      this.undoPayload = null
      if (this.toastTimer) clearTimeout(this.toastTimer)
    },
    refresh() {
      this.refreshTick++
      void this.loadOverview()
    },
  },
})
```

`mensi.client/app/layouts/default.vue`:

```vue
<script setup lang="ts">
const route = useRoute()
const store = useAppStore()
onMounted(() => { void store.loadOverview() })

const NAV = [
  { to: '/', label: 'Ma', icon: 'M12 3.2a8.8 8.8 0 1 1 0 17.6 8.8 8.8 0 0 1 0-17.6Z', icon2: 'M12 7.4V12l3.2 1.9' },
  { to: '/trendek', label: 'Trendek', icon: 'M4 19V6M9 19v-6M14 19V9M19 19v-9', icon2: 'M4 19h16' },
  { to: '/bejegyzesek', label: 'Bejegyzések', icon: 'M6 3.8h9.5L19 7.3V20a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V4.8a1 1 0 0 1 1-1Z', icon2: 'M8.5 12h7M8.5 15.5h7M8.5 8.5h3.5' },
  { to: '/esely', label: 'Esély', icon: 'M12 20s-6.5-4.3-6.5-9A3.9 3.9 0 0 1 12 8.4 3.9 3.9 0 0 1 18.5 11c0 4.7-6.5 9-6.5 9Z', icon2: 'M12 8.4V20' },
]
const TITLES: Record<string, string> = {
  '/': 'Mensi', '/trendek': 'Trendek', '/bejegyzesek': 'Bejegyzések', '/esely': 'Fogamzási esély',
}
const headerTitle = computed(() => TITLES[route.path] ?? 'Mensi')
const headerRight = computed(() => {
  const o = store.overview
  if (!o) return ''
  return route.path === '/' ? formatDateLong(o.today) : o.cycle ? `ciklus ${o.cycle.day}. nap` : ''
})
</script>

<template>
  <div class="shell">
    <aside class="side">
      <div class="brand">
        <div class="brand-badge">M</div>
        <span class="brand-name">Mensi</span>
      </div>
      <nav class="side-nav">
        <NuxtLink v-for="item in NAV" :key="item.to" :to="item.to" class="side-item"
          :class="{ active: route.path === item.to }">
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor"
            stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path :d="item.icon" /><path :d="item.icon2" />
          </svg>
          <span>{{ item.label }}</span>
        </NuxtLink>
      </nav>
    </aside>

    <div class="main">
      <header class="topbar">
        <NuxtLink v-if="route.path === '/esely'" to="/" class="back" aria-label="Vissza">←</NuxtLink>
        <span class="topbar-title">{{ headerTitle }}</span>
        <span class="topbar-right">{{ headerRight }}</span>
      </header>

      <main class="content">
        <slot />
      </main>

      <nav class="tabbar">
        <NuxtLink v-for="item in NAV.slice(0, 3)" :key="item.to" :to="item.to" class="tab-item"
          :class="{ active: route.path === item.to }">
          <svg viewBox="0 0 24 24" width="23" height="23" fill="none" stroke="currentColor"
            stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path :d="item.icon" /><path :d="item.icon2" />
          </svg>
          <span>{{ item.label }}</span>
        </NuxtLink>
      </nav>
    </div>

    <SaveToast />
    <LogSheet />
  </div>
</template>

<style scoped>
.shell { display: flex; min-height: 100vh; }
.side { display: none; }
.main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.topbar {
  background: #fff; padding: 14px 16px; display: flex; align-items: center; gap: 11px;
  position: sticky; top: 0; z-index: 5; box-shadow: 0 1px 0 rgba(33, 36, 61, .06);
}
.back {
  width: 30px; height: 30px; border-radius: 10px; background: var(--tint);
  display: grid; place-items: center; font-size: 14px; font-weight: 700;
  color: var(--primary); text-decoration: none;
}
.topbar-title { font-weight: 700; font-size: 16px; letter-spacing: -.01em; }
.topbar-right { margin-left: auto; font-size: 12px; font-weight: 600; color: var(--ink-3); }
.content { flex: 1; padding: 14px 16px 90px; display: flex; flex-direction: column; gap: 14px; max-width: 720px; width: 100%; margin: 0 auto; }
.tabbar {
  background: #fff; position: sticky; bottom: 0; z-index: 5; display: flex;
  box-shadow: 0 -1px 0 rgba(33, 36, 61, .06); padding: 8px 8px 16px;
}
.tab-item {
  flex: 1; padding: 9px 0 7px; border-radius: 16px; display: flex; flex-direction: column;
  align-items: center; gap: 4px; text-decoration: none; color: var(--ink-3);
  font-size: 11px; font-weight: 500;
}
.tab-item.active { color: var(--primary); background: var(--tint); font-weight: 700; }

@media (min-width: 1000px) {
  .side {
    display: flex; flex-direction: column; width: 224px; flex-shrink: 0; background: #fff;
    position: sticky; top: 0; height: 100vh; box-shadow: 1px 0 0 rgba(33, 36, 61, .06);
  }
  .brand { padding: 22px 20px 18px; display: flex; align-items: center; gap: 11px; }
  .brand-badge {
    width: 30px; height: 30px; background: var(--primary); border-radius: 10px;
    display: grid; place-items: center; color: #fff; font-weight: 700; font-size: 14px;
  }
  .brand-name { font-weight: 700; font-size: 17px; letter-spacing: -.01em; }
  .side-nav { padding: 6px 12px; display: flex; flex-direction: column; gap: 2px; }
  .side-item {
    padding: 11px 14px; border-radius: 12px; display: flex; align-items: center; gap: 11px;
    text-decoration: none; color: var(--ink-2); font-size: 13.5px; font-weight: 500;
  }
  .side-item:hover { background: var(--tint); }
  .side-item.active { background: var(--tint); color: var(--primary); font-weight: 700; }
  .tabbar { display: none; }
  .content { padding-bottom: 24px; }
}
</style>
```

(A `SaveToast` és `LogSheet` komponens a Task 18-ban készül — addig hozz létre két üres placeholder komponenst `mensi.client/app/components/SaveToast.vue` és `mensi.client/app/components/LogSheet.vue` néven, `<template><div hidden /></template>` tartalommal, hogy a type-check zöld legyen.)

- [ ] **Step 9: Type-check + commit**

Run: `cd mensi.client && npm run type-check && npm run test`
Expected: zöld.

```bash
git add mensi.client
git commit -m "feat: Nuxt scaffold tokenekkel, layouttal, API-réteggel és formázókkal"
```

---

### Task 17: Ma nézet (hero, 5 hetes sáv, időzítés, tegnap, mai bejegyzés, empty state)

**Files:**
- Create: `mensi.client/app/pages/index.vue`, `mensi.client/app/components/ma/HeroCard.vue`, `mensi.client/app/components/ma/TimingCard.vue`, `mensi.client/app/components/ma/YesterdayCard.vue`, `mensi.client/app/components/ma/TodayCard.vue`, `mensi.client/app/components/ma/EmptyState.vue`, `mensi.client/app/utils/fieldValue.ts`

**Interfaces:**
- Consumes: `useAppStore()` (overview, openSheet), formázók + címkék (Task 16)
- Produces: `fieldValue(log, key): string | null` — a mező kijelzett értéke (a naptár-panel és a sheet-összegzés is ezt használja)
- Vizuális referencia: prototípus `isMa` blokk + `maPanel` render-logika

- [ ] **Step 1: fieldValue segéd**

`mensi.client/app/utils/fieldValue.ts`:

```typescript
import type { DailyLog } from '~/types/api'
import { CRAMP_SEVERITY_LABELS, CRAMP_TYPE_LABELS, FLOW_LABELS, LH_LABELS, MOOD_EMOJI, MOOD_LABELS, MUCUS_LABELS, type FieldKey } from '~/utils/labels'
import { formatTemp } from '~/utils/format'

/** A napló egy mezőjének kijelzett értéke; null = nincs rögzítve.
 *  Megjegyzés: az együttlét "explicit 0" állapotot a séma nem tárolja —
 *  üres eseménylista = nincs rögzítve. */
export function fieldValue(log: DailyLog | null, key: FieldKey): string | null {
  if (!log) return null
  switch (key) {
    case 'bbt':
      return formatTemp(log.bbtCelsius)
    case 'mucus':
      return log.cervicalMucus ? MUCUS_LABELS[log.cervicalMucus] : null
    case 'lh':
      return log.lhTest ? LH_LABELS[log.lhTest] : null
    case 'cramp':
      if (log.crampSeverity === null) return null
      if (log.crampSeverity === 0) return 'Nincs'
      return `${log.crampType ? CRAMP_TYPE_LABELS[log.crampType] + ' · ' : ''}${CRAMP_SEVERITY_LABELS[log.crampSeverity]}`
    case 'flow': {
      if (log.flowIntensity === null) return log.periodStart ? 'Ciklus 1. napja' : null
      const base = FLOW_LABELS[log.flowIntensity]
      return log.periodStart ? `${base} · ciklus 1. napja` : base
    }
    case 'intercourse': {
      if (log.intercourse.length === 0) return null
      const prot = log.intercourse.filter(e => e.protected === true).length
      return `${log.intercourse.length}×${prot > 0 ? ` · ${prot} védekezéssel` : ''}`
    }
    case 'mood':
      return log.moods.length
        ? log.moods.map(m => `${MOOD_EMOJI[m]} ${MOOD_LABELS[m]}`).join(', ')
        : null
  }
}
```

- [ ] **Step 2: Ma oldal + kártyák**

`mensi.client/app/pages/index.vue`:

```vue
<script setup lang="ts">
const store = useAppStore()
const overview = computed(() => store.overview)
</script>

<template>
  <div v-if="overview" class="stack">
    <MaEmptyState v-if="overview.isEmpty" />
    <template v-else>
      <div v-if="overview.pregnancyHint" class="card hint-card">{{ overview.pregnancyHint }}</div>
      <MaHeroCard :overview="overview" />
      <MaTimingCard :overview="overview" />
      <MaYesterdayCard :overview="overview" />
      <MaTodayCard :overview="overview" />
    </template>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.hint-card { background: var(--tint); color: var(--primary-ink); font-size: 13px; line-height: 1.55; }
</style>
```

`mensi.client/app/components/ma/EmptyState.vue`:

```vue
<script setup lang="ts">
const store = useAppStore()
const STEPS = [
  'Rögzítsd a testhőt minden reggel, felkelés előtt — ez adja a görbe gerincét.',
  'Jelöld a menstruáció első napját; innen indul a ciklusszámítás.',
  'A nyák és az LH-teszt gyorsítja az ovuláció felismerését, de nem kötelező.',
]
</script>

<template>
  <div class="stack">
    <div class="intro">
      <div class="intro-tag">Nincs még adat</div>
      <div class="intro-title">Előrejelzéshez legalább egy teljes ciklus kell.</div>
      <div class="intro-body">Amíg nincs elég mérés, az app nem mutat becsült ovulációt és
        menstruációt — inkább semmit, mint pontatlant.</div>
    </div>
    <div v-for="(txt, i) in STEPS" :key="i" class="card step">
      <div class="step-n">{{ i + 1 }}</div>
      <div class="step-txt">{{ txt }}</div>
    </div>
    <button class="btn btn-primary" @click="store.openSheet(store.overview!.today)">
      Első bejegyzés rögzítése
    </button>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 12px; }
.intro { background: var(--tint); border-radius: 20px; padding: 22px 18px; }
.intro-tag { font-size: 12.5px; font-weight: 600; color: var(--primary); }
.intro-title { font-size: 22px; font-weight: 700; margin-top: 8px; letter-spacing: -.02em; line-height: 1.25; }
.intro-body { font-size: 13.5px; color: var(--primary-ink); line-height: 1.6; margin-top: 9px; }
.step { display: flex; align-items: center; gap: 13px; padding: 16px; border-radius: 16px; }
.step-n {
  width: 26px; height: 26px; border-radius: 99px; background: var(--tint); flex-shrink: 0;
  display: grid; place-items: center; font-size: 12px; font-weight: 700; color: var(--primary);
}
.step-txt { font-size: 13px; color: var(--ink-2); line-height: 1.5; }
</style>
```

`mensi.client/app/components/ma/HeroCard.vue`:

```vue
<script setup lang="ts">
import type { Overview, StripDay } from '~/types/api'
import { CATEGORY_LEGEND, CONFIDENCE_LABELS, STRIP_COLORS } from '~/utils/labels'
import { formatDateShort, formatRange } from '~/utils/format'

const props = defineProps<{ overview: Overview }>()
const o = computed(() => props.overview)
const WEEK_HEADS = ['H', 'K', 'Sz', 'Cs', 'P', 'Szo', 'V']

function tagOf(day: StripDay): string {
  if (day.isToday) return 'ma'
  const d = new Date(`${day.date}T00:00:00`)
  return d.getDate() === 1 ? formatDateShort(day.date).split(' ')[0]! : ''
}
const dayNum = (day: StripDay) => new Date(`${day.date}T00:00:00`).getDate()
</script>

<template>
  <div class="hero">
    <div class="hero-top">
      <div class="hero-row">
        <span class="hero-tag">Ciklus {{ o.cycle!.day }}. nap</span>
        <span class="hero-since">{{ formatDateShort(o.cycle!.startDate) }} óta</span>
      </div>
      <div class="hero-headline">{{ o.headline }}</div>
      <div class="hero-boxes">
        <div class="hero-box">
          <div class="hero-box-label">Ovuláció</div>
          <div class="hero-box-value">{{ formatRange(o.ovulationWindow!.from, o.ovulationWindow!.to) }}</div>
        </div>
        <div class="hero-box">
          <div class="hero-box-label">Következő menstruáció</div>
          <div class="hero-box-value">{{ formatRange(o.nextPeriodWindow!.from, o.nextPeriodWindow!.to) }}</div>
        </div>
      </div>
      <div class="phase">
        <div class="phase-row">
          <span class="phase-label">{{ o.phase!.label }}</span>
          <span class="phase-remaining">{{ o.phase!.remainingDays }} nap</span>
        </div>
        <div class="phase-dots">
          <div v-for="i in o.phase!.totalDays" :key="i" class="phase-dot"
            :class="{ done: i <= o.phase!.elapsedDays }" />
        </div>
      </div>
    </div>

    <div class="hero-bottom">
      <div class="strip-head">
        <span class="strip-range">{{ formatRange(o.strip!.from, o.strip!.to) }}</span>
        <span class="chip strip-conf">konfidencia: {{ CONFIDENCE_LABELS[o.confidence!] }}</span>
      </div>
      <div class="strip-grid">
        <div v-for="w in WEEK_HEADS" :key="w" class="strip-weekhead">{{ w }}</div>
        <div v-for="day in o.strip!.days" :key="day.date" class="strip-cell" :style="{
          background: STRIP_COLORS[day.category].bg,
          color: STRIP_COLORS[day.category].fg,
          boxShadow: day.isToday ? 'inset 0 0 0 2px #21243d' : 'none',
        }">
          <span class="strip-tag">{{ tagOf(day) }}</span>
          <span class="strip-num" :class="{ bold: day.isToday || day.category === 'ovulation' }">
            {{ dayNum(day) }}
          </span>
        </div>
      </div>
      <div class="legend">
        <div v-for="item in CATEGORY_LEGEND" :key="item.key" class="legend-item">
          <span class="legend-dot" :style="{ background: STRIP_COLORS[item.key].bg }" />
          <span>{{ item.label }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.hero { border-radius: 20px; overflow: hidden; box-shadow: 0 1px 2px rgba(33,36,61,.05), 0 6px 22px rgba(33,36,61,.07); }
.hero-top { background: var(--primary); padding: 20px 18px 18px; }
.hero-row { display: flex; align-items: center; }
.hero-tag { font-size: 12px; font-weight: 600; color: #d2daff; }
.hero-since { margin-left: auto; font-size: 11.5px; font-weight: 500; color: var(--lavender); }
.hero-headline { font-size: 25px; font-weight: 700; color: #fff; letter-spacing: -.025em; line-height: 1.18; margin-top: 10px; }
.hero-boxes { display: flex; gap: 8px; margin-top: 16px; }
.hero-box { flex: 1; background: rgba(255,255,255,.16); border-radius: 12px; padding: 11px 13px; }
.hero-box-label { font-size: 10.5px; font-weight: 600; color: #d2daff; }
.hero-box-value { font-size: 15px; font-weight: 700; color: #fff; margin-top: 3px; }
.phase { margin-top: 14px; }
.phase-row { display: flex; align-items: baseline; }
.phase-label { font-size: 11px; font-weight: 600; color: #d2daff; }
.phase-remaining { margin-left: auto; font-size: 11px; font-weight: 700; color: #fff; }
.phase-dots { display: flex; gap: 4px; margin-top: 7px; }
.phase-dot { flex: 1; height: 7px; border-radius: 99px; background: #fff; }
.phase-dot.done { background: rgba(255,255,255,.35); }
.hero-bottom { background: #fff; padding: 18px; }
.strip-head { display: flex; align-items: center; }
.strip-range { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.strip-conf { margin-left: auto; color: var(--primary-hover); background: var(--tint); font-weight: 700; }
.strip-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 5px; margin: 14px auto 0; max-width: 400px; }
.strip-weekhead { text-align: center; font-size: 10px; font-weight: 600; color: var(--ink-4); padding-bottom: 2px; }
.strip-cell {
  aspect-ratio: 1; border-radius: 10px; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 1px;
}
.strip-tag { font-size: 8px; font-weight: 600; line-height: 1; min-height: 8px; }
.strip-num { font-size: 11.5px; font-weight: 500; line-height: 1.1; }
.strip-num.bold { font-weight: 700; }
.legend { display: flex; flex-wrap: wrap; gap: 9px 13px; margin-top: 15px; }
.legend-item { display: flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 500; color: var(--ink-2); }
.legend-dot { width: 9px; height: 9px; border-radius: 3px; }
</style>
```

`mensi.client/app/components/ma/TimingCard.vue`:

```vue
<script setup lang="ts">
import type { Overview } from '~/types/api'
import { TIMING_LABELS } from '~/utils/labels'
import { formatDateShort, formatPercent } from '~/utils/format'

const props = defineProps<{ overview: Overview }>()
const t = computed(() => props.overview.timing!)
const short = (iso: string) => formatDateShort(iso).replace(' ', '')
</script>

<template>
  <NuxtLink to="/esely" class="card timing">
    <div class="head">
      <span class="head-label">Időzítés ebben a ciklusban</span>
      <span class="chip head-chip">{{ TIMING_LABELS[t.label] }} · {{ formatPercent(t.chancePercent) }}</span>
    </div>
    <div class="days">
      <div v-for="d in t.windowDays" :key="d.date" class="day" :style="{
        background: d.intercourseCount > 0 ? 'var(--primary)' : d.isFuture ? 'var(--surface)' : '#e3e8fb',
        boxShadow: d.isOvulationWindow ? 'inset 0 0 0 2px var(--primary)' : 'none',
      }">
        <span v-if="d.intercourseCount > 0" class="day-count">{{ d.intercourseCount }}×</span>
      </div>
    </div>
    <div class="dates">
      <div v-for="d in t.windowDays" :key="d.date" class="date"
        :class="{ ovu: d.isOvulationWindow }">{{ short(d.date) }}</div>
    </div>
    <div class="note">
      <span class="note-mark" />
      <span>Ovulációs ablak — itt számít legtöbbet az együttlét</span>
    </div>
    <div class="summary">
      <span>{{ t.intercourseTotal }} együttlét · {{ t.daysRemaining }} nap hátra</span>
      <span class="details">Részletek</span>
    </div>
  </NuxtLink>
</template>

<style scoped>
.timing { display: block; text-decoration: none; color: inherit; cursor: pointer; }
.timing:hover { box-shadow: 0 1px 2px rgba(33,36,61,.08), 0 8px 26px rgba(33,36,61,.1); }
.head { display: flex; align-items: baseline; }
.head-label { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.head-chip { margin-left: auto; font-weight: 700; color: var(--primary-hover); background: var(--tint); }
.days { display: flex; gap: 5px; margin-top: 14px; }
.day { flex: 1; height: 26px; border-radius: 8px; display: flex; align-items: center; justify-content: center; }
.day-count { font-size: 9px; font-weight: 700; color: #fff; }
.dates { display: flex; gap: 5px; margin-top: 5px; }
.date { flex: 1; text-align: center; font-size: 8.5px; font-weight: 500; color: #a8adc7; }
.date.ovu { font-weight: 700; color: var(--plum-ink); }
.note { display: flex; align-items: center; gap: 6px; margin-top: 9px; font-size: 10.5px; font-weight: 500; color: var(--muted); }
.note-mark { width: 8px; height: 8px; border-radius: 2px; box-shadow: inset 0 0 0 2px var(--primary); }
.summary { display: flex; align-items: center; margin-top: 9px; font-size: 12px; color: var(--ink-2); }
.details { margin-left: auto; font-weight: 700; color: var(--primary); }
</style>
```

`mensi.client/app/components/ma/YesterdayCard.vue`:

```vue
<script setup lang="ts">
import type { Overview } from '~/types/api'
import { FIELD_ORDER, FIELD_LABELS } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'
import { formatDateShort } from '~/utils/format'
import { addDays } from '~/utils/format'

const props = defineProps<{ overview: Overview }>()
const chips = computed(() => {
  const log = props.overview.yesterdayLog
  const items: string[] = []
  for (const key of FIELD_ORDER) {
    const value = fieldValue(log, key)
    if (value === null) continue
    items.push(key === 'bbt' ? value : `${FIELD_LABELS[key]}: ${value}`)
  }
  return items
})
const dateLabel = computed(() => formatDateShort(addDays(props.overview.today, -1)))
</script>

<template>
  <div class="card">
    <div class="title">Tegnap · {{ dateLabel }}</div>
    <div class="chips">
      <span v-if="chips.length === 0" class="chip empty">Nincs bejegyzés</span>
      <span v-for="c in chips" :key="c" class="chip item">{{ c }}</span>
    </div>
  </div>
</template>

<style scoped>
.title { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.chips { display: flex; flex-wrap: wrap; gap: 7px; margin-top: 11px; }
.chip { font-size: 12px; padding: 6px 12px; }
.item { color: var(--plum-ink); background: var(--tint); }
.empty { color: var(--ink-3); background: var(--surface); }
</style>
```

`mensi.client/app/components/ma/TodayCard.vue`:

```vue
<script setup lang="ts">
import type { Overview } from '~/types/api'
import { FIELD_ORDER, FIELD_LABELS } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'

const props = defineProps<{ overview: Overview }>()
const store = useAppStore()
const rows = computed(() => FIELD_ORDER.map((key, i) => {
  const value = fieldValue(props.overview.todayLog, key)
  return { key, i, label: FIELD_LABELS[key], value }
}))
const filled = computed(() => rows.value.filter(r => r.value !== null).length)
</script>

<template>
  <div class="card">
    <div class="head">
      <span class="section-title">Mai bejegyzés</span>
      <span class="count">{{ filled }} / 7 kitöltve</span>
    </div>
    <div class="sub">Külön-külön is rögzíthető — csak azt töltsd ki, ami ma történt.</div>
    <div class="rows">
      <button v-for="row in rows" :key="row.key" class="row"
        :class="{ filled: row.value !== null }"
        @click="store.openSheet(overview.today, row.i, true)">
        <span class="row-label">{{ row.label }}</span>
        <span class="row-value" :class="{ set: row.value !== null }">{{ row.value ?? 'nincs rögzítve' }}</span>
        <span class="row-action">{{ row.value !== null ? 'módosítás' : 'rögzítés' }}</span>
      </button>
    </div>
    <button class="btn all" @click="store.openSheet(overview.today, 0, false)">Mind végigkérdezése</button>
  </div>
</template>

<style scoped>
.head { display: flex; align-items: center; }
.count { margin-left: auto; font-size: 11.5px; font-weight: 600; color: var(--ink-3); }
.sub { font-size: 12px; color: var(--ink-3); margin-top: 5px; line-height: 1.45; }
.rows { display: flex; flex-direction: column; gap: 3px; margin-top: 13px; }
.row {
  display: flex; align-items: center; gap: 12px; padding: 12px 13px; border-radius: 12px;
  background: #f7f8fd; border: 0; cursor: pointer; font-family: inherit; text-align: left;
}
.row.filled { background: var(--tint); }
.row:hover { background: var(--tint); }
.row-label { font-size: 12.5px; font-weight: 600; color: var(--ink-2); width: 84px; flex-shrink: 0; }
.row-value { font-size: 13.5px; font-weight: 500; color: var(--ink-4); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.row-value.set { font-weight: 700; color: var(--ink); }
.row-action { margin-left: auto; flex-shrink: 0; font-size: 11.5px; font-weight: 700; color: var(--primary); white-space: nowrap; }
.all { margin-top: 13px; background: var(--tint); color: var(--primary-deep); padding: 15px 0; font-size: 13px; }
.all:hover { background: #dfe4ff; }
</style>
```

- [ ] **Step 3: Type-check + vizuális ellenőrzés**

Run: `cd mensi.client && npm run type-check`
Expected: zöld. Utána `npm run dev` + a backend (`dotnet run --project Mensi.Server`) mellett nyisd meg a http://localhost:3000-t: üres DB-nél az empty state, seedelt adatnál a 4 kártya jelenjen meg. Vesd össze a prototípussal (`docs/design/mensi-care-prototipus-kicsomagolt.html` böngészőben megnyitva) és igazítsd a részleteket.

- [ ] **Step 4: Commit**

```bash
git add mensi.client
git commit -m "feat: Ma nézet (hero, sáv, időzítés, tegnap, mai bejegyzés, empty state)"
```

---

### Task 18: LogSheet varázsló (8 lépés) + WheelPicker + SaveToast

**Files:**
- Create: `mensi.client/app/components/WheelPicker.vue`
- Modify (placeholder cseréje): `mensi.client/app/components/LogSheet.vue`, `mensi.client/app/components/SaveToast.vue`

**Interfaces:**
- Consumes: `useAppStore()` (sheetOpen/sheetDate/sheetStep/sheetSingle, saveLog, saveIntercourse, closeSheet), `useApi().log`, címkék + `fieldValue`
- Produces: globális sheet, amit bármely nézet a `store.openSheet(date, step, single)` hívással nyit
- Vizuális referencia: prototípus `sheet` blokk (step0–step7)

- [ ] **Step 1: WheelPicker**

`mensi.client/app/components/WheelPicker.vue`:

```vue
<script setup lang="ts">
const props = defineProps<{ options: number[]; modelValue: number; width?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()
const ROW = 58
const wheel = ref<HTMLElement | null>(null)
let scrollTimer: ReturnType<typeof setTimeout> | null = null

function scrollToValue(behavior: ScrollBehavior = 'auto') {
  const index = props.options.indexOf(props.modelValue)
  if (wheel.value && index >= 0) wheel.value.scrollTo({ top: index * ROW, behavior })
}
onMounted(() => scrollToValue())
watch(() => props.modelValue, () => scrollToValue('smooth'))

function onScroll() {
  if (scrollTimer) clearTimeout(scrollTimer)
  scrollTimer = setTimeout(() => {
    if (!wheel.value) return
    const index = Math.round(wheel.value.scrollTop / ROW)
    const value = props.options[Math.min(Math.max(index, 0), props.options.length - 1)]!
    if (value !== props.modelValue) emit('update:modelValue', value)
  }, 120)
}
</script>

<template>
  <div ref="wheel" class="wheel noscroll" :style="{ width: width ?? '62px' }" @scroll="onScroll">
    <div class="pad" />
    <button v-for="option in options" :key="option" class="item"
      :class="{ active: option === modelValue }" @click="emit('update:modelValue', option)">
      {{ option }}
    </button>
    <div class="pad" />
  </div>
</template>

<style scoped>
.wheel { height: 172px; overflow-y: auto; scroll-snap-type: y mandatory; background: var(--bg); border-radius: 18px; }
.pad { height: 57px; }
.item {
  width: 100%; height: 58px; scroll-snap-align: center; display: grid; place-items: center;
  font: 500 20px 'Montserrat', sans-serif; color: var(--ink-4); border: 0; background: none; cursor: pointer;
}
.item.active { font-size: 30px; font-weight: 700; color: var(--ink); }
</style>
```

- [ ] **Step 2: SaveToast**

`mensi.client/app/components/SaveToast.vue` (a placeholder cseréje):

```vue
<script setup lang="ts">
const store = useAppStore()
</script>

<template>
  <Transition name="toast">
    <div v-if="store.toastVisible" class="wrap">
      <div class="toast">
        <div class="row">
          <div class="mark">✓</div>
          <span class="text">Mentve</span>
          <button class="undo" @click="store.undo()">Visszavonás</button>
        </div>
        <div class="bar-track"><div class="bar" /></div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.wrap { position: fixed; left: 16px; right: 16px; bottom: 96px; z-index: 60; display: flex; justify-content: center; }
.toast { width: 100%; max-width: 420px; background: var(--primary); border-radius: 16px; color: #fff; overflow: hidden; box-shadow: 0 10px 34px rgba(90,92,214,.38); }
.row { display: flex; align-items: center; gap: 12px; padding: 14px 16px; }
.mark { width: 26px; height: 26px; border-radius: 99px; background: rgba(255,255,255,.22); display: grid; place-items: center; font-size: 13px; font-weight: 700; flex-shrink: 0; }
.text { font-size: 13.5px; font-weight: 600; }
.undo {
  margin-left: auto; font: 700 13px 'Montserrat', sans-serif; color: #fff;
  background: rgba(255,255,255,.18); border: 0; border-radius: 99px; padding: 7px 13px; cursor: pointer;
}
.undo:hover { background: rgba(255,255,255,.28); }
.bar-track { height: 3px; background: rgba(255,255,255,.25); }
.bar { height: 100%; background: #fff; animation: shrink 3.4s linear forwards; }
@keyframes shrink { from { width: 100%; } to { width: 0; } }
.toast-enter-active, .toast-leave-active { transition: opacity .15s, transform .15s; }
.toast-enter-from, .toast-leave-to { opacity: 0; transform: translateY(8px); }
</style>
```

- [ ] **Step 3: LogSheet**

`mensi.client/app/components/LogSheet.vue` (a placeholder cseréje):

```vue
<script setup lang="ts">
import type { CervicalMucus, CrampType, DailyLog, FlowIntensity, LhTest, LogPatch, Mood } from '~/types/api'
import { CRAMP_SEVERITY_LABELS, CRAMP_TYPE_LABELS, CRAMP_TYPE_ORDER, FIELD_LABELS, FIELD_ORDER, FLOW_LABELS, FLOW_ORDER, LH_LABELS, LH_NOTES, LH_ORDER, MOOD_EMOJI, MOOD_LABELS, MOOD_ORDER, MUCUS_LABELS, MUCUS_ORDER } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'
import { formatTemp } from '~/utils/format'

const store = useAppStore()
const api = useApi()

const STEPS = ['Testhő', 'Nyák', 'LH-teszt', 'Görcs', 'Folyás', 'Együttlét', 'Hangulat', 'Összegzés']

const before = ref<DailyLog | null>(null)
const step = ref(0)
const skipped = ref<Set<number>>(new Set())
const touched = ref<Set<number>>(new Set())

// mezőállapot
const whole = ref(36); const tenths = ref(3); const hundredths = ref(6); const tempSet = ref(false)
const mucus = ref<CervicalMucus | null>(null)
const lh = ref<LhTest | null>(null)
const crampType = ref<CrampType | null>(null)
const crampSeverity = ref<number | null>(null)
const flow = ref<FlowIntensity | null>(null)
const periodStart = ref(false)
const sexEvents = ref<{ protected: boolean | null }[]>([])
const sexTouched = ref(false)
const moods = ref<Mood[]>([])

const bbtValue = computed(() => whole.value + tenths.value / 10 + hundredths.value / 100)

watch(() => store.sheetOpen, async (open) => {
  if (!open || !store.sheetDate) return
  step.value = store.sheetStep
  skipped.value = new Set()
  touched.value = new Set()
  const log = await api.log(store.sheetDate)
  before.value = log
  tempSet.value = log.bbtCelsius !== null
  if (log.bbtCelsius !== null) {
    whole.value = Math.floor(log.bbtCelsius)
    tenths.value = Math.floor(log.bbtCelsius * 10) % 10
    hundredths.value = Math.round(log.bbtCelsius * 100) % 10
  } else { whole.value = 36; tenths.value = 3; hundredths.value = 6 }
  mucus.value = log.cervicalMucus
  lh.value = log.lhTest
  crampType.value = log.crampType
  crampSeverity.value = log.crampSeverity
  flow.value = log.flowIntensity
  periodStart.value = log.periodStart
  sexEvents.value = log.intercourse.map(e => ({ protected: e.protected }))
  sexTouched.value = log.intercourse.length > 0
  moods.value = [...log.moods]
})

function touch(i: number) { touched.value.add(i); skipped.value.delete(i) }

function buildPatch(only?: number): LogPatch {
  const patch: LogPatch = {}
  const include = (i: number) => (only === undefined ? touched.value.has(i) : only === i)
  if (include(0)) patch.bbtCelsius = tempSet.value ? bbtValue.value : null
  if (include(1)) patch.cervicalMucus = mucus.value
  if (include(2)) patch.lhTest = lh.value
  if (include(3)) { patch.crampType = crampType.value; patch.crampSeverity = crampSeverity.value }
  if (include(4)) { patch.flowIntensity = flow.value; patch.periodStart = periodStart.value }
  if (include(6)) patch.moods = moods.value
  return patch
}

async function save() {
  const date = store.sheetDate!
  const single = store.sheetSingle
  const active = step.value
  store.closeSheet()
  if (single && active === 5) {
    await store.saveIntercourse(date, sexEvents.value, before.value)
    return
  }
  if (single) {
    await store.saveLog(date, buildPatch(active), before.value)
    return
  }
  const patch = buildPatch()
  if (Object.keys(patch).length > 0) await store.saveLog(date, patch, before.value)
  if (touched.value.has(5)) await store.saveIntercourse(date, sexEvents.value, before.value)
}

function next() {
  if (store.sheetSingle || step.value === 7) { void save(); return }
  step.value = Math.min(step.value + 1, 7)
}
function skip() {
  skipped.value.add(step.value)
  step.value = Math.min(step.value + 1, 6)
}
const summaryRows = computed(() => {
  const preview: DailyLog = {
    ...(before.value ?? {
      date: store.sheetDate ?? '', bbtOutlier: false, updatedAt: null, updatedBy: null,
      bbtCelsius: null, cervicalMucus: null, lhTest: null, crampType: null,
      crampSeverity: null, flowIntensity: null, periodStart: false, moods: [], intercourse: [],
    }),
    bbtCelsius: tempSet.value ? bbtValue.value : null,
    cervicalMucus: mucus.value,
    lhTest: lh.value,
    crampType: crampType.value,
    crampSeverity: crampSeverity.value,
    flowIntensity: flow.value,
    periodStart: periodStart.value,
    moods: moods.value,
    intercourse: sexEvents.value.map((e, i) => ({ id: i, protected: e.protected })),
  }
  return FIELD_ORDER.map((key, i) => ({ i, label: FIELD_LABELS[key], value: fieldValue(preview, key) }))
})
</script>

<template>
  <Teleport to="body">
    <div v-if="store.sheetOpen" class="overlay">
      <div class="backdrop" @click="store.closeSheet()" />
      <div class="box">
        <div class="head">
          <div class="grip" />
          <div class="head-row">
            <span class="section-title">{{ STEPS[step] }}</span>
            <span class="count">{{ store.sheetSingle ? 'egy mező' : `${step + 1} / 8` }}</span>
            <button class="close" aria-label="Bezárás" @click="store.closeSheet()">✕</button>
          </div>
          <div v-if="!store.sheetSingle" class="dots">
            <button v-for="(s, i) in STEPS" :key="s" class="dot"
              :class="{ current: i === step, done: i < step && !skipped.has(i), skipped: skipped.has(i) }"
              @click="step = i" />
          </div>
        </div>

        <div class="body noscroll">
          <!-- 0: Testhő -->
          <div v-if="step === 0">
            <div class="step-title">Testhő</div>
            <div class="step-sub">Nem kötelező.
              <template v-if="store.overview?.yesterdayLog?.bbtCelsius">
                Tegnap: {{ formatTemp(store.overview.yesterdayLog.bbtCelsius) }}</template>
            </div>
            <div class="wheels" @click="tempSet = true; touch(0)">
              <WheelPicker v-model="whole" :options="[35, 36, 37, 38]" width="80px" @update:model-value="tempSet = true; touch(0)" />
              <div class="wheel-sep">,</div>
              <WheelPicker v-model="tenths" :options="[0,1,2,3,4,5,6,7,8,9]" @update:model-value="tempSet = true; touch(0)" />
              <WheelPicker v-model="hundredths" :options="[0,1,2,3,4,5,6,7,8,9]" @update:model-value="tempSet = true; touch(0)" />
              <div class="wheel-unit">°C</div>
            </div>
          </div>

          <!-- 1: Nyák -->
          <div v-else-if="step === 1">
            <div class="step-title">Cervikális nyák</div>
            <div class="step-sub">Szárazból nyúlósba — a nyúlós a legtermékenyebb.</div>
            <div class="mucus-row">
              <button v-for="(key, i) in MUCUS_ORDER" :key="key" class="mucus-opt"
                @click="mucus = key; touch(1)">
                <div class="mucus-swatch" :class="{ active: mucus === key }"
                  :style="{ background: mucus === key ? 'var(--primary)' : ['#f2f6ff', '#dfe9ff', '#c6d6ff', '#aac4ff'][i] }" />
                <div class="opt-label" :class="{ active: mucus === key }">{{ MUCUS_LABELS[key] }}</div>
              </button>
            </div>
          </div>

          <!-- 2: LH -->
          <div v-else-if="step === 2">
            <div class="step-title">LH-teszt</div>
            <div class="step-sub" v-if="store.overview?.yesterdayLog?.lhTest">
              Tegnap: {{ LH_LABELS[store.overview.yesterdayLog.lhTest].toLowerCase() }}</div>
            <div class="lh-col">
              <button v-for="key in LH_ORDER" :key="key" class="lh-opt" :class="{ active: lh === key }"
                @click="lh = key; touch(2)">
                <span class="lh-label">{{ LH_LABELS[key] }}</span>
                <span class="lh-note">{{ LH_NOTES[key] }}</span>
              </button>
            </div>
          </div>

          <!-- 3: Görcs -->
          <div v-else-if="step === 3">
            <div class="step-title">Görcs</div>
            <div class="step-sub">Előbb a hely, aztán az erőssége.</div>
            <div class="cramp-types">
              <button v-for="key in CRAMP_TYPE_ORDER" :key="key" class="cramp-type"
                :class="{ active: crampType === key, disabled: crampSeverity === 0 }"
                :disabled="crampSeverity === 0"
                @click="crampType = key; if (crampSeverity === null) crampSeverity = 1; touch(3)">
                {{ CRAMP_TYPE_LABELS[key] }}
              </button>
            </div>
            <div v-if="crampSeverity === 0" class="cramp-hint">Nincs görcs esetén nincs mit kiválasztani.</div>
            <div class="scale-label">Intenzitás</div>
            <div class="scale">
              <button v-for="(label, i) in CRAMP_SEVERITY_LABELS" :key="label" class="scale-opt"
                @click="crampSeverity = i; if (i === 0) crampType = null; touch(3)">
                <div class="scale-bar" :class="{ active: crampSeverity === i }"
                  :style="{ height: `${24 + i * 15}px`,
                    background: crampSeverity === i ? 'var(--primary)' : `rgba(90,92,214,${0.1 + i * 0.16})` }" />
                <div class="opt-label" :class="{ active: crampSeverity === i }">{{ label }}</div>
              </button>
            </div>
          </div>

          <!-- 4: Folyás -->
          <div v-else-if="step === 4">
            <div class="step-title">Folyás</div>
            <div class="scale">
              <button v-for="(key, i) in FLOW_ORDER" :key="key" class="scale-opt"
                @click="flow = key; touch(4)">
                <div class="scale-bar" :class="{ active: flow === key }"
                  :style="{ height: `${22 + i * 14}px`,
                    background: flow === key ? 'var(--plum)' : `rgba(150,152,226,${0.12 + i * 0.17})` }" />
                <div class="opt-label" :class="{ active: flow === key }">{{ FLOW_LABELS[key] }}</div>
              </button>
            </div>
            <button class="period-toggle" :class="{ active: periodStart }" @click="periodStart = !periodStart; touch(4)">
              <span class="period-box" :class="{ active: periodStart }">{{ periodStart ? '✓' : '' }}</span>
              <span>
                <span class="period-title">Ma kezdődött a menstruáció</span>
                <span class="period-sub">Ez lesz az új ciklus 1. napja.</span>
              </span>
            </button>
          </div>

          <!-- 5: Együttlét -->
          <div v-else-if="step === 5">
            <div class="step-title">Együttlét</div>
            <div class="step-sub">Ez az egyetlen adat, ami a fogamzási esélybe számít. Egy napon több is lehet.</div>
            <div class="sex-box" :class="{ active: sexEvents.length > 0 }">
              <div>
                <div class="sex-label">{{ sexEvents.length === 0 ? (sexTouched ? 'Ma nem volt' : 'Nincs rögzítve')
                  : sexEvents.length === 1 ? 'Egy alkalom' : `${sexEvents.length} alkalom` }}</div>
                <div class="sex-note">{{ sexTouched ? 'rögzítve lesz mentéskor' : 'állítsd be a mai számot' }}</div>
              </div>
              <div class="sex-controls">
                <button class="sex-btn minus" aria-label="Kevesebb"
                  @click="sexEvents = sexEvents.slice(0, -1); sexTouched = true; touch(5)">−</button>
                <span class="sex-count">{{ sexEvents.length }}</span>
                <button class="sex-btn plus" aria-label="Több"
                  @click="if (sexEvents.length < 6) sexEvents = [...sexEvents, { protected: false }]; sexTouched = true; touch(5)">+</button>
              </div>
            </div>
            <div v-if="sexEvents.length > 0" class="sex-events">
              <div v-for="(ev, i) in sexEvents" :key="i" class="sex-event">
                <span class="sex-event-label">{{ i + 1 }}. alkalom</span>
                <span class="sex-event-note">Védekezéssel volt</span>
                <button class="switch" :class="{ on: ev.protected === true }" role="switch"
                  :aria-checked="ev.protected === true"
                  @click="sexEvents[i] = { protected: ev.protected !== true }; touch(5)">
                  <span class="knob" />
                </button>
              </div>
            </div>
            <div class="sex-footnote">A 0 azt jelenti, hogy ma nem volt együttlét — ez is rögzített adat, nem hiányzó.</div>
          </div>

          <!-- 6: Hangulat -->
          <div v-else-if="step === 6">
            <div class="step-title">Hangulat</div>
            <div class="step-sub">Ez is jelezheti az ovulációt — több is kiválasztható.</div>
            <div class="moods">
              <button v-for="key in MOOD_ORDER" :key="key" class="mood-chip"
                :class="{ active: moods.includes(key) }"
                @click="moods = moods.includes(key) ? moods.filter(m => m !== key) : [...moods, key]; touch(6)">
                {{ MOOD_EMOJI[key] }} {{ MOOD_LABELS[key] }}
              </button>
            </div>
          </div>

          <!-- 7: Összegzés -->
          <div v-else>
            <div class="step-title">Összegzés</div>
            <div class="step-sub">Bármelyik sorra koppintva javíthatod.</div>
            <div class="summary">
              <button v-for="row in summaryRows" :key="row.i" class="summary-row" @click="step = row.i">
                <span class="summary-label">{{ row.label }}</span>
                <span class="summary-value" :class="{ set: row.value !== null }">{{ row.value ?? 'Kihagyva' }}</span>
                <span class="summary-action">{{ row.value !== null ? 'javítás' : 'kitöltés' }}</span>
              </button>
            </div>
          </div>
        </div>

        <div class="foot">
          <button v-if="store.sheetSingle" class="btn btn-ghost foot-cancel" @click="store.closeSheet()">Mégse</button>
          <button v-if="!store.sheetSingle && step > 0" class="foot-back" aria-label="Vissza"
            @click="step = Math.max(0, step - 1)">←</button>
          <button v-if="!store.sheetSingle && step < 6" class="btn btn-ghost foot-skip" @click="skip()">Kihagyom</button>
          <button class="btn btn-primary foot-next" @click="next()">
            {{ store.sheetSingle || step === 7 ? 'Mentés' : step === 6 ? 'Összegzés' : 'Tovább' }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.overlay { position: fixed; inset: 0; z-index: 50; display: flex; align-items: flex-end; justify-content: center; }
.backdrop { position: absolute; inset: 0; background: rgba(33, 36, 61, .4); }
.box {
  position: relative; width: 100%; max-width: 560px; max-height: 88vh; background: #fff;
  border-radius: 22px 22px 0 0; display: flex; flex-direction: column;
  box-shadow: 0 -10px 40px rgba(33, 36, 61, .18);
}
.head { padding: 14px 20px; flex-shrink: 0; }
.grip { width: 38px; height: 4px; border-radius: 99px; background: #d8dcec; margin: 0 auto 14px; }
.head-row { display: flex; align-items: center; }
.count { margin-left: auto; font-size: 11.5px; font-weight: 600; color: var(--ink-3); }
.close {
  margin-left: 12px; width: 28px; height: 28px; border-radius: 99px; background: var(--bg);
  border: 0; display: grid; place-items: center; font-size: 13px; color: var(--ink-2); cursor: pointer;
}
.dots { display: flex; gap: 5px; margin-top: 12px; }
.dot { flex: 1; height: 5px; border-radius: 99px; background: #e3e8fb; border: 0; cursor: pointer; padding: 0; }
.dot.done { background: var(--light-blue); }
.dot.skipped { background: #d8dcec; }
.dot.current { background: var(--primary); }
.body { flex: 1; overflow-y: auto; padding: 8px 20px 16px; }
.step-title { font-size: 21px; font-weight: 700; letter-spacing: -.02em; }
.step-sub { font-size: 13px; color: var(--ink-2); margin-top: 6px; }
.wheels { display: flex; gap: 8px; margin-top: 20px; justify-content: center; align-items: center; }
.wheel-sep { font-size: 26px; font-weight: 700; color: var(--ink-4); }
.wheel-unit { font-size: 15px; font-weight: 600; color: var(--ink-3); }
.mucus-row { display: flex; gap: 8px; margin-top: 22px; }
.mucus-opt { flex: 1; border: 0; background: none; cursor: pointer; display: flex; flex-direction: column; gap: 9px; padding: 0; }
.mucus-swatch { height: 70px; border-radius: 14px; width: 100%; }
.mucus-swatch.active { box-shadow: inset 0 0 0 3px var(--plum-ink), 0 0 0 4px rgba(90,92,214,.16); }
.opt-label { font-size: 11px; font-weight: 600; text-align: center; color: var(--ink-2); width: 100%; }
.opt-label.active { color: var(--primary); }
.lh-col { display: flex; flex-direction: column; gap: 10px; margin-top: 20px; }
.lh-opt {
  padding: 19px 18px; border-radius: 16px; background: var(--surface); border: 0; cursor: pointer;
  display: flex; align-items: center; font-family: inherit;
}
.lh-opt.active { background: var(--tint); box-shadow: inset 0 0 0 3px var(--primary), 0 0 0 4px rgba(90,92,214,.16); }
.lh-label { font-size: 15px; font-weight: 700; color: var(--ink); }
.lh-opt.active .lh-label { color: var(--primary-hover); }
.lh-note { margin-left: auto; font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.cramp-types { display: flex; gap: 9px; margin-top: 20px; }
.cramp-type {
  flex: 1; padding: 17px 0; text-align: center; border-radius: 14px; background: var(--surface);
  border: 0; cursor: pointer; font: 600 14px 'Montserrat', sans-serif; color: var(--ink);
}
.cramp-type.active { background: var(--tint); color: var(--primary); box-shadow: inset 0 0 0 3px var(--primary); }
.cramp-type.disabled { color: #c1c5db; opacity: .55; cursor: not-allowed; }
.cramp-hint { font-size: 11.5px; color: var(--muted); margin-top: 9px; }
.scale-label { font-size: 12px; font-weight: 600; color: var(--ink-3); margin-top: 24px; }
.scale { display: flex; gap: 8px; margin-top: 11px; align-items: flex-end; }
.scale-opt { flex: 1; border: 0; background: none; cursor: pointer; display: flex; flex-direction: column; gap: 9px; justify-content: flex-end; padding: 0; }
.scale-bar { border-radius: 12px; width: 100%; }
.scale-bar.active { box-shadow: inset 0 0 0 3px var(--plum-ink), 0 0 0 4px rgba(90,92,214,.16); }
.period-toggle {
  margin-top: 18px; display: flex; align-items: center; gap: 13px; padding: 16px; width: 100%;
  border-radius: 14px; background: var(--surface); border: 0; cursor: pointer; text-align: left; font-family: inherit;
}
.period-toggle.active { background: #e6e7fb; box-shadow: inset 0 0 0 3px var(--plum); }
.period-box {
  width: 22px; height: 22px; border-radius: 7px; background: #fff; flex-shrink: 0;
  box-shadow: inset 0 0 0 2px #d8dcec; display: grid; place-items: center;
  font-size: 12px; font-weight: 700; color: #fff;
}
.period-box.active { background: var(--plum); box-shadow: none; }
.period-title { display: block; font-size: 13.5px; font-weight: 600; color: var(--ink); }
.period-toggle.active .period-title { color: var(--plum-ink); }
.period-sub { display: block; font-size: 11.5px; color: var(--ink-3); margin-top: 2px; }
.sex-box {
  display: flex; align-items: center; gap: 14px; margin-top: 22px; padding: 20px;
  border-radius: 18px; background: var(--surface);
}
.sex-box.active { background: var(--tint); box-shadow: inset 0 0 0 3px var(--primary); }
.sex-label { font-size: 17px; font-weight: 700; color: var(--ink); }
.sex-box.active .sex-label { color: var(--primary); }
.sex-note { font-size: 12px; color: var(--ink-2); margin-top: 3px; }
.sex-controls { margin-left: auto; display: flex; align-items: center; gap: 10px; }
.sex-btn {
  width: 44px; height: 44px; border-radius: 99px; border: 0; cursor: pointer;
  font: 700 20px 'Montserrat', sans-serif;
}
.sex-btn.minus { background: #fff; color: var(--primary-deep); box-shadow: 0 1px 3px rgba(33,36,61,.14); }
.sex-btn.plus { background: var(--primary); color: #fff; box-shadow: 0 2px 6px rgba(90,92,214,.32); }
.sex-count { min-width: 26px; text-align: center; font-size: 24px; font-weight: 700; }
.sex-events { display: flex; flex-direction: column; gap: 8px; margin-top: 12px; }
.sex-event { display: flex; align-items: center; padding: 14px 16px; border-radius: 14px; background: var(--surface); }
.sex-event-label { font-size: 13px; font-weight: 600; }
.sex-event-note { margin-left: auto; margin-right: 10px; font-size: 12px; font-weight: 500; color: var(--ink-2); }
.switch { width: 46px; height: 27px; border-radius: 99px; background: #dde1ef; border: 0; position: relative; cursor: pointer; flex-shrink: 0; }
.switch.on { background: var(--primary); }
.knob {
  position: absolute; top: 2.5px; left: 2.5px; width: 22px; height: 22px; border-radius: 99px;
  background: #fff; box-shadow: 0 1px 3px rgba(33,36,61,.25); transition: left .15s;
}
.switch.on .knob { left: 21.5px; }
.sex-footnote { font-size: 12px; color: var(--ink-3); margin-top: 12px; line-height: 1.5; }
.moods { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 20px; }
.mood-chip {
  padding: 12px 16px; border-radius: 99px; background: var(--surface); border: 0; cursor: pointer;
  font: 600 13.5px 'Montserrat', sans-serif; color: var(--ink); white-space: nowrap;
}
.mood-chip.active { background: var(--tint); color: var(--primary); box-shadow: inset 0 0 0 3px var(--primary); }
.summary { display: flex; flex-direction: column; gap: 3px; margin-top: 16px; }
.summary-row {
  display: flex; align-items: center; padding: 14px; border-radius: 13px; border: 0;
  background: #fafbff; cursor: pointer; font-family: inherit; text-align: left;
}
.summary-row:nth-child(odd) { background: var(--surface); }
.summary-label { font-size: 12px; font-weight: 500; color: var(--ink-3); width: 92px; flex-shrink: 0; }
.summary-value { font-size: 14px; font-weight: 500; color: var(--ink-4); }
.summary-value.set { font-weight: 600; color: var(--ink); }
.summary-action { margin-left: auto; font-size: 11.5px; font-weight: 600; color: var(--primary); }
.foot { padding: 14px 20px 22px; display: flex; gap: 10px; flex-shrink: 0; background: #fff; }
.foot-cancel { flex: 1; }
.foot-back {
  width: 56px; flex-shrink: 0; background: var(--bg); border: 0; border-radius: 99px;
  color: var(--ink-2); font: 700 16px 'Montserrat', sans-serif; cursor: pointer;
}
.foot-skip { flex: 1; }
.foot-next { flex: 2; }
</style>
```

- [ ] **Step 4: Type-check + kézi próba**

Run: `cd mensi.client && npm run type-check`
Expected: zöld. Kézi próba (dev szerver + backend): teljes varázsló végigvitele → mentés → toast → Visszavonás visszaállítja az előző értékeket; egymezős mód a Mai bejegyzés sorból; a Testhő görgő szinkronban a koppintással.

- [ ] **Step 5: Commit**

```bash
git add mensi.client
git commit -m "feat: napló-sheet varázsló, görgős testhő-picker, mentés-toast undo-val"
```

---

### Task 19: Trendek nézet (ciklusstatisztika, BBT-táblázat, bejegyzés-hőtérkép)

**Files:**
- Create: `mensi.client/app/pages/trendek.vue`, `mensi.client/app/components/trendek/CycleStatsCard.vue`, `mensi.client/app/components/trendek/BbtTableCard.vue`, `mensi.client/app/components/trendek/EntriesGridCard.vue`

**Interfaces:**
- Consumes: `useApi().trends`, `useApi().logs`, formázók/címkék, `store.refreshTick` (mentés utáni újratöltéshez)
- Vizuális referencia: prototípus `isTrendek` blokk (`lenMarks`, `historyRows`, `bbtRows`, `gridRows`)

- [ ] **Step 1: Oldal + kártyák**

`mensi.client/app/pages/trendek.vue`:

```vue
<script setup lang="ts">
import type { DailyLog, Trends } from '~/types/api'

const store = useAppStore()
const api = useApi()
const trends = ref<Trends | null>(null)
const cycleLogs = ref<DailyLog[]>([])

async function load() {
  trends.value = await api.trends()
  const rows = trends.value.bbt?.rows
  if (rows && rows.length > 0)
    cycleLogs.value = (await api.logs(rows[0]!.date, rows[rows.length - 1]!.date)).days
  else cycleLogs.value = []
}
watch(() => store.refreshTick, load, { immediate: true })
</script>

<template>
  <div v-if="trends" class="stack">
    <div v-if="!trends.stats" class="card empty">
      Statisztikához legalább egy lezárt ciklus kell.
    </div>
    <TrendekCycleStatsCard v-if="trends.stats" :trends="trends" />
    <TrendekBbtTableCard v-if="trends.bbt" :bbt="trends.bbt" />
    <TrendekEntriesGridCard v-if="trends.bbt" :rows="trends.bbt.rows" :logs="cycleLogs" />
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.empty { font-size: 13px; color: var(--ink-2); }
</style>
```

`mensi.client/app/components/trendek/CycleStatsCard.vue`:

```vue
<script setup lang="ts">
import type { Trends } from '~/types/api'
import { TIMING_LABELS } from '~/utils/labels'
import { formatDateShort } from '~/utils/format'

const props = defineProps<{ trends: Trends }>()
const s = computed(() => props.trends.stats!)
const cycles = computed(() => props.trends.cycles)

// A sáv-vizualizáció skálája: [min−2, max+2] napra feszítve.
const axisMin = computed(() => s.value.minLength - 2)
const axisMax = computed(() => s.value.maxLength + 2)
const pos = (n: number) => `${((n - axisMin.value) / (axisMax.value - axisMin.value)) * 100}%`
const axis = computed(() => {
  const ticks: number[] = []
  for (let n = axisMin.value; n <= axisMax.value; n += 2) ticks.push(n)
  return ticks
})
const comma1 = (n: number) => n.toFixed(1).replace('.', ',')

const timingStyle = (label: 'weak' | 'medium' | 'good') => ({
  color: label === 'good' ? '#2f3170' : label === 'medium' ? '#4a4cbd' : '#626884',
  background: label === 'good' ? '#d2daff' : label === 'medium' ? '#eef1ff' : '#eef0f7',
})
const dev = (n: number) => n === 0 ? 'átlagos' : `${n > 0 ? '+' : '−'}${Math.abs(n)} nap`
</script>

<template>
  <div class="card">
    <div class="label">Ciklushossz · utolsó {{ Math.min(cycles.length, 6) }} ciklus</div>
    <div class="big-row">
      <span class="big">{{ Math.round(s.averageLength) }}</span>
      <span class="big-unit">nap az átlag</span>
    </div>
    <div class="sentence">A ciklusaid <b>{{ s.minLength }} és {{ s.maxLength }} nap</b> között mozogtak,
      ±{{ comma1(s.stdDev) }} nap szórással.</div>

    <div class="band">
      <div class="band-track" />
      <div class="band-range" :style="{ left: pos(s.minLength), width: `calc(${pos(s.maxLength)} - ${pos(s.minLength)})` }" />
      <div class="band-avg" :style="{ left: pos(s.averageLength) }" />
      <div v-for="c in cycles.slice(0, 6)" :key="c.startDate" class="band-mark"
        :style="{ left: pos(c.lengthDays), background: Math.abs(c.deviationFromAverage) >= 2 ? 'var(--primary)' : 'var(--lavender)' }" />
      <div v-for="t in axis" :key="t" class="band-tick" :style="{ left: pos(t) }">{{ t }}</div>
      <div class="band-avg-label" :style="{ left: pos(s.averageLength) }">átlag {{ Math.round(s.averageLength) }}</div>
    </div>

    <div class="tiles">
      <div class="tile"><div class="tile-v">{{ s.minLength }}</div><div class="tile-l">legrövidebb</div></div>
      <div class="tile"><div class="tile-v">{{ s.maxLength }}</div><div class="tile-l">leghosszabb</div></div>
      <div class="tile accent"><div class="tile-v">{{ s.averageLuteal === null ? '—' : comma1(s.averageLuteal) }}</div><div class="tile-l">luteális</div></div>
      <div class="tile"><div class="tile-v">{{ s.loggedPercent }}%</div><div class="tile-l">rögzített</div></div>
    </div>

    <div class="table-head">
      <span class="col-start">Kezdet</span><span class="col-len">Hossz</span>
      <span class="col-dev">Átlaghoz</span><span class="col-lut">Luteális</span>
      <span class="col-tim">Időzítés</span>
    </div>
    <div v-for="c in cycles" :key="c.startDate" class="table-row"
      :class="{ hot: Math.abs(c.deviationFromAverage) >= 2 }">
      <span class="col-start row-start">{{ formatDateShort(c.startDate) }}</span>
      <span class="col-len row-len">{{ c.lengthDays }} nap</span>
      <span class="col-dev"><span class="chip" :style="timingStyle(Math.abs(c.deviationFromAverage) >= 2 ? 'good' : 'medium')">{{ dev(c.deviationFromAverage) }}</span></span>
      <span class="col-lut row-lut">{{ c.anovulatory ? 'anovul.' : c.lutealLength === null ? '—' : `${c.lutealLength} nap` }}</span>
      <span class="col-tim"><span class="chip" :style="timingStyle(c.timing.label)">{{ TIMING_LABELS[c.timing.label] }}</span></span>
    </div>
  </div>
</template>

<style scoped>
.label { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.big-row { display: flex; align-items: baseline; gap: 9px; margin-top: 7px; }
.big { font-size: 38px; font-weight: 700; letter-spacing: -.035em; line-height: 1; }
.big-unit { font-size: 14px; font-weight: 600; color: var(--ink-2); }
.sentence { font-size: 13px; color: var(--ink-2); line-height: 1.55; margin-top: 8px; }
.band { position: relative; margin-top: 28px; height: 58px; }
.band-track { position: absolute; left: 0; right: 0; top: 22px; height: 10px; border-radius: 99px; background: #f0f2fb; }
.band-range { position: absolute; top: 22px; height: 10px; border-radius: 99px; background: var(--light-blue); }
.band-avg { position: absolute; top: 14px; width: 3px; height: 26px; margin-left: -1.5px; border-radius: 99px; background: var(--primary); }
.band-mark { position: absolute; top: 19px; width: 16px; height: 16px; margin-left: -8px; border-radius: 99px; border: 2px solid #fff; box-shadow: 0 1px 3px rgba(33,36,61,.2); }
.band-tick { position: absolute; top: 40px; transform: translateX(-50%); font-size: 10px; font-weight: 600; color: var(--ink-4); }
.band-avg-label { position: absolute; top: -4px; transform: translateX(-50%); font-size: 10px; font-weight: 700; color: var(--primary); white-space: nowrap; }
.tiles { display: flex; gap: 8px; margin-top: 22px; flex-wrap: wrap; }
.tile { flex: 1; min-width: 78px; background: var(--surface); border-radius: 14px; padding: 13px 12px; text-align: center; }
.tile.accent { background: var(--tint); }
.tile-v { font-size: 18px; font-weight: 700; letter-spacing: -.02em; }
.tile.accent .tile-v { color: #2f3170; }
.tile-l { font-size: 10.5px; font-weight: 600; color: var(--ink-3); margin-top: 2px; }
.table-head { display: flex; padding: 22px 0 9px; border-top: 1px solid var(--line); margin-top: 20px; font-size: 10.5px; font-weight: 600; color: var(--ink-3); }
.table-row { display: flex; align-items: center; padding: 11px 8px; margin: 0 -8px; border-radius: 10px; border-top: 1px solid var(--line); }
.table-row.hot { background: var(--surface); }
.col-start { flex: 1.4; }
.col-len { width: 58px; flex-shrink: 0; text-align: right; }
.col-dev { width: 78px; flex-shrink: 0; text-align: right; }
.col-lut { width: 62px; flex-shrink: 0; text-align: right; }
.col-tim { width: 72px; flex-shrink: 0; text-align: right; }
.row-start { font-size: 13px; font-weight: 600; }
.row-len { font-size: 13.5px; font-weight: 700; }
.row-lut { font-size: 12.5px; font-weight: 500; color: var(--ink-2); }
.chip { font-size: 11px; font-weight: 700; padding: 4px 9px; white-space: nowrap; }
</style>
```

`mensi.client/app/components/trendek/BbtTableCard.vue`:

```vue
<script setup lang="ts">
import type { Trends } from '~/types/api'
import { LH_LABELS, MUCUS_LABELS } from '~/utils/labels'
import { formatDateShort, formatDelta, formatTemp } from '~/utils/format'

const props = defineProps<{ bbt: NonNullable<Trends['bbt']> }>()
const comma2 = (n: number) => n.toFixed(2).replace('.', ',')

function marks(row: NonNullable<Trends['bbt']>['rows'][number]): string {
  if (row.isOutlier) return 'kiugró'
  const parts: string[] = []
  if (row.marks.cervicalMucus) parts.push(MUCUS_LABELS[row.marks.cervicalMucus].toLowerCase())
  if (row.marks.lhTest) parts.push(row.marks.lhTest === 'negative' ? 'LH−' : 'LH+')
  return parts.join(' · ') || '—'
}
</script>

<template>
  <div class="card">
    <div class="head">
      <span class="section-title">Bazális testhő</span>
      <span v-if="bbt.coverline !== null" class="chip cover">Coverline {{ comma2(bbt.coverline) }} °C</span>
    </div>

    <div class="thead">
      <span class="c-day">Nap</span><span class="c-date">Dátum</span><span class="c-temp">Mérés</span>
      <span class="c-delta">Eltérés</span><span class="c-marks">Jelek</span>
    </div>
    <div v-for="row in bbt.rows" :key="row.date" class="trow" :class="{ above: row.aboveCoverline }">
      <span class="c-day day" :class="{ dim: row.value === null }">{{ row.cycleDay }}</span>
      <span class="c-date date">{{ formatDateShort(row.date) }}</span>
      <span class="c-temp temp" :class="{ dim: row.value === null, above: row.aboveCoverline }">
        {{ row.value === null ? 'nincs mérés' : formatTemp(row.value) }}</span>
      <span class="c-delta delta" :class="{ pos: (row.deltaFromCoverline ?? -1) >= 0 }">
        {{ row.deltaFromCoverline === null ? '—' : formatDelta(row.deltaFromCoverline) }}</span>
      <span class="c-marks marks">{{ marks(row) }}</span>
    </div>

    <div class="flags">
      <span v-if="bbt.excludedOutlierCount > 0" class="chip flag-a">
        {{ bbt.excludedOutlierCount }} kiugró érték kihagyva a coverline-ból</span>
      <span v-if="bbt.missingDayCount > 0" class="chip flag-b">{{ bbt.missingDayCount }} nap kimaradt</span>
    </div>
    <div class="status">
      <template v-if="bbt.ovulationConfirmed">
        Az ovuláció hőemelkedéssel <b>megerősítve</b> — {{ formatDateShort(bbt.confirmedOvulationDate!) }}
      </template>
      <template v-else>
        Az ovuláció hőemelkedéssel <b>még nem erősödött meg</b> — ehhez három egymást követő
        magasabb érték kell a coverline fölött.
      </template>
    </div>
  </div>
</template>

<style scoped>
.head { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.cover { margin-left: auto; color: var(--primary-hover); background: var(--tint); }
.thead { display: flex; padding: 16px 0 9px; font-size: 10.5px; font-weight: 600; color: var(--ink-3); }
.trow { display: flex; align-items: center; padding: 9px 8px; margin: 0 -8px; border-radius: 10px; border-top: 1px solid var(--line); }
.trow.above { background: var(--tint); }
.c-day { width: 30px; flex-shrink: 0; }
.c-date { width: 58px; flex-shrink: 0; }
.c-temp { width: 84px; flex-shrink: 0; }
.c-delta { width: 56px; flex-shrink: 0; text-align: right; }
.c-marks { flex: 1; text-align: right; }
.day { font-size: 12.5px; font-weight: 700; }
.day.dim { color: var(--muted); }
.date { font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.temp { font-size: 13.5px; font-weight: 600; white-space: nowrap; }
.temp.dim { font-weight: 500; color: var(--muted); }
.temp.above { font-weight: 700; color: var(--primary-hover); }
.delta { font-size: 12px; font-weight: 600; color: var(--ink-4); }
.delta.pos { color: var(--primary-hover); }
.marks { font-size: 10.5px; font-weight: 600; color: var(--primary-hover); }
.flags { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 16px; }
.flag-a { font-size: 11.5px; color: var(--primary-hover); background: var(--tint); padding: 7px 12px; }
.flag-b { font-size: 11.5px; color: var(--ink-2); background: #e3e8fb; padding: 7px 12px; }
.status { margin-top: 12px; background: var(--tint); border-radius: 14px; padding: 13px 14px; font-size: 12.5px; color: var(--primary-ink); line-height: 1.55; }
</style>
```

`mensi.client/app/components/trendek/EntriesGridCard.vue`:

```vue
<script setup lang="ts">
import type { DailyLog, Trends } from '~/types/api'
import { MOOD_EMOJI } from '~/utils/labels'

const props = defineProps<{ rows: NonNullable<Trends['bbt']>['rows']; logs: DailyLog[] }>()

const MUCUS_RAMP = ['#f2f6ff', '#dfe9ff', '#c6d6ff', '#aac4ff']
const CRAMP_RAMP = ['#f3f3ff', '#e4e4ff', '#cfd0ff', '#b1b2ff']
const FLOW_RAMP = ['#eeeefb', '#dcddf6', '#c5c6f0', '#adaee9', '#9698e2']
const MUCUS_IDX = { dry: 0, sticky: 1, creamy: 2, eggWhite: 3 } as const
const FLOW_IDX = { none: 0, spotting: 1, light: 2, medium: 3, heavy: 4 } as const

interface Cell { bg: string; fg: string; txt: string }
const OFF: Cell = { bg: 'var(--bg)', fg: 'transparent', txt: '' }

const byDate = computed(() => new Map(props.logs.map(l => [l.date, l])))
const gridRows = computed(() => {
  const defs: { label: string; cell: (log: DailyLog | undefined) => Cell }[] = [
    { label: 'Testhő', cell: l => l?.bbtCelsius != null ? { bg: '#dde1ef', fg: 'transparent', txt: '' } : OFF },
    { label: 'Nyák', cell: l => l?.cervicalMucus ? { bg: MUCUS_RAMP[MUCUS_IDX[l.cervicalMucus]]!, fg: '#1e3566', txt: String(MUCUS_IDX[l.cervicalMucus] + 1) } : OFF },
    { label: 'LH', cell: l => l?.lhTest ? (l.lhTest === 'negative' ? { bg: '#dde1ef', fg: '#464b6b', txt: '–' } : { bg: '#5a5cd6', fg: '#fff', txt: '+' }) : OFF },
    { label: 'Görcs', cell: l => l?.crampSeverity != null && l.crampSeverity > 0 ? { bg: CRAMP_RAMP[l.crampSeverity]!, fg: '#2c2d63', txt: String(l.crampSeverity) } : OFF },
    { label: 'Folyás', cell: l => l?.flowIntensity && l.flowIntensity !== 'none' ? { bg: FLOW_RAMP[FLOW_IDX[l.flowIntensity]]!, fg: '#26265c', txt: String(FLOW_IDX[l.flowIntensity]) } : OFF },
    { label: 'Együttlét', cell: l => l && l.intercourse.length > 0 ? { bg: '#5a5cd6', fg: '#fff', txt: String(l.intercourse.length) } : OFF },
    { label: 'Hangulat', cell: l => l && l.moods.length > 0 ? { bg: 'rgba(90,92,214,.16)', fg: '#3a3c9e', txt: MOOD_EMOJI[l.moods[0]!] } : OFF },
  ]
  return defs.map(def => ({
    label: def.label,
    cells: props.rows.map(r => def.cell(byDate.value.get(r.date))),
  }))
})
</script>

<template>
  <div class="card grid-card">
    <div class="head">
      <span class="section-title">Bejegyzések</span>
      <span class="hint">görgethető →</span>
    </div>
    <div class="scroll noscroll">
      <div class="grid" :style="{ minWidth: `${80 + rows.length * 24}px` }">
        <div class="grid-days">
          <div v-for="r in rows" :key="r.date" class="grid-day"
            :class="{ today: r.cycleDay === rows.length }">{{ r.cycleDay }}</div>
        </div>
        <div v-for="row in gridRows" :key="row.label" class="grid-row">
          <div class="grid-label">{{ row.label }}</div>
          <div class="grid-cells">
            <div v-for="(cell, i) in row.cells" :key="i" class="grid-cell"
              :style="{ background: cell.bg, color: cell.fg }">{{ cell.txt }}</div>
          </div>
        </div>
      </div>
    </div>
    <div class="footnote">Halvány cella = aznap nem volt bejegyzés. A telítettség az intenzitást jelöli.</div>
  </div>
</template>

<style scoped>
.grid-card { padding-left: 0; padding-right: 0; }
.head { padding: 0 18px; display: flex; align-items: baseline; }
.hint { margin-left: auto; font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.scroll { overflow-x: auto; margin-top: 14px; padding: 0 18px; }
.grid-days { display: flex; gap: 2px; padding-left: 80px; margin-bottom: 6px; }
.grid-day { flex: 1; min-width: 20px; text-align: center; font-size: 9px; font-weight: 600; color: var(--ink-4); }
.grid-day.today { color: var(--primary); }
.grid-row { display: flex; align-items: center; margin-bottom: 4px; }
.grid-label { width: 80px; flex-shrink: 0; font-size: 11px; font-weight: 600; color: var(--ink-2); }
.grid-cells { flex: 1; display: flex; gap: 2px; }
.grid-cell {
  flex: 1; min-width: 20px; height: 20px; border-radius: 6px; display: grid; place-items: center;
  font-size: 8.5px; font-weight: 700;
}
.footnote { padding: 14px 18px 0; font-size: 11.5px; color: var(--ink-3); line-height: 1.5; }
</style>
```

- [ ] **Step 2: Type-check + kézi próba, majd commit**

Run: `cd mensi.client && npm run type-check`
Expected: zöld; dev szerveren a három kártya rendben renderel seedelt adattal.

```bash
git add mensi.client
git commit -m "feat: Trendek nézet (statisztika, BBT-táblázat, bejegyzés-hőtérkép)"
```

---

### Task 20: Bejegyzések nézet (havi naptár + kiválasztott nap)

**Files:**
- Create: `mensi.client/app/pages/bejegyzesek.vue`

**Interfaces:**
- Consumes: `useApi().calendar`, `useApi().log`, `store.openSheet`, `CAL_COLORS`, `fieldValue`, `monthTitle`
- Vizuális referencia: prototípus `isNaptar` blokk

- [ ] **Step 1: Oldal**

`mensi.client/app/pages/bejegyzesek.vue`:

```vue
<script setup lang="ts">
import type { CalendarMonth, DailyLog } from '~/types/api'
import { CAL_COLORS, FIELD_LABELS, FIELD_ORDER } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'
import { monthTitle } from '~/utils/format'

const store = useAppStore()
const api = useApi()

const current = ref<CalendarMonth | null>(null)
const selectedDate = ref<string | null>(null)
const selectedLog = ref<DailyLog | null>(null)
const month = ref('') // "2026-08"

function ym(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

async function loadMonth(value: string) {
  const [y, m] = value.split('-').map(Number)
  current.value = await api.calendar(y!, m!)
  month.value = value
}

async function select(date: string) {
  selectedDate.value = date
  selectedLog.value = await api.log(date)
}

onMounted(async () => {
  const today = new Date()
  await loadMonth(ym(today))
  const iso = current.value!.days.find(d => d.isToday)?.date
  if (iso) await select(iso)
})
watch(() => store.refreshTick, async () => {
  if (month.value) await loadMonth(month.value)
  if (selectedDate.value) await select(selectedDate.value)
})

const monthOptions = computed(() => {
  if (!current.value) return []
  const options: string[] = []
  const [fy, fm] = current.value.range.firstMonth.split('-').map(Number)
  const [ly, lm] = current.value.range.lastMonth.split('-').map(Number)
  const cursor = new Date(fy!, fm! - 1, 1)
  const last = new Date(ly!, lm! - 1, 1)
  while (cursor <= last) { options.push(ym(cursor)); cursor.setMonth(cursor.getMonth() + 1) }
  return options
})
const canPrev = computed(() => monthOptions.value.indexOf(month.value) > 0)
const canNext = computed(() => {
  const i = monthOptions.value.indexOf(month.value)
  return i >= 0 && i < monthOptions.value.length - 1
})
function shift(delta: number) {
  const i = monthOptions.value.indexOf(month.value)
  const next = monthOptions.value[i + delta]
  if (next) void loadMonth(next)
}

const WEEK_HEADS = ['H', 'K', 'Sz', 'Cs', 'P', 'Szo', 'V']
const leadingBlanks = computed(() => {
  if (!current.value) return 0
  const first = new Date(`${current.value.days[0]!.date}T00:00:00`)
  return (first.getDay() + 6) % 7
})
const LEGEND = [
  { label: 'Menstruáció', bg: CAL_COLORS.menstruation.bg },
  { label: 'Termékeny', bg: CAL_COLORS.fertile.bg },
  { label: 'Ovulációs ablak', bg: CAL_COLORS.ovulation.bg },
  { label: 'Luteális', bg: CAL_COLORS.luteal.bg },
  { label: 'Ma', bg: 'var(--primary)' },
]

const selRows = computed(() => FIELD_ORDER.map((key, i) => ({
  key, i, label: FIELD_LABELS[key], value: fieldValue(selectedLog.value, key),
})))
const selHasAny = computed(() => selRows.value.some(r => r.value !== null))
const selectedDay = computed(() =>
  current.value?.days.find(d => d.date === selectedDate.value) ?? null)
const isFutureSelected = computed(() => {
  const today = store.overview?.today
  return !!(today && selectedDate.value && selectedDate.value > today)
})
const dayNum = (iso: string) => Number(iso.slice(8))
</script>

<template>
  <div v-if="current" class="stack">
    <div class="card">
      <div class="nav">
        <button class="nav-btn" :disabled="!canPrev" aria-label="Előző hónap" @click="shift(-1)">‹</button>
        <select class="nav-select" :value="month" @change="loadMonth(($event.target as HTMLSelectElement).value)">
          <option v-for="option in monthOptions" :key="option" :value="option">{{ monthTitle(option) }}</option>
        </select>
        <button class="nav-btn" :disabled="!canNext" aria-label="Következő hónap" @click="shift(1)">›</button>
      </div>
      <div v-if="current.cycleDayOfToday" class="nav-sub">ciklus {{ current.cycleDayOfToday }}. nap</div>
      <div v-else-if="!current.hasData" class="nav-sub dim">Ehhez a hónaphoz még nincs rögzített adat</div>

      <div class="grid">
        <div v-for="w in WEEK_HEADS" :key="w" class="weekhead">{{ w }}</div>
        <div v-for="i in leadingBlanks" :key="`blank-${i}`" />
        <button v-for="day in current.days" :key="day.date" class="cell" :style="{
          background: day.date === selectedDate ? 'var(--primary)' : CAL_COLORS[day.category].bg,
          color: day.date === selectedDate ? '#ffffff' : CAL_COLORS[day.category].fg,
          boxShadow: day.isToday && day.date !== selectedDate ? 'inset 0 0 0 2px var(--primary)' : 'none',
        }" @click="select(day.date)">
          <span class="cell-num" :class="{ bold: day.isToday || day.date === selectedDate }">{{ dayNum(day.date) }}</span>
          <span class="cell-dots">
            <span v-if="day.hasBbt" class="cell-dot" :style="{ background: day.date === selectedDate ? '#fff' : '#7c82a6' }" />
            <span v-if="day.intercourseCount > 0" class="cell-dot" :style="{ background: day.date === selectedDate ? '#fff' : 'var(--primary)' }" />
          </span>
        </button>
      </div>

      <div class="legend">
        <div v-for="item in LEGEND" :key="item.label" class="legend-item">
          <span class="legend-dot" :style="{ background: item.bg }" />
          <span>{{ item.label }}</span>
        </div>
      </div>
    </div>

    <div v-if="selectedDate" class="card">
      <div class="sel-head">
        <span class="section-title">{{ monthTitle(selectedDate.slice(0, 7)).split('. ')[1] }} {{ dayNum(selectedDate) }}.</span>
        <span class="chip sel-chip">{{ selectedDay?.cycleDay ? `${selectedDay.cycleDay}. ciklusnap` : 'cikluson kívül' }}</span>
      </div>
      <div v-if="isFutureSelected" class="sel-empty">Ez a nap még előttünk áll — bejegyzés majd aznap rögzíthető.</div>
      <div v-else-if="!selHasAny" class="sel-empty">Ezen a napon nincs bejegyzés.
        <button class="sel-add" @click="store.openSheet(selectedDate, 0, false)">Rögzítés</button>
      </div>
      <div v-else class="sel-rows">
        <button v-for="row in selRows" :key="row.key" class="sel-row" :class="{ set: row.value !== null }"
          @click="store.openSheet(selectedDate, row.i, true)">
          <span class="sel-label">{{ row.label }}</span>
          <span class="sel-value" :class="{ set: row.value !== null }">{{ row.value ?? 'nincs adat' }}</span>
          <span class="sel-action">{{ row.value !== null ? 'módosítás' : 'rögzítés' }}</span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.nav { display: flex; align-items: center; gap: 8px; }
.nav-btn {
  width: 34px; height: 34px; flex-shrink: 0; border-radius: 10px; border: 0; background: #f5f7fe;
  color: var(--ink-2); font: 700 15px 'Montserrat', sans-serif; cursor: pointer;
}
.nav-btn:disabled { opacity: .4; cursor: default; }
.nav-btn:not(:disabled):hover { background: var(--tint); }
.nav-select {
  flex: 1; min-width: 0; text-align: center; font: 700 13px 'Montserrat', sans-serif; color: var(--ink);
  border: 0; background: #f5f7fe; border-radius: 10px; padding: 9px 4px; cursor: pointer;
}
.nav-sub { text-align: center; margin-top: 8px; font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.nav-sub.dim { color: var(--muted); }
.grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 5px; margin: 16px auto 0; max-width: 440px; }
.weekhead { text-align: center; font-size: 10.5px; font-weight: 600; color: var(--ink-3); padding-bottom: 4px; }
.cell {
  aspect-ratio: 1; border-radius: 12px; border: 0; cursor: pointer; font-family: inherit;
  display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px;
}
.cell-num { font-size: 12.5px; font-weight: 500; }
.cell-num.bold { font-weight: 700; }
.cell-dots { display: flex; gap: 3px; height: 4px; }
.cell-dot { width: 4px; height: 4px; border-radius: 99px; }
.legend { display: flex; flex-wrap: wrap; gap: 13px; margin-top: 18px; }
.legend-item { display: flex; align-items: center; gap: 7px; font-size: 11.5px; font-weight: 500; color: var(--ink-2); }
.legend-dot { width: 10px; height: 10px; border-radius: 3px; }
.sel-head { display: flex; align-items: baseline; }
.sel-chip { margin-left: auto; color: var(--primary); background: var(--tint); font-size: 11.5px; }
.sel-empty { margin-top: 14px; padding: 26px 14px; border-radius: 14px; background: #f5f7fe; text-align: center; font-size: 13px; color: var(--ink-2); }
.sel-add { display: block; margin: 12px auto 0; border: 0; background: var(--tint); color: var(--primary-deep); font: 700 12px 'Montserrat', sans-serif; border-radius: 99px; padding: 9px 18px; cursor: pointer; }
.sel-rows { display: flex; flex-direction: column; gap: 2px; margin-top: 12px; }
.sel-row {
  display: flex; align-items: center; padding: 12px 14px; border-radius: 12px; border: 0;
  background: transparent; cursor: pointer; font-family: inherit; text-align: left;
}
.sel-row.set { background: #f5f7fe; }
.sel-row:hover { background: var(--tint); }
.sel-label { font-size: 12.5px; font-weight: 500; color: var(--ink-2); width: 96px; flex-shrink: 0; }
.sel-value { font-size: 14px; font-weight: 500; color: var(--ink-4); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.sel-value.set { font-weight: 600; color: var(--ink); }
.sel-action { margin-left: auto; flex-shrink: 0; padding-left: 8px; font-size: 11px; font-weight: 700; color: var(--primary); }
</style>
```

- [ ] **Step 2: Type-check + kézi próba, majd commit**

Run: `cd mensi.client && npm run type-check`
Expected: zöld; naptárban hónapléptetés, nap-kiválasztás, sorból egymezős sheet nyílik, jövőbeli napra nincs szerkesztés.

```bash
git add mensi.client
git commit -m "feat: Bejegyzések nézet (havi naptár fázisszínekkel + napi részletek)"
```

---

### Task 21: Esély nézet

**Files:**
- Create: `mensi.client/app/pages/esely.vue`

**Interfaces:**
- Consumes: `useApi().chance`, `TIMING_LABELS`, `formatPercent`, `formatDateShort`
- Vizuális referencia: prototípus `isEsely` blokk

- [ ] **Step 1: Oldal**

`mensi.client/app/pages/esely.vue`:

```vue
<script setup lang="ts">
import type { Chance } from '~/types/api'
import { TIMING_LABELS } from '~/utils/labels'
import { formatDateShort, formatPercent } from '~/utils/format'

const store = useAppStore()
const api = useApi()
const chance = ref<Chance | null>(null)
watch(() => store.refreshTick, async () => { chance.value = await api.chance() }, { immediate: true })

const METHOD_NOTES = [
  'Az ovulációs ablak a lezárt ciklusok hosszából és luteális fázisából jön, az LH-teszttel és a nyákkal korrigálva.',
  'A százalék a Wilcox-féle, ovuláció-relatív napi valószínűségekből számított becslés — nem orvosi termékenységi vizsgálat.',
  'A hiányzó napokat nem pótolja becsléssel: ahol nincs adat, ott „nincs bejegyzés” szerepel.',
  'Nem veszi figyelembe az életkort, spermaminőséget, gyógyszereket és semmilyen orvosi tényezőt.',
]
const short = (iso: string) => formatDateShort(iso).replace(' ', '')
const barStyle = (label: 'weak' | 'medium' | 'good') => ({
  width: label === 'good' ? '100%' : label === 'medium' ? '62%' : '30%',
  background: label === 'good' ? '#2f3170' : label === 'medium' ? 'var(--primary)' : '#a8adc7',
})
</script>

<template>
  <div v-if="chance" class="stack">
    <div v-if="chance.isEmpty" class="card empty">Az esély-számításhoz legalább egy lezárt ciklus kell.</div>
    <template v-else>
      <div class="card">
        <div class="label">Időzítés ebben a ciklusban</div>
        <div class="big">{{ TIMING_LABELS[chance.timing!.label] }}</div>
        <div class="percent">becsült esély ebben a ciklusban: <b>{{ formatPercent(chance.timing!.chancePercent) }}</b></div>
        <div class="body">{{ chance.explanation }}</div>
        <div class="note">{{ chance.confidenceNote }}</div>
      </div>

      <div class="card">
        <div class="section-title">Termékeny ablak napjai</div>
        <div class="days">
          <div v-for="d in chance.fertileWindow!.days" :key="d.date" class="day-col">
            <div class="day-box" :style="{
              background: d.intercourseCount > 0 ? 'var(--primary)' : d.isFuture ? '#f5f7fe' : '#e3e8fb',
              color: d.intercourseCount > 0 ? '#fff' : d.isFuture ? '#b8bedb' : '#3f4f9c',
              boxShadow: d.isToday ? 'inset 0 0 0 2px #21243d' : 'none',
            }">
              <span class="day-num">{{ d.cycleDay }}</span>
              <span v-if="d.intercourseCount > 0" class="day-count">{{ d.intercourseCount }}×</span>
            </div>
            <span class="day-date">{{ short(d.date) }}</span>
          </div>
        </div>
        <div class="legend">
          <span class="legend-item"><span class="dot" style="background:#5a5cd6" />Volt együttlét</span>
          <span class="legend-item"><span class="dot" style="background:#e3e8fb" />Nincs bejegyzés</span>
          <span class="legend-item"><span class="dot" style="background:#f5f7fe" />Még hátra van</span>
        </div>
      </div>

      <div class="cols">
        <div class="remaining">
          <div class="rem-title">A hátralévő ablak</div>
          <div class="rem-big">
            <span class="rem-num">{{ chance.fertileWindow!.daysRemaining }}</span>
            <span class="rem-unit">nap van hátra</span>
          </div>
          <div class="rem-dots">
            <div v-for="i in chance.fertileWindow!.ovulationWindowTotal" :key="i" class="rem-dot"
              :class="{ done: i <= chance.fertileWindow!.ovulationWindowElapsed }" />
          </div>
          <div v-if="chance.whatIfHint" class="rem-hint">{{ chance.whatIfHint }}</div>
        </div>

        <div class="card">
          <div class="hist-head">
            <span class="section-title">Korábbi ciklusok</span>
            <span class="chip hist-chip">{{ chance.history!.goodCount }} jó a {{ chance.history!.totalCount }}-ból</span>
          </div>
          <div class="hist-rows">
            <div v-for="c in chance.history!.cycles" :key="c.startDate" class="hist-row">
              <span class="hist-label">{{ formatDateShort(c.startDate).split(' ')[0] }}</span>
              <div class="hist-track"><div class="hist-bar" :style="barStyle(c.timing.label)" /></div>
              <span class="hist-timing" :style="{ color: barStyle(c.timing.label).background }">
                {{ TIMING_LABELS[c.timing.label] }}</span>
            </div>
          </div>
        </div>
      </div>

      <div class="method">
        <div class="method-title">Módszertan</div>
        <div class="method-notes">
          <div v-for="(m, i) in METHOD_NOTES" :key="i" class="method-note">
            <span class="method-dot" /><span>{{ m }}</span>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.empty { font-size: 13px; color: var(--ink-2); }
.label { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.big { font-size: 36px; font-weight: 700; margin-top: 6px; letter-spacing: -.03em; color: var(--primary-hover); }
.percent { font-size: 13.5px; color: var(--ink); margin-top: 4px; }
.body { font-size: 13.5px; color: var(--ink-2); line-height: 1.6; margin-top: 9px; }
.note { font-size: 11.5px; color: var(--muted); line-height: 1.5; margin-top: 9px; }
.days { display: flex; gap: 6px; margin-top: 16px; }
.day-col { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 6px; }
.day-box {
  width: 100%; aspect-ratio: 1; border-radius: 12px; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 2px;
}
.day-num { font-size: 13px; font-weight: 700; }
.day-count { font-size: 9px; font-weight: 700; }
.day-date { font-size: 8.5px; font-weight: 600; color: var(--muted); }
.legend { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 16px; }
.legend-item { display: flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 500; color: var(--ink-2); }
.dot { width: 9px; height: 9px; border-radius: 3px; display: inline-block; }
.cols { display: flex; flex-direction: column; gap: 14px; }
@media (min-width: 700px) { .cols { display: grid; grid-template-columns: 1fr 1fr; } }
.remaining { background: var(--primary); border-radius: 20px; padding: 20px 18px; box-shadow: 0 6px 22px rgba(90,92,214,.22); }
.rem-title { font-size: 13px; font-weight: 700; color: #fff; }
.rem-big { display: flex; align-items: baseline; gap: 8px; margin-top: 10px; }
.rem-num { font-size: 34px; font-weight: 700; color: #fff; letter-spacing: -.03em; }
.rem-unit { font-size: 14px; font-weight: 600; color: #d2daff; }
.rem-dots { display: flex; gap: 4px; margin-top: 14px; }
.rem-dot { flex: 1; height: 8px; border-radius: 99px; background: #fff; }
.rem-dot.done { background: rgba(255,255,255,.35); }
.rem-hint { margin-top: 14px; background: rgba(255,255,255,.14); border-radius: 14px; padding: 13px 14px; font-size: 12.5px; color: #eceeff; line-height: 1.55; }
.hist-head { display: flex; align-items: baseline; }
.hist-chip { margin-left: auto; color: var(--primary-hover); background: var(--tint); font-size: 11px; font-weight: 600; }
.hist-rows { display: flex; flex-direction: column; gap: 8px; margin-top: 14px; }
.hist-row { display: flex; align-items: center; gap: 10px; }
.hist-label { width: 44px; flex-shrink: 0; font-size: 11.5px; font-weight: 600; color: var(--ink-3); }
.hist-track { flex: 1; height: 8px; border-radius: 99px; background: var(--tint); overflow: hidden; }
.hist-bar { height: 100%; border-radius: 99px; }
.hist-timing { width: 52px; flex-shrink: 0; text-align: right; font-size: 11px; font-weight: 700; }
.method { background: var(--tint); border-radius: 20px; padding: 20px 18px; }
.method-title { font-size: 13px; font-weight: 700; color: var(--primary); }
.method-notes { display: flex; flex-direction: column; gap: 10px; margin-top: 12px; }
.method-note { display: flex; gap: 10px; align-items: flex-start; font-size: 12.5px; color: var(--primary-ink); line-height: 1.55; }
.method-dot { width: 6px; height: 6px; border-radius: 99px; background: var(--primary); margin-top: 7px; flex-shrink: 0; }
</style>
```

- [ ] **Step 2: Type-check + teljes frontend teszt, majd commit**

Run: `cd mensi.client && npm run type-check && npm run test`
Expected: zöld.

```bash
git add mensi.client
git commit -m "feat: Esély nézet (minősítés + %, ablak, mit-ha, történet, módszertan)"
```

---

### Task 22: Dockerfile + docker-compose + .env.example + RUNBOOK

**Files:**
- Create: `Mensi.Server/Dockerfile`, `docker-compose.yml`, `docker-compose.override.yml`, `.env.example`, `deploy/RUNBOOK.md`

**Interfaces:**
- Consumes: a Task 15 env-kulcsai (`ConnectionStrings__Default`, `CloudflareAccess__TeamDomain`, `CloudflareAccess__Audience`, `Audit__RetentionDays`, `Display__TimeZone`)
- Produces: buildelhető image, futtatható stack a `127.0.0.1:8100`-on

- [ ] **Step 1: Dockerfile (multi-stage: Nuxt generate → dotnet publish → runtime)**

`Mensi.Server/Dockerfile` (a repo gyökeréből buildelve, `context: .`):

```dockerfile
# 1) Kliens: Nuxt statikus generálás
FROM node:22-alpine AS client
WORKDIR /src/mensi.client
COPY mensi.client/package.json mensi.client/package-lock.json ./
RUN npm ci
COPY mensi.client/ ./
RUN npm run generate

# 2) Backend publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Mensi.Core/Mensi.Core.csproj Mensi.Core/
COPY Mensi.Core/packages.lock.json Mensi.Core/
COPY Mensi.Server/Mensi.Server.csproj Mensi.Server/
COPY Mensi.Server/packages.lock.json Mensi.Server/
RUN dotnet restore Mensi.Server/Mensi.Server.csproj --locked-mode
COPY Mensi.Core/ Mensi.Core/
COPY Mensi.Server/ Mensi.Server/
RUN dotnet publish Mensi.Server/Mensi.Server.csproj -c Release -o /app --no-restore

# 3) Futtató image: aspnet + statikus kliens a wwwroot-ban
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
COPY --from=client /src/mensi.client/.output/public wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mensi.Server.dll"]
```

- [ ] **Step 2: docker-compose.yml**

A PortfolioCMS mintája (log-rotáció, internal backend háló, loopback port, healthcheck), kisebb limitekkel:

```yaml
# Log-rotáció minden szolgáltatásra: a json-file driver különben korlátlanul nő.
x-logging: &logging
  driver: json-file
  options:
    max-size: "10m"
    max-file: "3"

services:
  db:
    image: postgres:17-alpine
    restart: unless-stopped
    networks:
      - backend
    # Kis adatmennyiséghez (évi ~365 sor) szabott, memórialimit-arányos beállítások.
    command:
      - postgres
      - -c
      - shared_buffers=64MB
      - -c
      - effective_cache_size=192MB
      - -c
      - work_mem=4MB
      - -c
      - maintenance_work_mem=32MB
    logging: *logging
    deploy:
      resources:
        limits:
          memory: 256M
          cpus: "0.5"
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-mensi}
      POSTGRES_USER: ${POSTGRES_USER:-mensi}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER:-mensi} -d $${POSTGRES_DB:-mensi}"]
      interval: 5s
      timeout: 3s
      retries: 10

  app:
    image: ${APP_IMAGE:-mensi:local}
    build:
      context: .
      dockerfile: Mensi.Server/Dockerfile
    restart: unless-stopped
    networks:
      - backend
      - frontend
    # Loopback only: kifelé a cloudflared tunnel route visz, előtte a CF Access alkalmazással.
    # A VPS IP-jére küldött közvetlen kérés így el sem éri a konténert.
    ports:
      - "127.0.0.1:8100:8080"
    logging: *logging
    deploy:
      resources:
        limits:
          memory: 256M
          cpus: "1.0"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_HTTP_PORTS: "8080"
      ConnectionStrings__Default: "Host=db;Port=5432;Database=${POSTGRES_DB:-mensi};Username=${POSTGRES_USER:-mensi};Password=${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env}"
      # Az Access alkalmazás, aminek az assertionjét elfogadjuk. Nélküle a host el sem indul.
      CloudflareAccess__TeamDomain: ${CF_ACCESS_TEAM_DOMAIN:?set CF_ACCESS_TEAM_DOMAIN in .env}
      CloudflareAccess__Audience: ${CF_ACCESS_AUD:?set CF_ACCESS_AUD in .env}
      Audit__RetentionDays: ${AUDIT_RETENTION_DAYS:-365}
      Display__TimeZone: ${DISPLAY_TIMEZONE:-Europe/Budapest}
    healthcheck:
      # Az image-ben nincs curl/wget, ezért bash /dev/tcp. A /health az Access-ellenőrzés
      # előtt van, különben a konténer belülről nem tudná megnézni magát.
      test:
        [
          "CMD",
          "bash",
          "-c",
          'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health HTTP/1.0\r\n\r\n" >&3 && grep -q "200 OK" <&3',
        ]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 20s
    depends_on:
      db:
        condition: service_healthy

networks:
  # A db csak ezen a hálón él; internal → se a hoszt, se az internet felől nincs útvonal.
  # A db-re SOHA ne kerüljön ports: bejegyzés.
  backend:
    internal: true
  frontend:

volumes:
  pgdata:
```

- [ ] **Step 3: docker-compose.override.yml + .env.example**

`docker-compose.override.yml` (csak dev; a szerveren `docker compose -f docker-compose.yml up -d`):

```yaml
# Development-only overrides — docker compose automatikusan betölti.
# A db portját publikálja localhostra, hogy a helyi `dotnet run` elérje.
services:
  db:
    ports:
      - "127.0.0.1:5432:5432"
```

`.env.example`:

```bash
# Postgres
POSTGRES_DB=mensi
POSTGRES_USER=mensi
POSTGRES_PASSWORD=

# Cloudflare Access (Zero Trust dashboard: team domain + az alkalmazás AUD tagje)
CF_ACCESS_TEAM_DOMAIN=https://<team>.cloudflareaccess.com
CF_ACCESS_AUD=

# Opcionális
AUDIT_RETENTION_DAYS=365
DISPLAY_TIMEZONE=Europe/Budapest
APP_IMAGE=mensi:local
```

- [ ] **Step 4: deploy/RUNBOOK.md**

Tartalma (teljes szöveggel írd meg, ez a váz kötelező szakaszokkal):

```markdown
# Mensi — üzemeltetési runbook

## 1. Előfeltételek
- VPS docker + docker compose pluginnal, cloudflared tunnel már fut (PortfolioCMS minta)
- Cloudflare Zero Trust hozzáférés

## 2. Cloudflare Tunnel route
- A meglévő tunnel configba új ingress: `mensi.<domain>` → `http://localhost:8100`
- `cloudflared tunnel route dns <tunnel> mensi.<domain>`

## 3. Cloudflare Access alkalmazás
- Zero Trust → Access → Applications → Add self-hosted
- Domain: `mensi.<domain>`; Session: 24h
- Policy (Allow): Include → Emails: <saját email>, <feleség email>
- Require → Authentication method: hardware key vagy WARP/biometric MFA — ugyanaz a
  minta, mint az ssh.<domain>-nél
- Az app Overview oldaláról az Audience (AUD) tag → `.env` `CF_ACCESS_AUD`
- Team domain (`https://<team>.cloudflareaccess.com`) → `CF_ACCESS_TEAM_DOMAIN`

## 4. Első indítás
- repo klón, `.env` kitöltés a `.env.example` alapján
- `docker compose -f docker-compose.yml build && docker compose -f docker-compose.yml up -d`
- migráció automatikus indul; ellenőrzés: `docker compose ps` (healthy),
  `curl -s http://127.0.0.1:8100/health` → OK
- böngészőből `https://mensi.<domain>` → Access login → app

## 5. Frissítés
- `git pull && docker compose -f docker-compose.yml build && docker compose -f docker-compose.yml up -d`

## 6. Mentés és visszaállítás
- napi pg_dump cron:
  `docker compose exec -T db pg_dump -U mensi mensi | gzip > /backup/mensi-$(date +%F).sql.gz`
- retention a backup mappán (pl. 30 nap), és időnkénti restore-próba:
  `gunzip -c mensi-<date>.sql.gz | docker compose exec -T db psql -U mensi mensi`

## 7. Adatvédelem
- Ez a stack legérzékenyebb adatkategóriája (egészségügyi + szexuális adat, GDPR 9. cikk):
  a VPS diszkjén ajánlott LUKS/kötettitkosítás, a backup célja is titkosított legyen
- Az edge auditot (ki, mikor lépett be) a Zero Trust → Logs → Access adja;
  az alkalmazásszintű módosítás-audit a Postgres `audit_log` táblájában van

## 8. Logok
- `docker compose logs -f app` — Serilog request + app log (rotáció: 3×10 MB)
- adatváltozások: `audit_log` tábla (ki, mikor, mit; retention env-ből)
```

- [ ] **Step 5: Build + stack smoke-teszt**

Run:
```bash
docker compose build
POSTGRES_PASSWORD=test CF_ACCESS_TEAM_DOMAIN=https://x.cloudflareaccess.com CF_ACCESS_AUD=test docker compose up -d
sleep 25 && curl -s http://127.0.0.1:8100/health
docker compose ps
```
Expected: `OK` a curl-től, mindkét konténer `healthy`. (Az Access most minden mást 403-mal dob — ez a helyes viselkedés: a JWT-t csak a valós edge tudja adni.) Utána: `docker compose down -v`.

- [ ] **Step 6: Commit**

```bash
git add Mensi.Server/Dockerfile docker-compose.yml docker-compose.override.yml .env.example deploy/RUNBOOK.md
git commit -m "feat: docker deploy (compose, multi-stage image, runbook)"
```

---

### Task 23: Végső verifikáció

**Files:** nincs új fájl — ellenőrző futtatások + esetleges javítások.

- [ ] **Step 1: Teljes backend tesztsor**

Run: `dotnet test`
Expected: minden zöld (unit + integrációs).

- [ ] **Step 2: Teljes frontend ellenőrzés**

Run: `cd mensi.client && npm run type-check && npm run test && npm run generate`
Expected: zöld, a `.output/public` létrejön `index.html`-lel.

- [ ] **Step 3: End-to-end kézi smoke dev módban**

- `docker compose up -d db` (override publikálja az 5432-t)
- `dotnet run --project Mensi.Server` (Development: Access kikapcsolva, warning a logban)
- `cd mensi.client && npm run dev` → http://localhost:3000
- Végigjátszás: empty state → első bejegyzés (periodStart) → pár nap adat → naptár/trendek/esély nézetek
- A spec 6. fejezetének nézetlistája alapján pipálj: Ma, Trendek, Bejegyzések, Esély, sheet, toast+undo, empty state

**Ha bármelyik nézet eltér a prototípustól** (`docs/design/mensi-care-prototipus-kicsomagolt.html` böngészőben), igazítsd a komponenst — a prototípus a vizuális igazság forrása.

- [ ] **Step 4: Spec-lefedettség ellenőrzés**

A spec (`docs/superpowers/specs/2026-08-24-mensi-design.md`) szakaszain végigmenve győződj meg róla, hogy mindegyikhez van implementáció (3. adatmodell → Task 3; 4.1–4.7 → Task 5–12; 5. API → Task 14–15; 6. frontend → Task 16–21; 7. auth → Task 2+15; 8. logging → Task 13+15+22; 9. deploy → Task 22; 10. tesztek → végig). Hiányt itt pótolj.

- [ ] **Step 5: Záró commit (ha volt javítás)**

```bash
git add -A
git commit -m "fix: végső verifikáció utáni igazítások"
```
