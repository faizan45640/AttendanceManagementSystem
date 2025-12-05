using AMS.Models.Entities;
using System.ComponentModel.DataAnnotations;

namespace AMS.Models.ViewModels
{
    public class AdminFilterViewModel
    {
        [Display(Name = "Search")]
        public string? Search { get; set; }

        [Display(Name = "From Date")]
        [DataType(DataType.Date)]
        public DateTime? FromDate { get; set; }

        [Display(Name = "To Date")]
        [DataType(DataType.Date)]
        public DateTime? ToDate { get; set; }

        [Display(Name = "Status")]
        public string? Status { get; set; } // "Active", "Inactive", or null for all

        public List<Admin> Admins { get; set; } = new List<Admin>();
    }
}
