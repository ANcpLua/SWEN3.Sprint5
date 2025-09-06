using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PaperlessREST.DAL;

public class DocumentPersistence : DbContext
{
    public DocumentPersistence(DbContextOptions<DocumentPersistence> options) : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<DocumentStatus>();

        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id").IsRequired();

            entity.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(255);

            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("document_status");

            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.StoragePath).HasColumnName("storage_path").HasMaxLength(500);

            entity.Property(e => e.Content).HasColumnName("content").HasMaxLength(1000000);

            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");

            entity.Property(e => e.Summary).HasColumnName("summary").HasMaxLength(5000);
        });
    }
}

public class DocumentPersistenceFactory : IDesignTimeDbContextFactory<DocumentPersistence>
{
    public DocumentPersistence CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentPersistence>();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PaperlessDb");

        optionsBuilder.UseNpgsql(connectionString, o => o.MapEnum<DocumentStatus>());

        return new DocumentPersistence(optionsBuilder.Options);
    }
}

public class DocumentEntity
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public DocumentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string StoragePath { get; set; } = null!;
    public string? Content { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}