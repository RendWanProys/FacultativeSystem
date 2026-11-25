namespace FacultativeSystem.Core.Models;

/// <summary>
/// Учебная группа, управляемая администратором и привязываемая к факультативным курсам.
/// </summary>
public class CourseGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StudentsCount { get; set; }
    public int CoursesCount { get; set; }
}
