using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacultativeSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly FacultativeService _service;

    public AuthController(FacultativeService service)
    {
        _service = service;
    }

    [HttpPost("login")]
    public ActionResult<User> Login([FromBody] LoginRequest request)
    {
        var user = _service.Authenticate(request.Login, request.Password);
        if (user is null)
            return Unauthorized(new ApiErrorResponse("Неверный логин или пароль."));

        return Ok(user);
    }
}
