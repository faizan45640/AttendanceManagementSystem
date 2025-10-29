//using AMS.TempScaffold.Entities;

namespace AMS.Models.Entities
{
    public class Timetable
    {
        public int TimetableId { get; set; }

        public int? BatchId { get; set; }

        public int? SemesterId { get; set; }

        public bool? IsActive { get; set; }

        public virtual Batch? Batch { get; set; }

        public virtual Semester? Semester { get; set; }

        public virtual ICollection<TimetableSlot> TimetableSlots { get; set; } = new List<TimetableSlot>();
    }
}
