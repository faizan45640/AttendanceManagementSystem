using AMS.Models.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace AMS.Models.ViewModels
{
    public class AddStudentViewModel
    {

        
        [Required]
        [StringLength(50)]
        //add unique message display

        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(20,MinimumLength =6 , ErrorMessage ="Password must be between 6 and 20 characters")]
        //validation message
       
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

        [Required(ErrorMessage = "Please select a batch")]
        public int BatchId { get; set; }
    }

    public class EditStudentViewModel
    {
        public int StudentId { get; set; }

        public int UserId { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [StringLength(20, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 20 characters")]
        public string? Password { get; set; }

        [Required]
        [StringLength(20)]
        public string RollNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a batch")]
        public int BatchId { get; set; }

        public bool IsActive { get; set; } = true;
    }
    public class StudentFilterViewModel
    {

        [Display(Name = "Name")]
        public string? Name { get; set; }

        [Display(Name = "Roll Number")]
        public string? RollNumber { get; set; }

        


        [Display(Name = "From Date")]
        [DataType(DataType.Date)]
        public DateTime? FromDate { get; set; }

        [Display(Name = "To Date")]
        [DataType(DataType.Date)]
        public DateTime? ToDate { get; set; }

        [Display(Name = "Status")]
        public string? Status { get; set; } // "Active", "Inactive", or null for all


        //for batch
        public int? BatchId { get; set; }
        public int? CourseId { get; set; }
        public List<Student> Students { get; set; } = new List<Student>();
        // For dropdowns
        public List<SelectListItem> Batches { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Courses { get; set; } = new List<SelectListItem>();
    }

    /*
    public class StudentCourseViewModel
    {
        public Student Student { get; set; }
        public List<string> Courses { get; set; }
    }*/
}