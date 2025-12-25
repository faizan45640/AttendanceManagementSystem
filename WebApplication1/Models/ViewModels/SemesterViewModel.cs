using System.ComponentModel.DataAnnotations;
using AMS.Models.Entities;

namespace AMS.Models.ViewModels
{
    public class SemesterViewModel
    {
    }
    public class AddSemesterViewModel
    {
        [Required(ErrorMessage = "Semester name is required")]
        [StringLength(100, ErrorMessage = "Semester name cannot exceed 100 characters")]
        [Display(Name = "Semester Name")]
        public string SemesterName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Year is required")]
        [Range(2000, 2100, ErrorMessage = "Please enter a valid year between 2000 and 2100")]
        [Display(Name = "Year")]
        public int Year { get; set; }

        [Required(ErrorMessage = "Start date is required")]
        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateOnly StartDate { get; set; }

        [Required(ErrorMessage = "End date is required")]
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateOnly EndDate { get; set; }

        [Display(Name = "Active Status")]
        public bool IsActive { get; set; } = true;
    }
    public class SemesterFilterViewModel : PagedViewModel
    {
        public List<Semester> Semesters { get; set; } = new List<Semester>();
        public int? Year { get; set; }
        public string? Status { get; set; }
    }
}
