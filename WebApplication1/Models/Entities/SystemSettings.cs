using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AMS.Models.Entities
{
    [Table("SystemSettings")]
    public class SystemSetting
    {
        [Key]
        public int SettingId { get; set; }

        [Required]
        [MaxLength(100)]
        public string SettingKey { get; set; } = string.Empty;

        public string? SettingValue { get; set; }

        [MaxLength(50)]
        public string? SettingType { get; set; }

        [MaxLength(100)]
        public string? Category { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        public bool IsEditable { get; set; } = true;

        public DateTime? UpdatedAt { get; set; }

        public int? UpdatedBy { get; set; }
    }
}
