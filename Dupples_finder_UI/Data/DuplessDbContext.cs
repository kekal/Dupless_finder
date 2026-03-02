using Dupples_finder_UI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dupples_finder_UI.Data
{
    public class DuplessDbContext : DbContext
    {
        public DbSet<Photo> Photos { get; }
        public DbSet<SimilarityResult> SimilarityResults { get; }

        private readonly string _dbPath;

        public DuplessDbContext(string dbPath = null)
        {
            _dbPath = dbPath ?? "dupless_cache.db";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Photo>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.FilePath)
                    .IsRequired()
                    .HasMaxLength(1024);

                // FilePath index is kept for fast lookups but is no longer unique —
                // the same content can exist at multiple paths.
                entity.HasIndex(e => e.FilePath)
                    .IsUnique(false);

                entity.Property(e => e.SiftDescriptors)
                    .IsRequired(false);

                entity.Property(e => e.DescriptorRows)
                    .HasDefaultValue(0);

                entity.Property(e => e.DescriptorCols)
                    .HasDefaultValue(128);

                entity.Property(e => e.FileSize)
                    .IsRequired();

                entity.Property(e => e.HashDate)
                    .IsRequired();

                entity.Property(e => e.LastModifiedUtc)
                    .IsRequired();

                // Thumbnail is optional — null until generated.
                entity.Property(e => e.Thumbnail)
                    .IsRequired(false);

                // Composite index on (FileSize, LastModifiedUtc) is the new
                // fingerprint-based identity for cache lookups. Unique so that
                // we never store two rows for the same physical content.
                entity.HasIndex(e => new { e.FileSize, e.LastModifiedUtc })
                    .IsUnique();

                // The Fingerprint property is computed in-memory and not stored.
                entity.Ignore(e => e.Fingerprint);
            });

            modelBuilder.Entity<SimilarityResult>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Photo1)
                    .WithMany()
                    .HasForeignKey(e => e.Photo1Id)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Photo2)
                    .WithMany()
                    .HasForeignKey(e => e.Photo2Id)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(e => e.Score)
                    .IsRequired();

                entity.Property(e => e.CompareDate)
                    .IsRequired();

                entity.HasIndex(e => new { e.Photo1Id, e.Photo2Id })
                    .IsUnique();
            });
        }
    }
}
