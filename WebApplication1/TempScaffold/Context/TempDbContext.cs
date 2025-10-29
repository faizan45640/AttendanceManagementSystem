using System;
using System.Collections.Generic;
using AMS.TempScaffold.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.TempScaffold.Context;

public partial class TempDbContext : DbContext
{
    public TempDbContext()
    {
    }

    public TempDbContext(DbContextOptions<TempDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Admin> Admins { get; set; }

    public virtual DbSet<Attendance> Attendances { get; set; }

    public virtual DbSet<Batch> Batches { get; set; }

    public virtual DbSet<Course> Courses { get; set; }

    public virtual DbSet<CourseAssignment> CourseAssignments { get; set; }

    public virtual DbSet<Enrollment> Enrollments { get; set; }

    public virtual DbSet<Semester> Semesters { get; set; }

    public virtual DbSet<Session> Sessions { get; set; }

    public virtual DbSet<Student> Students { get; set; }

    public virtual DbSet<Teacher> Teachers { get; set; }

    public virtual DbSet<Timetable> Timetables { get; set; }

    public virtual DbSet<TimetableSlot> TimetableSlots { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Data Source=desktop-mdfvllc\\sqlexpress;Initial Catalog=AMS;Integrated Security=True;Encrypt=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.AdminId).HasName("PK__Admins__719FE4882C0EACCB");

            entity.HasIndex(e => e.UserId, "UQ__Admins__1788CC4D187583B8").IsUnique();

            entity.Property(e => e.FirstName).HasMaxLength(50);
            entity.Property(e => e.LastName).HasMaxLength(50);

            entity.HasOne(d => d.User).WithOne(p => p.Admin)
                .HasForeignKey<Admin>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Admins__UserId__412EB0B6");
        });

        modelBuilder.Entity<Attendance>(entity =>
        {
            entity.HasKey(e => e.AttendanceId).HasName("PK__Attendan__8B69261C27B44320");

            entity.ToTable("Attendance");

            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Absent");

            entity.HasOne(d => d.MarkedByNavigation).WithMany(p => p.Attendances)
                .HasForeignKey(d => d.MarkedBy)
                .HasConstraintName("FK__Attendanc__Marke__6B24EA82");

            entity.HasOne(d => d.Session).WithMany(p => p.Attendances)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("FK__Attendanc__Sessi__6754599E");

            entity.HasOne(d => d.Student).WithMany(p => p.Attendances)
                .HasForeignKey(d => d.StudentId)
                .HasConstraintName("FK__Attendanc__Stude__68487DD7");
        });

        modelBuilder.Entity<Batch>(entity =>
        {
            entity.HasKey(e => e.BatchId).HasName("PK__Batches__5D55CE580E6F1491");

            entity.Property(e => e.BatchName).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.HasKey(e => e.CourseId).HasName("PK__Courses__C92D71A718B957C6");

            entity.HasIndex(e => e.CourseCode, "UQ__Courses__FC00E0008DE7A2DC").IsUnique();

            entity.Property(e => e.CourseCode).HasMaxLength(10);
            entity.Property(e => e.CourseName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<CourseAssignment>(entity =>
        {
            entity.HasKey(e => e.AssignmentId).HasName("PK__CourseAs__32499E77E68389CE");

            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Batch).WithMany(p => p.CourseAssignments)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("FK__CourseAss__Batch__5812160E");

            entity.HasOne(d => d.Course).WithMany(p => p.CourseAssignments)
                .HasForeignKey(d => d.CourseId)
                .HasConstraintName("FK__CourseAss__Cours__571DF1D5");

            entity.HasOne(d => d.Semester).WithMany(p => p.CourseAssignments)
                .HasForeignKey(d => d.SemesterId)
                .HasConstraintName("FK__CourseAss__Semes__59063A47");

            entity.HasOne(d => d.Teacher).WithMany(p => p.CourseAssignments)
                .HasForeignKey(d => d.TeacherId)
                .HasConstraintName("FK__CourseAss__Teach__5629CD9C");
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.HasKey(e => e.EnrollmentId).HasName("PK__Enrollme__7F68771B48B20095");

            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            entity.HasOne(d => d.Course).WithMany(p => p.Enrollments)
                .HasForeignKey(d => d.CourseId)
                .HasConstraintName("FK__Enrollmen__Cours__5DCAEF64");

            entity.HasOne(d => d.Semester).WithMany(p => p.Enrollments)
                .HasForeignKey(d => d.SemesterId)
                .HasConstraintName("FK__Enrollmen__Semes__5EBF139D");

            entity.HasOne(d => d.Student).WithMany(p => p.Enrollments)
                .HasForeignKey(d => d.StudentId)
                .HasConstraintName("FK__Enrollmen__Stude__5CD6CB2B");
        });

        modelBuilder.Entity<Semester>(entity =>
        {
            entity.HasKey(e => e.SemesterId).HasName("PK__Semester__043301DDDBF242CD");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.SemesterName).HasMaxLength(50);
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__Sessions__C9F492903CE85F74");

            entity.HasOne(d => d.CourseAssignment).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.CourseAssignmentId)
                .HasConstraintName("FK__Sessions__Course__6383C8BA");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK__Sessions__Create__6477ECF3");
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(e => e.StudentId).HasName("PK__Students__32C52B9990F352E7");

            entity.HasIndex(e => e.UserId, "UQ__Students__1788CC4D671FEA16").IsUnique();

            entity.HasIndex(e => e.RollNumber, "UQ__Students__E9F06F169BC6B808").IsUnique();

            entity.Property(e => e.FirstName).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastName).HasMaxLength(50);
            entity.Property(e => e.RollNumber).HasMaxLength(20);

            entity.HasOne(d => d.Batch).WithMany(p => p.Students)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("FK__Students__BatchI__4BAC3F29");

            entity.HasOne(d => d.User).WithOne(p => p.Student)
                .HasForeignKey<Student>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Students__UserId__4AB81AF0");
        });

        modelBuilder.Entity<Teacher>(entity =>
        {
            entity.HasKey(e => e.TeacherId).HasName("PK__Teachers__EDF25964FB258040");

            entity.HasIndex(e => e.UserId, "UQ__Teachers__1788CC4DAE73BFF3").IsUnique();

            entity.Property(e => e.FirstName).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastName).HasMaxLength(50);

            entity.HasOne(d => d.User).WithOne(p => p.Teacher)
                .HasForeignKey<Teacher>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Teachers__UserId__44FF419A");
        });

        modelBuilder.Entity<Timetable>(entity =>
        {
            entity.HasKey(e => e.TimetableId).HasName("PK__Timetabl__68413F60DB556449");

            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Batch).WithMany(p => p.Timetables)
                .HasForeignKey(d => d.BatchId)
                .HasConstraintName("FK__Timetable__Batch__6E01572D");

            entity.HasOne(d => d.Semester).WithMany(p => p.Timetables)
                .HasForeignKey(d => d.SemesterId)
                .HasConstraintName("FK__Timetable__Semes__6EF57B66");
        });

        modelBuilder.Entity<TimetableSlot>(entity =>
        {
            entity.HasKey(e => e.SlotId).HasName("PK__Timetabl__0A124AAF4B80EF0A");

            entity.HasOne(d => d.CourseAssignment).WithMany(p => p.TimetableSlots)
                .HasForeignKey(d => d.CourseAssignmentId)
                .HasConstraintName("FK__Timetable__Cours__73BA3083");

            entity.HasOne(d => d.Timetable).WithMany(p => p.TimetableSlots)
                .HasForeignKey(d => d.TimetableId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Timetable__Timet__72C60C4A");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C9469E077");

            entity.HasIndex(e => e.Username, "UQ__Users__536C85E4B5590FF6").IsUnique();

            entity.HasIndex(e => e.Email, "UQ__Users__A9D10534D58440E5").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.Role).HasMaxLength(20);
            entity.Property(e => e.Username).HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
