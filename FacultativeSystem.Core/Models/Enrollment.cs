namespace FacultativeSystem.Core.Models;

/// <summary>
/// Зачисление студента в факультатив (и в группу предмета), итоговый зачет/оценка и необязательный комментарий.
/// </summary>
public class Enrollment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string? StudentGroup { get; set; }
    public int? CourseGroupId { get; set; }
    public string CourseGroupName { get; set; } = "Без группы";
    public string EnrolledAt { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string? Grade { get; set; }
    public string? Comment { get; set; }
    public string? GradedAt { get; set; }

    public bool IsCompleted => !string.IsNullOrWhiteSpace(Grade);
    public string StatusDisplay => IsCompleted ? "Аттестован" : "Обучается";
    public string GradeDisplay => GradeOptions.FormatDisplay(Grade);
}
