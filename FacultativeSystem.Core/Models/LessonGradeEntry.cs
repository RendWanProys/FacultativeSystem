namespace FacultativeSystem.Core.Models;

/// <summary>
/// Оценка студента за конкретную пару (поле факультатива) и необязательная заметка.
/// </summary>
public class LessonGradeEntry
{
    public int FieldId { get; set; }
    public int CourseId { get; set; }
    public string LessonTitle { get; set; } = string.Empty;
    public string LessonType { get; set; } = string.Empty;
    public string LessonDate { get; set; } = string.Empty;
    public string TargetGroupName { get; set; } = "Все группы";
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string CourseGroupName { get; set; } = "Без группы";
    public string? Grade { get; set; }
    public string? Comment { get; set; }
    public string? GradedAt { get; set; }

    public string GradeDisplay => GradeOptions.FormatDisplay(Grade);
}
