//using AMS.TempScaffold.Entities;

namespace AMS.Models.Entities
{
    public class TimetableSlot
    {
        public int SlotId { get; set; }

        public int? TimetableId { get; set; }

        public int? CourseAssignmentId { get; set; }

        public int? DayOfWeek { get; set; }

        public TimeOnly? StartTime { get; set; }

        public TimeOnly? EndTime { get; set; }

        public virtual CourseAssignment? CourseAssignment { get; set; }

        public virtual Timetable? Timetable { get; set; }
    }
}
