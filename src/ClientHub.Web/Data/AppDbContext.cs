using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Data;

/// <summary>
/// Identity without role tables: the role lives only in users.role (TSD column) and is issued as a claim
/// by <see cref="ClientHub.Web.Services.Access.AppClaimsPrincipalFactory"/>.
/// </summary>
public class AppDbContext : IdentityUserContext<AppUser, Guid>, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseStaff> CaseStaff => Set<CaseStaff>();
    public DbSet<CaseUpdate> CaseUpdates => Set<CaseUpdate>();
    public DbSet<DocumentRequest> DocumentRequests => Set<DocumentRequest>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillLine> BillLines => Set<BillLine>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FirmSettings> FirmSettings => Set<FirmSettings>();
    public DbSet<AuditLog> AuditLog => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>().ToTable("users");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        b.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.Language).HasMaxLength(5).HasDefaultValue("en");
        });

        b.Entity<Client>(e =>
        {
            e.ToTable("clients");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.UserId).IsUnique();
        });
        b.Entity<Case>(e =>
        {
            e.ToTable("cases");
            e.HasOne(x => x.Client).WithMany(c => c.Cases).HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lawyer).WithMany().HasForeignKey(x => x.LawyerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ClientId);
        });
        b.Entity<CaseStaff>(e =>
        {
            e.ToTable("case_staff");
            e.HasOne(x => x.Case).WithMany(c => c.StaffLinks).HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CaseId, x.UserId }).IsUnique();
        });
        b.Entity<CaseUpdate>(e =>
        {
            e.ToTable("case_updates");
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DocumentRequest>(e =>
        {
            e.ToTable("document_requests");
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Document>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Document>(e =>
        {
            e.ToTable("documents");
            // Nothing is deleted: removed documents are hidden everywhere unless a query opts out with IgnoreQueryFilters().
            e.HasQueryFilter(x => x.IsActive);
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RemovedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ClientId, x.SentAt });
        });
        b.Entity<Bill>(e =>
        {
            e.ToTable("bills");
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.BillNo).IsUnique();
            e.Property(x => x.Total).HasPrecision(12, 2);
        });
        b.Entity<BillLine>(e =>
        {
            e.ToTable("bill_lines");
            e.HasOne<Bill>().WithMany(x => x.Lines).HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Amount).HasPrecision(12, 2);
        });
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.ReadAt });
        });
        b.Entity<FirmSettings>().ToTable("firm_settings");
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_log");
            e.Property(x => x.Details).HasColumnType("jsonb");
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Entity e)
            {
                if (entry.State == EntityState.Added) { e.CreatedAt = now; e.UpdatedAt = now; }
                else if (entry.State == EntityState.Modified) e.UpdatedAt = now;
            }
            else if (entry.Entity is AppUser u)
            {
                if (entry.State == EntityState.Added) { u.CreatedAt = now; u.UpdatedAt = now; }
                else if (entry.State == EntityState.Modified) u.UpdatedAt = now;
            }
        }
        return base.SaveChangesAsync(ct);
    }
}
