SELECT 
    T.FirstName + ' ' + T.LastName AS [Teacher Name],
    U.Email AS [Login Email],
    'Hash_P@ssword123' AS [Password] -- The common testing password we used
FROM Users U
JOIN Teachers T ON U.UserId = T.UserId
WHERE U.Role = 'Teacher';