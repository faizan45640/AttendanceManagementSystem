-- 1. Add the BatchId column to the Enrollments table


-- 2. Add the Foreign Key constraint to link Enrollments to

-- 3. (Optional) Create an index on BatchId for better query performance
CREATE INDEX [IX_Enrollments_BatchId] ON [Enrollments]([BatchId]);
GO

-- =============================================
-- 4. Create SystemSettings Table for Settings Feature
-- =============================================
CREATE TABLE SystemSettings (
    SettingId INT IDENTITY(1,1) PRIMARY KEY,
    SettingKey NVARCHAR(100) NOT NULL,
    SettingValue NVARCHAR(MAX) NULL,
    SettingType NVARCHAR(50) NULL,
    Category NVARCHAR(100) NULL,
    Description NVARCHAR(255) NULL,
    IsEditable BIT DEFAULT 1,
    UpdatedAt DATETIME2 NULL,
    UpdatedBy INT NULL
);
GO

-- Add unique constraint on SettingKey
CREATE UNIQUE INDEX [UQ_SystemSettings_SettingKey] ON [SystemSettings]([SettingKey]);
GO

-- Insert Default Settings
INSERT INTO SystemSettings (SettingKey, SettingValue, SettingType, Category, Description, IsEditable) VALUES
('InstitutionName', 'My Institution', 'string', 'Branding', 'Name of the institution', 1),
('InstitutionLogo', NULL, 'image', 'Branding', 'Logo file path', 1),
('InstitutionAddress', NULL, 'string', 'Branding', 'Institution address', 1),
('InstitutionPhone', NULL, 'string', 'Branding', 'Institution phone number', 1),
('InstitutionEmail', NULL, 'string', 'Branding', 'Institution email address', 1),
('CurrentAcademicYear', '2024-2025', 'string', 'Academic', 'Current academic year', 1),
('MinimumAttendancePercentage', '75', 'int', 'Attendance', 'Minimum required attendance percentage', 1),
('LateThresholdMinutes', '15', 'int', 'Attendance', 'Minutes after which a student is marked late', 1);
GO
