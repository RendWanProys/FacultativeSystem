namespace FacultativeSystem.Core.Models;

/// <summary>
/// Поле факультатива — учебная пара (занятие), привязанная к предмету и группе студентов.
/// </summary>
public class CourseField
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int? GroupId { get; set; }
    public string GroupName { get; set; } = "Все группы";
    public string FieldName { get; set; } = string.Empty;
    public string FieldValue { get; set; } = string.Empty;
    public string FieldType { get; set; } = "Лекция";
    public string LessonDate { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 1;
}
