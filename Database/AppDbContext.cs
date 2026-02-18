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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entities.InvoiceEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasMany(x => x.LineItems)
                .WithOne(x => x.Invoice)
                .HasForeignKey(x => x.InvoiceEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Entities.InvoiceLineItemEntity>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<Entities.ClientEntity>(e =>
        {
            e.HasKey(x => x.Nickname);
        });

        modelBuilder.Entity<Entities.InvoiceSequenceEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.ToTable("InvoiceSequence");
        });
    }
}
