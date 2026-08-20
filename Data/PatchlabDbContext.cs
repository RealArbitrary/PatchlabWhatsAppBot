using Microsoft.EntityFrameworkCore;

namespace PatchlabWhatsAppBot.Data;

public class ErrorLog
{
    public int Id { get; set; }
    public string Severity { get; set; } = "";   // "Warning", "Error", "Critical"
    public string Source { get; set; } = "";     // logger category, e.g. "PatchlabWhatsAppBot.Controllers.WhatsAppWebhookController"
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Ticket
{
    public int Id { get; set; }
    public string TicketNumber { get; private set; } = "";
    public string CellphoneNumber { get; set; } = "";
    public string Issue { get; set; } = "";
    public string? Area { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "Open";
}

public class Customer
{
    public string CellphoneNumber { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Area { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TicketFeedback
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public string Status { get; set; } = ""; // "Satisfied" or "Unhappy"
    public string? Reason { get; set; }      // populated only for "Unhappy"
    public DateTime CreatedAt { get; set; }
}

public class PatchlabDbContext : DbContext
{
    public PatchlabDbContext(DbContextOptions<PatchlabDbContext> options) : base(options) { }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<TicketFeedback> TicketFeedback => Set<TicketFeedback>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErrorLog>(e =>
        {
            e.ToTable("ErrorLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Severity).HasMaxLength(20).IsRequired();
            e.Property(x => x.Source).HasMaxLength(300).IsRequired();
            e.Property(x => x.Message).IsRequired();
            e.Property(x => x.StackTrace);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_ErrorLogs_CreatedAt");
        });

        modelBuilder.Entity<Ticket>(e =>
        {
            e.ToTable("Tickets");
            e.HasKey(t => t.Id);

            e.Property(t => t.Id).ValueGeneratedOnAdd();

            // Computed, persisted column — EF must never try to insert/update it.
            e.Property(t => t.TicketNumber)
                .HasComputedColumnSql("('TCKT-'+right('0000'+CONVERT([varchar](10),[Id]),(4)))", stored: true)
                .ValueGeneratedOnAddOrUpdate();

            e.Property(t => t.CellphoneNumber).HasMaxLength(20).IsRequired();
            e.Property(t => t.Issue).IsRequired();
            e.Property(t => t.Area).HasMaxLength(200);
            e.Property(t => t.CreatedAt).HasDefaultValueSql("sysutcdatetime()");
            e.Property(t => t.Status).HasMaxLength(20).HasDefaultValue("Open");

            e.HasIndex(t => t.CellphoneNumber).HasDatabaseName("IX_Tickets_CellphoneNumber");
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(c => c.CellphoneNumber);

            e.Property(c => c.CellphoneNumber).HasMaxLength(20);
            e.Property(c => c.FirstName).HasMaxLength(100).IsRequired();
            e.Property(c => c.LastName).HasMaxLength(100).IsRequired();
            e.Property(c => c.Area).HasMaxLength(200);
            e.Property(c => c.CreatedAt).HasDefaultValueSql("sysutcdatetime()");
            e.Property(c => c.UpdatedAt).HasDefaultValueSql("sysutcdatetime()");
        });

        modelBuilder.Entity<TicketFeedback>(e =>
        {
            e.ToTable("TicketFeedback");
            e.HasKey(f => f.Id);

            e.Property(f => f.Status).HasMaxLength(20).IsRequired();
            e.Property(f => f.Reason);
            e.Property(f => f.CreatedAt).HasDefaultValueSql("sysutcdatetime()");

            e.HasOne(f => f.Ticket)
                .WithMany()
                .HasForeignKey(f => f.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}