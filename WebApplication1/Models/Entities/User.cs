using System;
using System.ComponentModel.DataAnnotations;

namespace AMS.Models.Entities
{
    public class User
    {
        [Key]
        public int UserId { get; set; }
        [Required, StringLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Student"; // e.g., "Student", "Teacher", "Admin"

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Admin? Admin { get; set; }
        public Teacher? Teacher { get; set; }
        public Student? Student
        {
            get; set;

        }
    }
}
