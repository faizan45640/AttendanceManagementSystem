using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMS.Models.Entities
{
    public class Admin
    {
        [Key]
        public int AdminId { get; set; }


        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
		public User? User { get; set; }

        [Required]
        public string FirstName { get; set; } = string.Empty;

		[Required]
		public string LastName { get; set; } = string.Empty;

	}
}
