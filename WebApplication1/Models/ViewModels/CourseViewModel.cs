using System.ComponentModel.DataAnnotations;
using AMS.Models.Entities;

namespace AMS.Models.ViewModels
{
    public class CourseViewModel
    {
    }
    public class AddCourseViewModel
    {
        [Required(ErrorMessage = "Course code is required")]
        [StringLength(20, ErrorMessage = "Course code cannot exceed 20 characters")]
        public string CourseCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Course name is required")]
        [StringLength(100, ErrorMessage = "Course name cannot exceed 100 characters")]
        public string CourseName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Credit hours is required")]
        [Range(1, 10, ErrorMessage = "Credit hours must be between 1 and 10")]
        public int CreditHours { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class CourseFilterViewModel : PagedViewModel
    {
        public IEnumerable<Course> Courses { get; set; } = new List<Course>();
        public string? CourseCode { get; set; }
        public string? CourseName { get; set; }
        public string? Status { get; set; }
    }
}
