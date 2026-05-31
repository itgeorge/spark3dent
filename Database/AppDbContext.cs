using Microsoft.EntityFrameworkCore;

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
    public DbSet<Entities.SchedulingAuthSessionEntity> SchedulingAuthSessions { get; set; }
    public DbSet<Entities.SchedulingOrderEntity> SchedulingOrders { get; set; }

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

        modelBuilder.Entity<Entities.SchedulingAuthSessionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.ClinicCode, x.CredentialId });
            e.Property(x => x.TokenHash).IsRequired();
            e.Property(x => x.ClinicCode).IsRequired();
            e.Property(x => x.CredentialId).IsRequired();
        });

        modelBuilder.Entity<Entities.SchedulingOrderEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OrderCode).IsUnique();
            e.HasIndex(x => x.ClinicCode);
            e.HasIndex(x => x.RequestedDeliveryDate);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Status);
            e.Property(x => x.OrderCode).IsRequired();
            e.Property(x => x.ClinicCode).IsRequired();
            e.Property(x => x.CredentialId).IsRequired();
        });
    }
}
