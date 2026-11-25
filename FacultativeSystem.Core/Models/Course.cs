namespace FacultativeSystem.Core.Models;

/// <summary>
/// Факультативный курс (предмет). За каждым курсом закреплен строго один преподаватель.
/// </summary>
public class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TeacherId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public int TotalHours { get; set; } = 36;
    public int EnrolledCount { get; set; }
    public int FieldsCount { get; set; }
    public int GroupsCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

    public string CapacityDisplay => $"{EnrolledCount} чел.";
    public string HoursDisplay => $"{TotalHours} ч.";
}
