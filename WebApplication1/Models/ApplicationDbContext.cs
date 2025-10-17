using Microsoft.EntityFrameworkCore;
using AMS.Models.Entities;

namespace AMS.Models
{
	public class ApplicationDbContext : DbContext
	{
		public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
			: base(options) { }

		public DbSet<User> Users { get; set; }
		public DbSet<Admin> Admins { get; set; }
		public DbSet<Teacher> Teachers { get; set; }
		public DbSet<Student> Students { get; set; }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			// Relationships: One User → One Role Entity
			modelBuilder.Entity<User>()
				.HasOne(u => u.Admin)
				.WithOne(a => a.User)
				.HasForeignKey<Admin>(a => a.UserId);

			modelBuilder.Entity<User>()
				.HasOne(u => u.Teacher)
				.WithOne(t => t.User)
				.HasForeignKey<Teacher>(t => t.UserId);

			modelBuilder.Entity<User>()
				.HasOne(u => u.Student)
				.WithOne(s => s.User)
				.HasForeignKey<Student>(s => s.UserId);
		}
	}
}
