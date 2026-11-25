using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacultativeSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EnrollmentsController : ControllerBase
{
    private readonly FacultativeService _service;

    public EnrollmentsController(FacultativeService service)
    {
        _service = service;
    }

    [HttpGet("course/{courseId:int}")]
    public ActionResult<List<Enrollment>> GetByCourse(int courseId)
        => Ok(_service.GetCourseEnrollments(courseId));

    [HttpGet("student/{studentId:int}")]
    public ActionResult<List<Enrollment>> GetByStudent(int studentId)
        => Ok(_service.GetStudentEnrollments(studentId));

    [HttpPost]
    public ActionResult<Enrollment> Enroll([FromBody] EnrollStudentRequest request)
        => Ok(_service.EnrollStudent(request.CourseId, request.StudentId, request.GroupId));

    [HttpPut("{id:int}/group")]
    public IActionResult AssignGroup(int id, [FromBody] AssignStudentGroupRequest request)
    {
        _service.AssignStudentToGroup(id, request.GroupId);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Remove(int id)
    {
        _service.RemoveEnrollment(id);
        return NoContent();
    }

    [HttpPut("{id:int}/grade")]
    public IActionResult SetGrade(int id, [FromBody] GradeCommentRequest request)
    {
        _service.SetEnrollmentGrade(id, request.Grade, request.Comment);
        return NoContent();
    }

    // --- Оценки за конкретные пары (поля) ---

    [HttpGet("course/{courseId:int}/lessons")]
    public ActionResult<List<LessonGradeEntry>> GetCourseLessonGrades(int courseId)
        => Ok(_service.GetLessonGradesForCourse(courseId));

    [HttpGet("lesson/{fieldId:int}")]
    public ActionResult<List<LessonGradeEntry>> GetLessonGrades(int fieldId)
        => Ok(_service.GetLessonGradesForField(fieldId));

    [HttpGet("course/{courseId:int}/student/{studentId:int}/lessons")]
    public ActionResult<List<LessonGradeEntry>> GetStudentLessonGrades(int courseId, int studentId)
        => Ok(_service.GetStudentLessonGrades(courseId, studentId));

    [HttpPut("lesson/grade")]
    public IActionResult SetLessonGrade([FromBody] LessonGradeRequest request)
    {
        _service.SetLessonGrade(request.FieldId, request.StudentId, request.Grade, request.Comment);
        return NoContent();
    }
}
