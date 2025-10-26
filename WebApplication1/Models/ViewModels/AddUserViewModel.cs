using System.ComponentModel.DataAnnotations;

namespace AMS.Models.ViewModels
{
	public class AddUserViewModel
	{

		public int? UserId { get; set; } //only for editing existing users or deleting
        [Required]
		public string Username { get; set; } = string.Empty;

		[Required, EmailAddress]
		public string Email { get; set; } = string.Empty;

		[Required]
		[DataType(DataType.Password)]
		public string Password { get; set; } = string.Empty;

		[Required]
		public string Role { get; set; } = "Student"; // Default role

		public string RollNumber { get; set; } = string.Empty;

		public string FirstName { get; set; } = string.Empty;

		public string LastName { get; set; } = string.Empty;

		

       
    }
}
