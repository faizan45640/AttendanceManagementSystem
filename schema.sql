-- ============================================
-- Attendance Management System - Nullable Schema
-- ============================================

-- Drop existing tables if they exist
IF OBJECT_ID('Attendance', 'U') IS NOT NULL DROP TABLE Attendance;
IF OBJECT_ID('TimetableSlots', 'U') IS NOT NULL DROP TABLE TimetableSlots;
IF OBJECT_ID('Timetables', 'U') IS NOT NULL DROP TABLE Timetables;
IF OBJECT_ID('Sessions', 'U') IS NOT NULL DROP TABLE Sessions;
IF OBJECT_ID('Enrollments', 'U') IS NOT NULL DROP TABLE Enrollments;
IF OBJECT_ID('CourseAssignments', 'U') IS NOT NULL DROP TABLE CourseAssignments;
IF OBJECT_ID('Courses', 'U') IS NOT NULL DROP TABLE Courses;
IF OBJECT_ID('Semesters', 'U') IS NOT NULL DROP TABLE Semesters;
IF OBJECT_ID('Students', 'U') IS NOT NULL DROP TABLE Students;
IF OBJECT_ID('Teachers', 'U') IS NOT NULL DROP TABLE Teachers;
IF OBJECT_ID('Admins', 'U') IS NOT NULL DROP TABLE Admins;
IF OBJECT_ID('Batches', 'U') IS NOT NULL DROP TABLE Batches;
IF OBJECT_ID('Users', 'U') IS NOT NULL DROP TABLE Users;

-- ============================================
-- 1. Users Table
-- ============================================
CREATE TABLE Users (
    UserId INT PRIMARY KEY IDENTITY(1,1),
    Username NVARCHAR(50) UNIQUE,
    Email NVARCHAR(100) UNIQUE,
    PasswordHash NVARCHAR(255),
    Role NVARCHAR(20) CHECK (Role IN ('Admin', 'Teacher', 'Student')),
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- ============================================
-- 2. Batches Table
-- ============================================
CREATE TABLE Batches (
    BatchId INT PRIMARY KEY IDENTITY(1,1),
    BatchName NVARCHAR(50),
    Year INT,
    IsActive BIT DEFAULT 1
);

-- ============================================
-- 3. Admins Table
-- ============================================
CREATE TABLE Admins (
    AdminId INT PRIMARY KEY IDENTITY(1,1),
    UserId INT UNIQUE FOREIGN KEY REFERENCES Users(UserId) ON DELETE CASCADE,
    FirstName NVARCHAR(50),
    LastName NVARCHAR(50)
);

-- ============================================
-- 4. Teachers Table
-- ============================================
CREATE TABLE Teachers (
    TeacherId INT PRIMARY KEY IDENTITY(1,1),
    UserId INT UNIQUE FOREIGN KEY REFERENCES Users(UserId) ON DELETE CASCADE,
    FirstName NVARCHAR(50),
    LastName NVARCHAR(50),
    IsActive BIT DEFAULT 1
);

-- ============================================
-- 5. Students Table
-- ============================================
CREATE TABLE Students (
    StudentId INT PRIMARY KEY IDENTITY(1,1),
    UserId INT UNIQUE FOREIGN KEY REFERENCES Users(UserId) ON DELETE CASCADE,
    RollNumber NVARCHAR(20) UNIQUE,
    FirstName NVARCHAR(50),
    LastName NVARCHAR(50),
    BatchId INT FOREIGN KEY REFERENCES Batches(BatchId),
    IsActive BIT DEFAULT 1
);

-- ============================================
-- 6. Semesters Table
-- ============================================
CREATE TABLE Semesters (
    SemesterId INT PRIMARY KEY IDENTITY(1,1),
    SemesterName NVARCHAR(50),
    Year INT,
    StartDate DATE,
    EndDate DATE,
    IsActive BIT DEFAULT 1
);

-- ============================================
-- 7. Courses Table
-- ============================================
CREATE TABLE Courses (
    CourseId INT PRIMARY KEY IDENTITY(1,1),
    CourseCode NVARCHAR(10) UNIQUE,
    CourseName NVARCHAR(100),
    CreditHours INT,
    IsActive BIT DEFAULT 1
);

-- ============================================
-- 8. CourseAssignments Table
-- ============================================
CREATE TABLE CourseAssignments (
    AssignmentId INT PRIMARY KEY IDENTITY(1,1),
    TeacherId INT FOREIGN KEY REFERENCES Teachers(TeacherId),
    CourseId INT FOREIGN KEY REFERENCES Courses(CourseId),
    BatchId INT FOREIGN KEY REFERENCES Batches(BatchId),
    SemesterId INT FOREIGN KEY REFERENCES Semesters(SemesterId),
    IsActive BIT DEFAULT 1
);

-- ============================================
-- 9. Enrollments Table
-- ============================================
CREATE TABLE Enrollments (
    EnrollmentId INT PRIMARY KEY IDENTITY(1,1),
    StudentId INT FOREIGN KEY REFERENCES Students(StudentId),
    CourseId INT FOREIGN KEY REFERENCES Courses(CourseId),
    SemesterId INT FOREIGN KEY REFERENCES Semesters(SemesterId),
    Status NVARCHAR(20) DEFAULT 'Active' CHECK (Status IN ('Active', 'Dropped'))
);

-- ============================================
-- 10. Sessions Table
-- ============================================
CREATE TABLE Sessions (
    SessionId INT PRIMARY KEY IDENTITY(1,1),
    CourseAssignmentId INT FOREIGN KEY REFERENCES CourseAssignments(AssignmentId),
    SessionDate DATE,
    StartTime TIME,
    EndTime TIME,
    CreatedBy INT FOREIGN KEY REFERENCES Users(UserId)
);

-- ============================================
-- 11. Attendance Table
-- ============================================
CREATE TABLE Attendance (
    AttendanceId INT PRIMARY KEY IDENTITY(1,1),
    SessionId INT FOREIGN KEY REFERENCES Sessions(SessionId),
    StudentId INT FOREIGN KEY REFERENCES Students(StudentId),
    Status NVARCHAR(20) DEFAULT 'Absent' CHECK (Status IN ('Present', 'Absent', 'Late')),
    MarkedBy INT FOREIGN KEY REFERENCES Users(UserId)
);

-- ============================================
-- 12. Timetables Table
-- ============================================
CREATE TABLE Timetables (
    TimetableId INT PRIMARY KEY IDENTITY(1,1),
    BatchId INT FOREIGN KEY REFERENCES Batches(BatchId),
    SemesterId INT FOREIGN KEY REFERENCES Semesters(SemesterId),
    IsActive BIT DEFAULT 1
);

-- ============================================
-- 13. TimetableSlots Table
-- ============================================
CREATE TABLE TimetableSlots (
    SlotId INT PRIMARY KEY IDENTITY(1,1),
    TimetableId INT FOREIGN KEY REFERENCES Timetables(TimetableId) ON DELETE CASCADE,
    CourseAssignmentId INT FOREIGN KEY REFERENCES CourseAssignments(AssignmentId),
    DayOfWeek INT CHECK (DayOfWeek BETWEEN 1 AND 7),
    StartTime TIME,
    EndTime TIME
);
GO
