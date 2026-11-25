using FacultativeSystem.Core.Models;

namespace FacultativeSystem.Core.Services;

/// <summary>
/// Контракт клиентского взаимодействия с ИС «Факультатив» (через REST API).
/// </summary>
public interface IFacultativeClient
{
    Task<bool> CheckHealthAsync();

    // Аутентификация
    Task<User?> AuthenticateAsync(string login, string password);

    // Пользователи и доступы
    Task<SystemStats> GetSystemStatsAsync();
    Task<List<User>> GetAllUsersAsync(UserRole? roleFilter = null);
    Task<List<User>> GetActiveTeachersAsync();
    Task<List<User>> GetActiveStudentsAsync();
    Task<User> CreateUserAsync(string login, string password, string fullName, UserRole role, string? studentGroup, string? email, bool isActive = true);
    Task UpdateUserAccessAsync(int userId, UserRole newRole, bool isActive, string? fullName = null, string? studentGroup = null, string? email = null, string? newPassword = null);
    Task DeleteUserAsync(int userId, int currentActorId);

    // Факультативные курсы
    Task<List<Course>> GetCoursesAsync(int? teacherId = null, string? search = null);
    Task<List<Course>> GetCoursesForStudentAsync(int studentId, string? search = null);
    Task<Course> CreateCourseAsync(string title, string description, int teacherId, int totalHours = 36);
    Task UpdateCourseAsync(int courseId, string title, string description, int teacherId, int totalHours);
    Task DeleteCourseAsync(int courseId);

    // Учебные группы (администратор) и привязка к факультативам/студентам
    Task<List<CourseGroup>> GetAllGroupsAsync();
    Task<CourseGroup> CreateGroupAsync(string name);
    Task RenameGroupAsync(int groupId, string newName);
    Task DeleteGroupAsync(int groupId);
    Task<List<User>> GetGroupStudentsAsync(int groupId);
    Task<List<Course>> GetGroupCoursesAsync(int groupId);
    Task AddStudentToGroupAsync(int groupId, int studentId);
    Task RemoveStudentFromGroupAsync(int groupId, int studentId);
    Task BindGroupToCourseAsync(int courseId, int groupId);
    Task UnbindGroupFromCourseAsync(int courseId, int groupId);
    Task<List<CourseGroup>> GetCourseGroupsAsync(int courseId);

    // Поля факультета (пары / занятия)
    Task<List<CourseField>> GetCourseFieldsAsync(int courseId, int? studentId = null);
    Task<CourseField> AddCourseFieldAsync(int courseId, string fieldName, string? fieldValue, string fieldType, string? lessonDate, int? groupId, int sortOrder = 1);
    Task UpdateCourseFieldAsync(int fieldId, string fieldName, string? fieldValue, string fieldType, string? lessonDate, int? groupId, int sortOrder);
    Task DeleteCourseFieldAsync(int fieldId);

    // Студенты на курсе, итоговые зачёты/оценки и оценки за пары
    Task<List<Enrollment>> GetCourseEnrollmentsAsync(int courseId);
    Task<List<Enrollment>> GetStudentEnrollmentsAsync(int studentId);
    Task<Enrollment> EnrollStudentAsync(int courseId, int studentId, int? groupId = null);
    Task AssignStudentToGroupAsync(int enrollmentId, int? groupId);
    Task RemoveEnrollmentAsync(int enrollmentId);
    Task SetEnrollmentGradeAsync(int enrollmentId, string? grade, string? comment);

    Task<List<LessonGradeEntry>> GetLessonGradesForCourseAsync(int courseId);
    Task<List<LessonGradeEntry>> GetLessonGradesForFieldAsync(int fieldId);
    Task<List<LessonGradeEntry>> GetStudentLessonGradesAsync(int courseId, int studentId);
    Task SetLessonGradeAsync(int fieldId, int studentId, string? grade, string? comment);

    // Резервное копирование
    Task<byte[]> DownloadBackupAsync();
}
