using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace AMS.Models.ViewModels
{
    // Profile View Model
    public class ProfileViewModel
    {
        public string Role { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Username { get; set; }

        // Role-specific data
        public StudentProfileData? StudentData { get; set; }
        public TeacherProfileData? TeacherData { get; set; }
        public AdminProfileData? AdminData { get; set; }
    }

    public class StudentProfileData
    {
        public int StudentId { get; set; }
        public string? RollNumber { get; set; }
        public string? BatchName { get; set; }
        public string? CurrentSemester { get; set; }
        public int TotalCourses { get; set; }
        public double OverallAttendance { get; set; }
    }

    public class TeacherProfileData
    {
        public int TeacherId { get; set; }
        public int TotalCourses { get; set; }
        public int TotalBatches { get; set; }
    }

    public class AdminProfileData
    {
        public int AdminId { get; set; }
        public int TotalStudents { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalBatches { get; set; }
    }

    // Institution Settings (Admin Only)
    public class InstitutionSettingsViewModel
    {
        [Required(ErrorMessage = "Institution name is required")]
        [MaxLength(200)]
        [Display(Name = "Institution Name")]
        public string InstitutionName { get; set; } = string.Empty;

        [Display(Name = "Current Logo")]
        public string? CurrentLogoPath { get; set; }

        [Display(Name = "Upload New Logo")]
        public IFormFile? LogoFile { get; set; }

        [MaxLength(500)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        [MaxLength(20)]
        [Display(Name = "Phone")]
        public string? Phone { get; set; }

        [EmailAddress]
        [MaxLength(100)]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Display(Name = "Current Academic Year")]
        public string? AcademicYear { get; set; }
    }

    // Change Password ViewModel
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Current password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new password")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        [Display(Name = "Confirm New Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
