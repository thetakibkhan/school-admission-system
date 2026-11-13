using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Data;

public sealed class AdmissionDbContext : DbContext
{
    public AdmissionDbContext(DbContextOptions<AdmissionDbContext> options) : base(options)
    {
    }

    public DbSet<ClassEntity> Classes => Set<ClassEntity>();
    public DbSet<HoldEntity> Holds => Set<HoldEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassEntity>(entity =>
        {
            entity.ToTable("classes", table =>
            {
                table.HasCheckConstraint("ck_classes_total_seats_positive", "total_seats > 0");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("text");
            entity.Property(e => e.Name).HasColumnName("name").HasColumnType("text").IsRequired();
            entity.Property(e => e.NormalizedName).HasColumnName("normalized_name").HasColumnType("text").IsRequired();
            entity.Property(e => e.TotalSeats).HasColumnName("total_seats");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");

            entity.HasIndex(e => e.NormalizedName).IsUnique().HasDatabaseName("ux_classes_normalized_name");
        });

        modelBuilder.Entity<HoldEntity>(entity =>
        {
            entity.ToTable("holds", table =>
            {
                table.HasCheckConstraint("ck_holds_student_id_format", "student_id ~ '^[0-9]{17}$'");
                table.HasCheckConstraint("ck_holds_status", "status IN ('HELD','CONFIRMED','CANCELLED')");
            });

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("text");
            entity.Property(e => e.ClassId).HasColumnName("class_id").HasColumnType("text").IsRequired();
            entity.Property(e => e.StudentId).HasColumnName("student_id").HasColumnType("text").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasColumnType("text").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamp with time zone");

            entity.HasOne<ClassEntity>()
                .WithMany()
                .HasForeignKey(e => e.ClassId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_holds_classes_class_id");

            entity.HasIndex(e => e.ClassId).HasDatabaseName("ix_holds_class_id");
            entity.HasIndex(e => new { e.ClassId, e.Status, e.ExpiresAt }).HasDatabaseName("ix_holds_class_id_status_expires_at");
            entity.HasIndex(e => new { e.StudentId, e.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_holds_student_id_created_at");
            entity.HasIndex(e => new { e.ClassId, e.StudentId })
                .IsUnique()
                .HasFilter("status = 'CONFIRMED'")
                .HasDatabaseName("ux_holds_class_id_student_id_confirmed");
            // Non-unique lookup index on the same columns. EF merges indexes that share a property
            // list, so the plain index is declared by name to keep it a separate index.
            entity.HasIndex(new[] { nameof(HoldEntity.ClassId), nameof(HoldEntity.StudentId) },
                    "ix_holds_class_id_student_id")
                .HasDatabaseName("ix_holds_class_id_student_id");
        });
    }
}
