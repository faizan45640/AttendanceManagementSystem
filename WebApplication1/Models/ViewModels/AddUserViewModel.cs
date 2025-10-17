using System.ComponentModel.DataAnnotations;

namespace AMS.Models.ViewModels
{
	public class AddUserViewModel
	{
		[Required]
		public string Username { get; set; } = string.Empty;

		[Required, EmailAddress]
		public string Email { get; set; } = string.Empty;

		[Required]
		[DataType(DataType.Password)]
		public string Password { get; set; } = string.Empty;

		[Required]
		public string Role { get; set; } = "Student"; // Default role
	}
}
