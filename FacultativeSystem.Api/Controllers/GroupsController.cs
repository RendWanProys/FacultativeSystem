using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacultativeSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GroupsController : ControllerBase
{
    private readonly FacultativeService _service;

    public GroupsController(FacultativeService service)
    {
        _service = service;
    }

    [HttpGet]
    public ActionResult<List<CourseGroup>> GetAll()
        => Ok(_service.GetAllGroups());

    [HttpPost]
    public ActionResult<CourseGroup> Create([FromBody] CourseGroupRequest request)
        => Ok(_service.CreateGroup(request.Name));

    [HttpPut("{id:int}")]
    public IActionResult Rename(int id, [FromBody] CourseGroupRequest request)
    {
        _service.RenameGroup(id, request.Name);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        _service.DeleteGroup(id);
        return NoContent();
    }

    [HttpGet("{id:int}/students")]
    public ActionResult<List<User>> GetStudents(int id)
        => Ok(_service.GetGroupStudents(id));

    [HttpPost("{id:int}/students/{studentId:int}")]
    public IActionResult AddStudent(int id, int studentId)
    {
        _service.AddStudentToGroup(id, studentId);
        return NoContent();
    }

    [HttpDelete("{id:int}/students/{studentId:int}")]
    public IActionResult RemoveStudent(int id, int studentId)
    {
        _service.RemoveStudentFromGroup(id, studentId);
        return NoContent();
    }

    [HttpGet("{id:int}/courses")]
    public ActionResult<List<Course>> GetCourses(int id)
        => Ok(_service.GetGroupCourses(id));

    [HttpPost("{id:int}/courses/{courseId:int}")]
    public IActionResult BindToCourse(int id, int courseId)
    {
        _service.BindGroupToCourse(courseId, id);
        return NoContent();
    }

    [HttpDelete("{id:int}/courses/{courseId:int}")]
    public IActionResult UnbindFromCourse(int id, int courseId)
    {
        _service.UnbindGroupFromCourse(courseId, id);
        return NoContent();
    }
}
