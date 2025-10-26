using AMS.Models.Entities;
using System.ComponentModel.DataAnnotations;

namespace AMS.Models.ViewModels
{
    public class AddStudentViewModel
    {

        public int? UserId { get; set; } //only for editing existing users or deleting
        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string RollNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required]
        public int BatchId { get; set; }
    }

    public class EditStudentViewModel
    {
        public int StudentId { get; set; }
        public int UserId { get; set; }

        [Required]
        [StringLength(50)]
        public string? Username { get; set; }

        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [MinLength(6)]
        public string? Password { get; set; } // Optional for edit

        [Required]
        [StringLength(20)]
        public string? RollNumber { get; set; }

        [Required]
        [StringLength(50)]
        public string? FirstName { get; set; }

        [Required]
        [StringLength(50)]
        public string? LastName { get; set; }

        [Required]
        public int BatchId { get; set; }

        public bool IsActive { get; set; }
    }


    /*
    public class StudentCourseViewModel
    {
        public Student Student { get; set; }
        public List<string> Courses { get; set; }
    }*/
}