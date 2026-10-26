using Microsoft.EntityFrameworkCore;
using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Data;

public class OhmventoryDbContext(DbContextOptions<OhmventoryDbContext> options) : DbContext(options)
{
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<Container> Containers => Set<Container>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<PartImage> PartImages => Set<PartImage>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectItem> ProjectItems => Set<ProjectItem>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Part>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Category).HasConversion<string>().HasMaxLength(40);
            e.HasIndex(p => p.PartNumber);
            e.HasIndex(p => p.Name);
        });

        b.Entity<Container>(e =>
        {
            e.Property(c => c.Code).HasMaxLength(40);
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne(c => c.Parent).WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockItem>(e =>
        {
            e.HasOne(s => s.Part).WithMany(p => p.Stock).HasForeignKey(s => s.PartId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Container).WithMany(c => c.Stock).HasForeignKey(s => s.ContainerId).OnDelete(DeleteBehavior.Restrict);
            // Ein Bauteil hat pro Container höchstens einen Bestandseintrag; "ohne Container" zählt als eigener Ort.
            e.HasIndex(s => new { s.PartId, s.ContainerId }).IsUnique().AreNullsDistinct(false);
        });

        b.Entity<PartImage>()
            .HasOne(i => i.Part).WithMany(p => p.Images).HasForeignKey(i => i.PartId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<ProjectItem>(e =>
        {
            e.HasOne(i => i.Project).WithMany(p => p.Items).HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Part).WithMany().HasForeignKey(i => i.PartId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ShoppingListItem>()
            .HasOne(i => i.Project).WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.SetNull);
    }
}
