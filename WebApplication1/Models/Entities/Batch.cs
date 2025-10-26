using System.ComponentModel.DataAnnotations;

namespace AMS.Models.Entities
{
    public class Batch
    {
        public int BatchId { get; set; }

        [Required(ErrorMessage = "Batch name is required")]
        [StringLength(50, ErrorMessage = "Batch name cannot exceed 50 characters")]
        public string? BatchName { get; set; }

        [Required(ErrorMessage = "Year is required")]
        [Range(2000, 2100, ErrorMessage = "Please enter a valid year between 2000 and 2100")]
        public int Year { get; set; }

        public bool IsActive { get; set; } = true;

        //students
        public ICollection<Student>? Students { get; set; }

        // For display purposes
        public int StudentCount { get; set; }
    }
}
