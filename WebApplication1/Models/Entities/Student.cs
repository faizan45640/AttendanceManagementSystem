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
		public string RollNumber { get; set; }

		[Required]
		public string FirstName { get; set; }

		[Required]
		public string LastName { get; set; }

		public bool IsActive { get; set; } = true;

		public int? BatchId { get; set; } // optional for now
	}
}
