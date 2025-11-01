using System.ComponentModel.DataAnnotations;

namespace AMS.Models.ViewModels
{
    public class BatchViewModel
    {
    }
    public class AddBatchViewModel
    {
        [Required(ErrorMessage = "Batch name is required")]
        [StringLength(100, ErrorMessage = "Batch name cannot exceed 100 characters")]
        [Display(Name = "Batch Name")]
        public string BatchName { get; set; }

        [Required(ErrorMessage = "Year is required")]
        [Range(2000, 2100, ErrorMessage = "Please enter a valid year between 2000 and 2100")]
        [Display(Name = "Year")]
        public int Year { get; set; }

        [Display(Name = "Active Status")]
        public bool IsActive { get; set; } = true;
    }
}
