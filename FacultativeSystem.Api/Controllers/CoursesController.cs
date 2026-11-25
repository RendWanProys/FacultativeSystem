using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacultativeSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoursesController : ControllerBase
{
    private readonly FacultativeService _service;

    public CoursesController(FacultativeService service)
    {
        _service = service;
    }

    [HttpGet]
    public ActionResult<List<Course>> GetCourses([FromQuery] int? teacherId = null, [FromQuery] string? search = null)
        => Ok(_service.GetCourses(teacherId, search));

    [HttpGet("student/{studentId:int}")]
    public ActionResult<List<Course>> GetCoursesForStudent(int studentId, [FromQuery] string? search = null)
        => Ok(_service.GetCoursesForStudent(studentId, search));

    [HttpPost]
    public ActionResult<Course> CreateCourse([FromBody] CreateCourseRequest request)
    {
        var created = _service.CreateCourse(
            request.Title,
            request.Description,
            request.TeacherId,
            request.TotalHours);
        return Ok(created);
    }

    [HttpPut("{id:int}")]
    public IActionResult UpdateCourse(int id, [FromBody] UpdateCourseRequest request)
    {
        _service.UpdateCourse(
            id,
            request.Title,
            request.Description,
            request.TeacherId,
            request.TotalHours);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult DeleteCourse(int id)
    {
        _service.DeleteCourse(id);
        return NoContent();
    }

    [HttpGet("{courseId:int}/groups")]
    public ActionResult<List<CourseGroup>> GetGroups(int courseId)
        => Ok(_service.GetCourseGroups(courseId));

    // --- Поля факультатива (пары / занятия) ---

    [HttpGet("{courseId:int}/fields")]
    public ActionResult<List<CourseField>> GetFields(int courseId, [FromQuery] int? studentId = null)
        => Ok(_service.GetCourseFields(courseId, studentId));

    [HttpPost("{courseId:int}/fields")]
    public ActionResult<CourseField> AddField(int courseId, [FromBody] CourseFieldRequest request)
    {
        var field = _service.AddCourseField(
            courseId,
            request.FieldName,
            request.FieldValue,
            request.FieldType,
            request.LessonDate,
            request.GroupId,
            request.SortOrder);
        return Ok(field);
    }

    [HttpPut("fields/{fieldId:int}")]
    public IActionResult UpdateField(int fieldId, [FromBody] CourseFieldRequest request)
    {
        _service.UpdateCourseField(
            fieldId,
            request.FieldName,
            request.FieldValue,
            request.FieldType,
            request.LessonDate,
            request.GroupId,
            request.SortOrder);
        return NoContent();
    }

    [HttpDelete("fields/{fieldId:int}")]
    public IActionResult DeleteField(int fieldId)
    {
        _service.DeleteCourseField(fieldId);
        return NoContent();
    }
}
