namespace FacultativeSystem.Core.Models;

/// <summary>
/// Роли пользователей в системе «Факультатив» согласно диаграмме вариантов использования.
/// </summary>
public enum UserRole
{
    Admin,
    Teacher,
    Student
}

public static class UserRoleExtensions
{
    public static string ToRussianName(this UserRole role) => role switch
    {
        UserRole.Admin => "Администратор",
        UserRole.Teacher => "Преподаватель",
        UserRole.Student => "Студент",
        _ => role.ToString()
    };

    public static UserRole ParseRole(string value) => value switch
    {
        "Admin" => UserRole.Admin,
        "Teacher" => UserRole.Teacher,
        "Student" => UserRole.Student,
        _ => throw new ArgumentException($"Неизвестная роль: {value}")
    };
}
