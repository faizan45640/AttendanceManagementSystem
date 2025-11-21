using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using AMS.Models.Entities;

namespace AMS.Models.ViewModels
{
    public class EnrollmentViewModel
    {
    }
    public class EnrollmentFilterViewModel
    {
        // Filter Properties
        public int? StudentId { get; set; }

        [Display(Name = "Course")]
        public int? CourseId { get; set; }

        [Display(Name = "Semester")]
        public int? SemesterId { get; set; }

        [Display(Name = "Status")]
        public string? Status { get; set; }

        // Data Collections
        public List<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

        // Dropdown Lists
        public List<SelectListItem> Students { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Courses { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Semesters { get; set; } = new List<SelectListItem>();
    }
    public class AddEnrollmentViewModel
    {
        [Required(ErrorMessage = "Please select a student")]
        [Display(Name = "Student")]
        public int? StudentId { get; set; }

        [Required(ErrorMessage = "Please select a course")]
        [Display(Name = "Course")]
        public int? CourseId { get; set; }

        [Required(ErrorMessage = "Please select a semester")]
        [Display(Name = "Semester")]
        public int? SemesterId { get; set; }

        [Required(ErrorMessage = "Please select a status")]
        [Display(Name = "Status")]
        public string? Status { get; set; }

        // Dropdown Lists
        public List<SelectListItem> Students { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Courses { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Semesters { get; set; } = new List<SelectListItem>();
    }

    public class BulkEnrollViewModel
    {
        [Required(ErrorMessage = "Please select a batch")]
        public int BatchId { get; set; }

        [Required(ErrorMessage = "Please select a semester")]
        public int SemesterId { get; set; }

        [Required(ErrorMessage = "Please select a course")]
        public int CourseId { get; set; }

        public SelectList? Batches { get; set; }
        public SelectList? Semesters { get; set; }
        public SelectList? Courses { get; set; }
    }

}
