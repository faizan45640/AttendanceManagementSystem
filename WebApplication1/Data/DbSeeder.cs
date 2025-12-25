using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AMS.Models;
using AMS.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMS.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Seed only when DB looks empty to avoid unique constraint conflicts.
        if (await db.Users.AsNoTracking().AnyAsync())
        {
            logger.LogInformation("DbSeeder: Users already exist; skipping seed.");
            return;
        }

        logger.LogInformation("DbSeeder: Seeding database with demo data...");

        var now = DateTime.UtcNow;
        var rng = new Random(42);

        // --- System settings (branding/academic) ---
        db.SystemSettings.AddRange(
            Setting("InstitutionName", "Attendo Demo University", "Branding", "string", "Shown in navbar and login"),
            Setting("InstitutionEmail", "info@attendo.local", "Branding", "string", "Support email"),
            Setting("InstitutionPhone", "+1 555 0100", "Branding", "string", "Support phone"),
            Setting("InstitutionAddress", "1 Campus Road", "Branding", "string", "Address"),
            Setting("InstitutionLogo", null, "Branding", "string", "Optional logo URL"),
            Setting("CurrentAcademicYear", "2025-2026", "Academic", "string", "Current academic year")
        );

        // --- Users ---
        var adminUser = new User
        {
            Username = "admin",
            Email = "admin@attendo.local",
            PasswordHash = "Admin123!", // NOTE: matches current login logic
            Role = "Admin",
            IsActive = true,
            CreatedAt = now
        };

        var teacherUser = new User
        {
            Username = "teacher",
            Email = "teacher@attendo.local",
            PasswordHash = "Teacher123!",
            Role = "Teacher",
            IsActive = true,
            CreatedAt = now
        };

        db.Users.AddRange(adminUser, teacherUser);

        // Create 10 students
        var studentUsers = new List<User>();
        for (var i = 1; i <= 10; i++)
        {
            studentUsers.Add(new User
            {
                Username = $"student{i}",
                Email = $"student{i}@attendo.local",
                PasswordHash = "Student123!",
                Role = "Student",
                IsActive = true,
                CreatedAt = now
            });
        }

        db.Users.AddRange(studentUsers);
        await db.SaveChangesAsync();

        // --- Admin/Teacher/Student profiles ---
        db.Admins.Add(new Admin
        {
            UserId = adminUser.UserId,
            FirstName = "Amina",
            LastName = "Admin",
            
        });

        var teacher = new Teacher
        {
            UserId = teacherUser.UserId,
            FirstName = "Tariq",
            LastName = "Teacher",
            IsActive = true
        };
        db.Teachers.Add(teacher);

        // --- Batch + Semester ---
        var batch = new Batch
        {
            BatchName = "BSCS-A",
            Year = 2025,
            IsActive = true
        };
        db.Batches.Add(batch);

        var semester = new Semester
        {
            SemesterName = "Fall",
            Year = 2025,
            StartDate = new DateOnly(2025, 9, 1),
            EndDate = new DateOnly(2025, 12, 31),
            IsActive = true
        };
        db.Semesters.Add(semester);

        await db.SaveChangesAsync();

        // --- Students ---
        var students = new List<Student>();
        for (var i = 1; i <= studentUsers.Count; i++)
        {
            students.Add(new Student
            {
                UserId = studentUsers[i - 1].UserId,
                RollNumber = $"BSCS25-{i:000}",
                FirstName = $"Student{i}",
                LastName = "Demo",
                BatchId = batch.BatchId,
                IsActive = true
            });
        }
        db.Students.AddRange(students);

        // --- Courses ---
        var courses = new List<Course>
        {
            new() { CourseCode = "CS101", CourseName = "Programming Fundamentals", CreditHours = 3, IsActive = true },
            new() { CourseCode = "CS102", CourseName = "Object Oriented Programming", CreditHours = 3, IsActive = true },
            new() { CourseCode = "CS201", CourseName = "Data Structures", CreditHours = 3, IsActive = true },
            new() { CourseCode = "CS202", CourseName = "Database Systems", CreditHours = 3, IsActive = true }
        };
        db.Courses.AddRange(courses);

        await db.SaveChangesAsync();

        // --- Course assignments (teacher teaches all courses for this batch/semester) ---
        var assignments = courses.Select(c => new CourseAssignment
        {
            TeacherId = teacher.TeacherId,
            CourseId = c.CourseId,
            BatchId = batch.BatchId,
            SemesterId = semester.SemesterId,
            IsActive = true
        }).ToList();
        db.CourseAssignments.AddRange(assignments);

        // --- Timetable + slots ---
        var timetable = new Timetable
        {
            BatchId = batch.BatchId,
            SemesterId = semester.SemesterId,
            IsActive = true
        };
        db.Timetables.Add(timetable);

        await db.SaveChangesAsync();

        // Create 4 slots (Mon-Thu 9:00-10:30) for each assignment
        var dayBase = 1; // Monday
        var slots = new List<TimetableSlot>();
        for (var i = 0; i < assignments.Count; i++)
        {
            slots.Add(new TimetableSlot
            {
                TimetableId = timetable.TimetableId,
                CourseAssignmentId = assignments[i].AssignmentId,
                DayOfWeek = dayBase + (i % 4),
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 30)
            });
        }
        db.TimetableSlots.AddRange(slots);

        // --- Enrollments (all students enrolled in all courses for this semester) ---
        var enrollments = new List<Enrollment>();
        foreach (var student in students)
        {
            foreach (var course in courses)
            {
                enrollments.Add(new Enrollment
                {
                    StudentId = student.StudentId,
                    CourseId = course.CourseId,
                    SemesterId = semester.SemesterId,
                    BatchId = batch.BatchId,
                    Status = "Active"
                });
            }
        }
        db.Enrollments.AddRange(enrollments);

        await db.SaveChangesAsync();

        // --- Sessions + Attendance (3 recent sessions per assignment) ---
        var today = DateOnly.FromDateTime(DateTime.Now);
        var sessions = new List<Session>();

        foreach (var assignment in assignments)
        {
            for (var k = 1; k <= 3; k++)
            {
                sessions.Add(new Session
                {
                    CourseAssignmentId = assignment.AssignmentId,
                    SessionDate = today.AddDays(-k * 2),
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(10, 30),
                    CreatedBy = teacherUser.UserId
                });
            }
        }

        db.Sessions.AddRange(sessions);
        await db.SaveChangesAsync();

        var attendanceRows = new List<Attendance>();
        foreach (var session in sessions)
        {
            foreach (var student in students)
            {
                var roll = rng.Next(100);
                var status = roll < 80 ? "Present" : roll < 95 ? "Absent" : "Late";

                attendanceRows.Add(new Attendance
                {
                    SessionId = session.SessionId,
                    StudentId = student.StudentId,
                    Status = status,
                    MarkedBy = teacherUser.UserId
                });
            }
        }

        db.Attendances.AddRange(attendanceRows);

        await db.SaveChangesAsync();
        logger.LogInformation("DbSeeder: Seed complete. Admin/Teacher/Student demo users created.");

        static SystemSetting Setting(string key, string? value, string category, string type, string? description)
            => new()
            {
                SettingKey = key,
                SettingValue = value,
                Category = category,
                SettingType = type,
                Description = description,
                UpdatedAt = DateTime.UtcNow
            };
    }
}
