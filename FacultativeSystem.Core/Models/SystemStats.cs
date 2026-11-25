namespace FacultativeSystem.Core.Models;

public class SystemStats
{
    public int TotalCourses { get; set; }
    public int TotalTeachers { get; set; }
    public int TotalStudents { get; set; }
    public int TotalAdmins { get; set; }
    public int TotalEnrollments { get; set; }
    public int CompletedEnrollments { get; set; }
    public double AverageGrade { get; set; }
}
