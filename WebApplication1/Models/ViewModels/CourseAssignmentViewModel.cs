using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using AMS.Models.Entities;

namespace AMS.Models.ViewModels
{
    public class CourseAssignmentViewModel
    {
    }
    public class CourseAssignmentFilterViewModel : PagedViewModel
    {
        [Display(Name = "Teacher")]
        public int? TeacherId { get; set; }

        [Display(Name = "Course")]
        public int? CourseId { get; set; }

        [Display(Name = "Batch")]
        public int? BatchId { get; set; }

        [Display(Name = "Semester")]
        public int? SemesterId { get; set; }

        [Display(Name = "Status")]
        public string? Status { get; set; } // "Active", "Inactive", or null for all

        public List<CourseAssignment> CourseAssignments { get; set; } = new List<CourseAssignment>();

        // For dropdowns
        public List<SelectListItem> Teachers { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Courses { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Batches { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Semesters { get; set; } = new List<SelectListItem>();
    }
    public class AddCourseAssignmentViewModel
    {
        [Required(ErrorMessage = "Please select a teacher")]
        [Display(Name = "Teacher")]
        public int? TeacherId { get; set; }

        [Required(ErrorMessage = "Please select a course")]
        [Display(Name = "Course")]
        public int? CourseId { get; set; }

        [Required(ErrorMessage = "Please select a batch")]
        [Display(Name = "Batch")]
        public int? BatchId { get; set; }

        [Required(ErrorMessage = "Please select a semester")]
        [Display(Name = "Semester")]
        public int? SemesterId { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        // For dropdowns
        public List<SelectListItem> Teachers { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Courses { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Batches { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Semesters { get; set; } = new List<SelectListItem>();
    }
}