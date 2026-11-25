using System.Text.Json.Serialization;

namespace FacultativeSystem.Core.Models;

/// <summary>
/// Учетная запись пользователя (Администратор, Преподаватель, Студент).
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Login { get; set; } = string.Empty;

    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? StudentGroup { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;

    public string RoleDisplay => Role.ToRussianName();
    public string StatusDisplay => IsActive ? "Активен" : "Доступ закрыт";
}
