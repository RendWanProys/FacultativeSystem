namespace FacultativeSystem.Core.Models;

public record LoginRequest(string Login, string Password);

public record CreateUserRequest(
    string Login,
    string Password,
    string FullName,
    UserRole Role,
    string? StudentGroup,
    string? Email,
    bool IsActive = true);

public record UpdateUserAccessRequest(
    UserRole Role,
    bool IsActive,
    string? FullName,
    string? StudentGroup,
    string? Email,
    string? NewPassword);

public record CreateCourseRequest(
    string Title,
    string Description,
    int TeacherId,
    int TotalHours = 36);

public record UpdateCourseRequest(
    string Title,
    string Description,
    int TeacherId,
    int TotalHours);

public record CourseGroupRequest(string Name);

public record CourseFieldRequest(
    string FieldName,
    string FieldValue,
    string FieldType,
    string LessonDate,
    int? GroupId,
    int SortOrder);

public record EnrollStudentRequest(int CourseId, int StudentId, int? GroupId = null);

public record AssignStudentGroupRequest(int? GroupId);

public record GradeCommentRequest(string? Grade, string? Comment);

public record LessonGradeRequest(int FieldId, int StudentId, string? Grade, string? Comment);

public record ApiErrorResponse(string Message);
