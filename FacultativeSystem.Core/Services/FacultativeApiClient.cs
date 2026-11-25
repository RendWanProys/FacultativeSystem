using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FacultativeSystem.Core.Models;

namespace FacultativeSystem.Core.Services;

/// <summary>
/// HTTP REST-клиент для взаимодействия WPF-приложения с сервером FacultativeSystem.Api.
/// </summary>
public class FacultativeApiClient : IFacultativeClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FacultativeApiClient(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public FacultativeApiClient(string baseUrl = "http://localhost:5150")
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            using var resp = await _http.GetAsync("api/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<User?> AuthenticateAsync(string login, string password)
    {
        using var resp = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(login, password), JsonOptions);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return null;
        await EnsureSuccessOrThrowAsync(resp);
        return await resp.Content.ReadFromJsonAsync<User>(JsonOptions);
    }

    public async Task<SystemStats> GetSystemStatsAsync()
    {
        using var resp = await _http.GetAsync("api/system/stats");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<SystemStats>(JsonOptions))!;
    }

    public async Task<List<User>> GetAllUsersAsync(UserRole? roleFilter = null)
    {
        var url = roleFilter.HasValue ? $"api/users?role={roleFilter.Value}" : "api/users";
        using var resp = await _http.GetAsync(url);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<User>>(JsonOptions)) ?? new();
    }

    public async Task<List<User>> GetActiveTeachersAsync()
    {
        using var resp = await _http.GetAsync("api/users/teachers");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<User>>(JsonOptions)) ?? new();
    }

    public async Task<List<User>> GetActiveStudentsAsync()
    {
        using var resp = await _http.GetAsync("api/users/students");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<User>>(JsonOptions)) ?? new();
    }

    public async Task<User> CreateUserAsync(string login, string password, string fullName, UserRole role, string? studentGroup, string? email, bool isActive = true)
    {
        using var resp = await _http.PostAsJsonAsync(
            "api/users",
            new CreateUserRequest(login, password, fullName, role, studentGroup, email, isActive),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<User>(JsonOptions))!;
    }

    public async Task UpdateUserAccessAsync(int userId, UserRole newRole, bool isActive, string? fullName = null, string? studentGroup = null, string? email = null, string? newPassword = null)
    {
        using var resp = await _http.PutAsJsonAsync(
            $"api/users/{userId}/access",
            new UpdateUserAccessRequest(newRole, isActive, fullName, studentGroup, email, newPassword),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task DeleteUserAsync(int userId, int currentActorId)
    {
        using var resp = await _http.DeleteAsync($"api/users/{userId}?actorId={currentActorId}");
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task<List<Course>> GetCoursesAsync(int? teacherId = null, string? search = null)
    {
        var query = new List<string>();
        if (teacherId.HasValue) query.Add($"teacherId={teacherId.Value}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        var url = query.Count > 0 ? "api/courses?" + string.Join("&", query) : "api/courses";

        using var resp = await _http.GetAsync(url);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<Course>>(JsonOptions)) ?? new();
    }

    public async Task<List<Course>> GetCoursesForStudentAsync(int studentId, string? search = null)
    {
        var url = string.IsNullOrWhiteSpace(search)
            ? $"api/courses/student/{studentId}"
            : $"api/courses/student/{studentId}?search={Uri.EscapeDataString(search)}";

        using var resp = await _http.GetAsync(url);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<Course>>(JsonOptions)) ?? new();
    }

    public async Task<Course> CreateCourseAsync(string title, string description, int teacherId, int totalHours = 36)
    {
        using var resp = await _http.PostAsJsonAsync(
            "api/courses",
            new CreateCourseRequest(title, description, teacherId, totalHours),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<Course>(JsonOptions))!;
    }

    public async Task UpdateCourseAsync(int courseId, string title, string description, int teacherId, int totalHours)
    {
        using var resp = await _http.PutAsJsonAsync(
            $"api/courses/{courseId}",
            new UpdateCourseRequest(title, description, teacherId, totalHours),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task DeleteCourseAsync(int courseId)
    {
        using var resp = await _http.DeleteAsync($"api/courses/{courseId}");
        await EnsureSuccessOrThrowAsync(resp);
    }

    // --- Группы ---

    public async Task<List<CourseGroup>> GetAllGroupsAsync()
    {
        using var resp = await _http.GetAsync("api/groups");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<CourseGroup>>(JsonOptions)) ?? new();
    }

    public async Task<CourseGroup> CreateGroupAsync(string name)
    {
        using var resp = await _http.PostAsJsonAsync("api/groups", new CourseGroupRequest(name), JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<CourseGroup>(JsonOptions))!;
    }

    public async Task RenameGroupAsync(int groupId, string newName)
    {
        using var resp = await _http.PutAsJsonAsync($"api/groups/{groupId}", new CourseGroupRequest(newName), JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task DeleteGroupAsync(int groupId)
    {
        using var resp = await _http.DeleteAsync($"api/groups/{groupId}");
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task<List<User>> GetGroupStudentsAsync(int groupId)
    {
        using var resp = await _http.GetAsync($"api/groups/{groupId}/students");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<User>>(JsonOptions)) ?? new();
    }

    public async Task<List<Course>> GetGroupCoursesAsync(int groupId)
    {
        using var resp = await _http.GetAsync($"api/groups/{groupId}/courses");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<Course>>(JsonOptions)) ?? new();
    }

    public async Task AddStudentToGroupAsync(int groupId, int studentId)
    {
        using var resp = await _http.PostAsync($"api/groups/{groupId}/students/{studentId}", null);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task RemoveStudentFromGroupAsync(int groupId, int studentId)
    {
        using var resp = await _http.DeleteAsync($"api/groups/{groupId}/students/{studentId}");
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task BindGroupToCourseAsync(int courseId, int groupId)
    {
        using var resp = await _http.PostAsync($"api/groups/{groupId}/courses/{courseId}", null);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task UnbindGroupFromCourseAsync(int courseId, int groupId)
    {
        using var resp = await _http.DeleteAsync($"api/groups/{groupId}/courses/{courseId}");
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task<List<CourseGroup>> GetCourseGroupsAsync(int courseId)
    {
        using var resp = await _http.GetAsync($"api/courses/{courseId}/groups");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<CourseGroup>>(JsonOptions)) ?? new();
    }

    // --- Поля (пары) ---

    public async Task<List<CourseField>> GetCourseFieldsAsync(int courseId, int? studentId = null)
    {
        var url = studentId.HasValue
            ? $"api/courses/{courseId}/fields?studentId={studentId.Value}"
            : $"api/courses/{courseId}/fields";
        using var resp = await _http.GetAsync(url);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<CourseField>>(JsonOptions)) ?? new();
    }

    public async Task<CourseField> AddCourseFieldAsync(int courseId, string fieldName, string? fieldValue, string fieldType, string? lessonDate, int? groupId, int sortOrder = 1)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"api/courses/{courseId}/fields",
            new CourseFieldRequest(fieldName, fieldValue ?? string.Empty, fieldType, lessonDate ?? string.Empty, groupId, sortOrder),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<CourseField>(JsonOptions))!;
    }

    public async Task UpdateCourseFieldAsync(int fieldId, string fieldName, string? fieldValue, string fieldType, string? lessonDate, int? groupId, int sortOrder)
    {
        using var resp = await _http.PutAsJsonAsync(
            $"api/courses/fields/{fieldId}",
            new CourseFieldRequest(fieldName, fieldValue ?? string.Empty, fieldType, lessonDate ?? string.Empty, groupId, sortOrder),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task DeleteCourseFieldAsync(int fieldId)
    {
        using var resp = await _http.DeleteAsync($"api/courses/fields/{fieldId}");
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task<List<Enrollment>> GetCourseEnrollmentsAsync(int courseId)
    {
        using var resp = await _http.GetAsync($"api/enrollments/course/{courseId}");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<Enrollment>>(JsonOptions)) ?? new();
    }

    public async Task<List<Enrollment>> GetStudentEnrollmentsAsync(int studentId)
    {
        using var resp = await _http.GetAsync($"api/enrollments/student/{studentId}");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<Enrollment>>(JsonOptions)) ?? new();
    }

    public async Task<Enrollment> EnrollStudentAsync(int courseId, int studentId, int? groupId = null)
    {
        using var resp = await _http.PostAsJsonAsync(
            "api/enrollments",
            new EnrollStudentRequest(courseId, studentId, groupId),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<Enrollment>(JsonOptions))!;
    }

    public async Task AssignStudentToGroupAsync(int enrollmentId, int? groupId)
    {
        using var resp = await _http.PutAsJsonAsync(
            $"api/enrollments/{enrollmentId}/group",
            new AssignStudentGroupRequest(groupId),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task RemoveEnrollmentAsync(int enrollmentId)
    {
        using var resp = await _http.DeleteAsync($"api/enrollments/{enrollmentId}");
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task SetEnrollmentGradeAsync(int enrollmentId, string? grade, string? comment)
    {
        using var resp = await _http.PutAsJsonAsync(
            $"api/enrollments/{enrollmentId}/grade",
            new GradeCommentRequest(grade, comment),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task<List<LessonGradeEntry>> GetLessonGradesForCourseAsync(int courseId)
    {
        using var resp = await _http.GetAsync($"api/enrollments/course/{courseId}/lessons");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<LessonGradeEntry>>(JsonOptions)) ?? new();
    }

    public async Task<List<LessonGradeEntry>> GetLessonGradesForFieldAsync(int fieldId)
    {
        using var resp = await _http.GetAsync($"api/enrollments/lesson/{fieldId}");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<LessonGradeEntry>>(JsonOptions)) ?? new();
    }

    public async Task<List<LessonGradeEntry>> GetStudentLessonGradesAsync(int courseId, int studentId)
    {
        using var resp = await _http.GetAsync($"api/enrollments/course/{courseId}/student/{studentId}/lessons");
        await EnsureSuccessOrThrowAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<List<LessonGradeEntry>>(JsonOptions)) ?? new();
    }

    public async Task SetLessonGradeAsync(int fieldId, int studentId, string? grade, string? comment)
    {
        using var resp = await _http.PutAsJsonAsync(
            "api/enrollments/lesson/grade",
            new LessonGradeRequest(fieldId, studentId, grade, comment),
            JsonOptions);
        await EnsureSuccessOrThrowAsync(resp);
    }

    public async Task<byte[]> DownloadBackupAsync()
    {
        using var resp = await _http.GetAsync("api/system/backup/download");
        await EnsureSuccessOrThrowAsync(resp);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
            return;

        string message = $"Ошибка API ({(int)resp.StatusCode})";
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);
            if (!string.IsNullOrWhiteSpace(err?.Message))
                message = err.Message;
        }
        catch
        {
            var raw = await resp.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(raw))
                message = raw;
        }

        throw new InvalidOperationException(message);
    }
}
