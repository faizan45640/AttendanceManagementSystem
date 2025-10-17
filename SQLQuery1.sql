INSERT INTO Users (Username, Email, PasswordHash, Role)
VALUES ('admin1', 'admin@mail.com', '12345', 'Admin'),
       ('student1', 'student@mail.com', '12345', 'Student'),
       ('teacher1', 'teacher@mail.com', '12345', 'Teacher');
       ALTER TABLE Users
ADD CONSTRAINT DF_Users_CreatedAt DEFAULT GETDATE() FOR CreatedAt;
ALTER TABLE Users
ADD CONSTRAINT DF_Users_IsActive DEFAULT 1 FOR IsActive;
