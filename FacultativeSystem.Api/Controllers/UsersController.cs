using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacultativeSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly FacultativeService _service;

    public UsersController(FacultativeService service)
    {
        _service = service;
    }

    [HttpGet]
    public ActionResult<List<User>> GetAll([FromQuery] UserRole? role = null)
        => Ok(_service.GetAllUsers(role));

    [HttpGet("teachers")]
    public ActionResult<List<User>> GetActiveTeachers()
        => Ok(_service.GetActiveTeachers());

    [HttpGet("students")]
    public ActionResult<List<User>> GetActiveStudents()
        => Ok(_service.GetActiveStudents());

    [HttpPost]
    public ActionResult<User> Create([FromBody] CreateUserRequest request)
    {
        var user = _service.CreateUser(
            request.Login,
            request.Password,
            request.FullName,
            request.Role,
            request.StudentGroup,
            request.Email,
            request.IsActive);
        return Ok(user);
    }

    [HttpPut("{id:int}/access")]
    public IActionResult UpdateAccess(int id, [FromBody] UpdateUserAccessRequest request)
    {
        _service.UpdateUserAccess(
            id,
            request.Role,
            request.IsActive,
            request.FullName,
            request.StudentGroup,
            request.Email,
            request.NewPassword);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id, [FromQuery] int actorId)
    {
        _service.DeleteUser(id, actorId);
        return NoContent();
    }
}
