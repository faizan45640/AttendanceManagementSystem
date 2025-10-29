using Microsoft.EntityFrameworkCore;
using AMS.Models;
using AMS.Models.Entities;


namespace AMS.Models
{
    public class ApplicationDbContext_old : DbContext
    {
        public ApplicationDbContext_old(DbContextOptions<ApplicationDbContext_old> options)
            : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<Batch> Batches { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User Configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.UserId);
                entity.HasIndex(u => u.Email).IsUnique();
                entity.Property(u => u.IsActive).HasDefaultValue(true);
                entity.Property(u => u.CreatedAt).HasDefaultValueSql("GETDATE()");
            });

            // Batch Configuration
            modelBuilder.Entity<Batch>(entity =>
            {
                entity.HasKey(b => b.BatchId);
                entity.Property(b => b.BatchName).IsRequired().HasMaxLength(50);
                entity.Property(b => b.Year).IsRequired();
                entity.Property(b => b.IsActive).HasDefaultValue(true);
            });

            // Admin Configuration
            modelBuilder.Entity<Admin>(entity =>
            {
                entity.HasKey(a => a.AdminId);
                entity.HasIndex(a => a.UserId).IsUnique();
            });

            // Teacher Configuration
            modelBuilder.Entity<Teacher>(entity =>
            {
                entity.HasKey(t => t.TeacherId);
                entity.HasIndex(t => t.UserId).IsUnique();
            });

            // Student Configuration
            modelBuilder.Entity<Student>(entity =>
            {
                entity.HasKey(s => s.StudentId);
                entity.HasIndex(s => s.UserId).IsUnique();
                entity.HasIndex(s => s.RollNumber).IsUnique();
            });

            // Relationships: One User → One Role Entity
            modelBuilder.Entity<User>()
                .HasOne(u => u.Admin)
                .WithOne(a => a.User)
                .HasForeignKey<Admin>(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Teacher)
                .WithOne(t => t.User)
                .HasForeignKey<Teacher>(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Student)
                .WithOne(s => s.User)
                .HasForeignKey<Student>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relationship: One Batch → Many Students
            modelBuilder.Entity<Batch>()
                .HasMany(b => b.Students)
                .WithOne(s => s.Batch)
                .HasForeignKey(s => s.BatchId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent deleting batch if students exist
        }
    }
}