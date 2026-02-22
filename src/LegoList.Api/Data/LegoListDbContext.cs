using LegoList.Models;
using Microsoft.EntityFrameworkCore;

namespace LegoList.Data;

public class LegoListDbContext(DbContextOptions<LegoListDbContext> options) : DbContext(options)
{
    public DbSet<SetList> SetLists => Set<SetList>();
    public DbSet<LegoSet> LegoSets => Set<LegoSet>();
    public DbSet<User> Users => Set<User>();
    public DbSet<SetMetadata> SetMetadatas => Set<SetMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.GoogleSub).HasColumnName("google_sub");
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.GoogleSub).IsUnique();
        });

        modelBuilder.Entity<SetList>(e =>
        {
            e.ToTable("set_lists");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.HasOne(l => l.User).WithMany(u => u.SetLists)
                .HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(l => l.Sets)
                .WithOne(s => s.SetList)
                .HasForeignKey(s => s.SetListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SetMetadata>(e =>
        {
            e.ToTable("set_metadata");
            e.HasKey(x => x.SetNumber);
            e.Property(x => x.SetNumber).HasColumnName("set_number");
            e.Property(x => x.ImageUrl).HasColumnName("image_url");
            e.Property(x => x.PieceCount).HasColumnName("piece_count");
            e.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        });

        modelBuilder.Entity<LegoSet>(e =>
        {
            e.ToTable("lego_sets");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SetNumber).HasColumnName("set_number");
            e.Property(x => x.Theme).HasColumnName("theme");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.SetListId).HasColumnName("set_list_id");
            e.HasOne(s => s.SetMetadata)
                .WithMany()
                .HasForeignKey(s => s.SetNumber)
                .HasPrincipalKey(m => m.SetNumber)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
