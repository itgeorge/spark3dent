using Microsoft.EntityFrameworkCore;
using Orders;

namespace Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Entities.InvoiceEntity> Invoices { get; set; }
    public DbSet<Entities.InvoiceLineItemEntity> InvoiceLineItems { get; set; }
    public DbSet<Entities.ClientEntity> Clients { get; set; }
    public DbSet<Entities.InvoiceSequenceEntity> InvoiceSequence { get; set; }
    public DbSet<Entities.SchedulingLabEntity> SchedulingLabs { get; set; }
    public DbSet<Entities.SchedulingClinicEntity> SchedulingClinics { get; set; }
    public DbSet<Entities.SchedulingMemberEntity> SchedulingMembers { get; set; }
    public DbSet<Entities.SchedulingAuthSessionEntity> SchedulingAuthSessions { get; set; }
    public DbSet<Entities.SchedulingOrderEntity> SchedulingOrders { get; set; }
    public DbSet<Entities.SchedulingReservationEntity> SchedulingReservations { get; set; }
    public DbSet<Entities.SchedulingMaterialConfigEntity> SchedulingMaterialConfigs { get; set; }
    public DbSet<Entities.SchedulingCapacityConfigEntity> SchedulingCapacityConfigs { get; set; }
    public DbSet<Entities.SchedulingLabOffdayEntity> SchedulingLabOffdays { get; set; }
    public DbSet<Entities.SchedulingDeadlineRecommendationLogEntity> SchedulingDeadlineRecommendationLogs { get; set; }
    public DbSet<Entities.SchedulingDeadlineOverrideLogEntity> SchedulingDeadlineOverrideLogs { get; set; }
    public DbSet<Entities.AuditEventEntity> AuditEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entities.InvoiceEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.NumberNumeric).IsUnique();
            e.HasMany(x => x.LineItems)
                .WithOne(x => x.Invoice)
                .HasForeignKey(x => x.InvoiceEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Entities.InvoiceLineItemEntity>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // TODO: now that we support renaming clients, we should probably change the key to be the company identifier
        modelBuilder.Entity<Entities.ClientEntity>(e =>
        {
            e.HasKey(x => x.Nickname);
        });

        modelBuilder.Entity<Entities.InvoiceSequenceEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.ToTable("InvoiceSequence", t =>
                t.HasCheckConstraint("CK_InvoiceSequence_Id", "Id = 1"));
        });

        modelBuilder.Entity<Entities.SchedulingLabEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).IsRequired();
            e.Property(x => x.DisplayName).IsRequired();
            e.ToTable("SchedulingLabs", t =>
                t.HasCheckConstraint("CK_SchedulingLabs_Singleton", "Id = 1"));
        });

        modelBuilder.Entity<Entities.SchedulingClinicEntity>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).IsRequired();
            e.Property(x => x.DisplayName).IsRequired();
        });

        modelBuilder.Entity<Entities.SchedulingMemberEntity>(e =>
        {
            e.HasKey(x => new { x.OrganizationType, x.OrganizationCode, x.Id });
            e.Property(x => x.OrganizationType).HasConversion<string>();
            e.Property(x => x.OrganizationCode).IsRequired();
            e.Property(x => x.Id).IsRequired();
            e.Property(x => x.Label).IsRequired();
            e.Property(x => x.PinHash).IsRequired();
            e.HasIndex(x => new { x.OrganizationType, x.OrganizationCode, x.IsActive });
        });

        modelBuilder.Entity<Entities.SchedulingAuthSessionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.OrganizationType, x.OrganizationCode, x.MemberId });
            e.Property(x => x.TokenHash).IsRequired();
            e.Property(x => x.OrganizationType).HasConversion<string>().IsRequired();
            e.Property(x => x.OrganizationCode).HasColumnName("ClinicCode").IsRequired();
            e.Property(x => x.MemberId).HasColumnName("CredentialId").IsRequired();
        });

        modelBuilder.Entity<Entities.SchedulingMaterialConfigEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Material, x.ActiveFromDate }).IsUnique();
            e.HasIndex(x => x.Material);
            e.Property(x => x.Material).HasConversion<string>().IsRequired();
            e.Property(x => x.ActiveFromDate).IsRequired();
            e.Property(x => x.FixedLeadTimeBusinessDays).IsRequired();
            e.Property(x => x.CapacityUnitsPerTooth).HasColumnType("TEXT").IsRequired();
        });

        modelBuilder.Entity<Entities.SchedulingOrderEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderCode).IsUnique();
            e.HasIndex(x => x.ClinicCode);
            e.HasIndex(x => x.RequestedDeliveryDate);
            e.HasIndex(x => x.CreatedAtUnixTimeMilliseconds);
            e.HasIndex(x => x.Status);
            e.Property(x => x.OrderCode).IsRequired();
            e.Property(x => x.ClinicCode).IsRequired();
            e.Property(x => x.MemberId).HasColumnName("CredentialId").IsRequired();
            e.Property(x => x.MemberLabel).HasColumnName("CredentialLabel").IsRequired();
            e.Property(x => x.MemberPinHashFingerprint).HasColumnName("CredentialPinHashFingerprint").IsRequired();
            e.Property(x => x.WorkItemsJson).IsRequired();
            e.Property(x => x.Shade).HasConversion<int>();
            e.Property(x => x.CalculatedCapacityUnits).HasColumnType("TEXT");
        });

        modelBuilder.Entity<Entities.SchedulingReservationEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ClinicCode);
            e.HasIndex(x => x.RequestedDeliveryDate);
            e.HasIndex(x => x.ImpressionDate);
            e.HasIndex(x => x.CreatedAtUnixTimeMilliseconds);
            e.HasIndex(x => x.Status);
            e.Property(x => x.ClinicCode).IsRequired();
            e.Property(x => x.MemberId).HasColumnName("CredentialId").IsRequired();
            e.Property(x => x.MemberLabel).HasColumnName("CredentialLabel").IsRequired();
            e.Property(x => x.MemberPinHashFingerprint).HasColumnName("CredentialPinHashFingerprint").IsRequired();
            e.Property(x => x.WorkItemsJson).IsRequired();
            e.Property(x => x.Shade).HasConversion<int>();
            e.Property(x => x.CalculatedCapacityUnits).HasColumnType("TEXT");
        });

        modelBuilder.Entity<Entities.SchedulingCapacityConfigEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ActiveFromDate).IsUnique();
            e.Property(x => x.DailyCapacityUnits).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.WeeklyCapacityUnits).HasColumnType("TEXT").IsRequired();
        });

        modelBuilder.Entity<Entities.SchedulingLabOffdayEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartDate);
            e.HasIndex(x => x.EndDate);
            e.Property(x => x.StartDate).IsRequired();
            e.Property(x => x.EndDate).IsRequired();
        });

        modelBuilder.Entity<Entities.SchedulingDeadlineRecommendationLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.OrderCode);
            e.HasIndex(x => x.CreatedAtUnixTimeMilliseconds);
            e.Property(x => x.OrderCode).IsRequired();
            e.Property(x => x.CreatedByOrganizationType).IsRequired();
            e.Property(x => x.CreatedByOrganizationCode).IsRequired();
            e.Property(x => x.CreatedByMemberId).IsRequired();
            e.Property(x => x.CreatedByMemberLabel).IsRequired();
            e.Property(x => x.Material).IsRequired();
            e.Property(x => x.CapacityUnitsPerToothUsed).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.CalculatedOrderCapacityUnits).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.ResultStatus).IsRequired();
            e.Property(x => x.CandidateChecksJson).IsRequired();
            e.Property(x => x.ConfigSnapshotJson).IsRequired();
        });

        modelBuilder.Entity<Entities.SchedulingDeadlineOverrideLogEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.OrderCode);
            e.HasIndex(x => x.CreatedAtUnixTimeMilliseconds);
            e.Property(x => x.OrderCode).IsRequired();
            e.Property(x => x.CreatedByOrganizationType).IsRequired();
            e.Property(x => x.CreatedByOrganizationCode).IsRequired();
            e.Property(x => x.CreatedByMemberId).IsRequired();
            e.Property(x => x.CreatedByMemberLabel).IsRequired();
            e.Property(x => x.OrderCapacityUnits).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.RulesBypassedJson).IsRequired();
            e.Property(x => x.OverrideReason).IsRequired();
            e.Property(x => x.ExistingDailyCapacityUsed).HasColumnType("TEXT");
            e.Property(x => x.ExistingWeeklyCapacityUsed).HasColumnType("TEXT");
            e.Property(x => x.DailyCapacityLimitUsed).HasColumnType("TEXT");
            e.Property(x => x.WeeklyCapacityLimitUsed).HasColumnType("TEXT");
            e.Property(x => x.DailyCapacityAfterOverride).HasColumnType("TEXT");
            e.Property(x => x.WeeklyCapacityAfterOverride).HasColumnType("TEXT");
        });

        modelBuilder.Entity<Entities.AuditEventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OccurredAtUnixTimeMilliseconds);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => new { x.ActorOrganizationCode, x.OccurredAtUnixTimeMilliseconds });
            e.HasIndex(x => new { x.ServiceName, x.Operation, x.OccurredAtUnixTimeMilliseconds });
            e.Property(x => x.ServiceName).IsRequired();
            e.Property(x => x.Operation).IsRequired();
            e.Property(x => x.EntityType).IsRequired();
            e.Property(x => x.EntityId).IsRequired();
            e.Property(x => x.ActorOrganizationType).HasColumnName("ActorRole").IsRequired();
            e.Property(x => x.ActorOrganizationCode).HasColumnName("ActorClinicCode");
            e.Property(x => x.ActorMemberId).HasColumnName("ActorCredentialId");
            e.Property(x => x.ActorMemberLabel).HasColumnName("ActorCredentialLabel");
        });
    }
}
