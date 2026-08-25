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
            e.Property(x => x.LhValue).HasColumnName("lh_value").HasColumnType("numeric(3,2)");
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
