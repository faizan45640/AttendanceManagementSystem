using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMS.Models.Entities
{
	public class Student
	{
		[Key]
		public int StudentId { get; set; }

		[Required]
		public int UserId { get; set; }

		[ForeignKey("UserId")]
		public User User { get; set; }

		[Required]
		public string RollNumber { get; set; } = string.Empty;

        [Required]
		public string FirstName { get; set; } = string.Empty;

        [Required]
		public string LastName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
		public Batch? Batch { get; set; }
        public int? BatchId { get; set; } // optional for now
	}
}
