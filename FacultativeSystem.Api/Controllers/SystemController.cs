using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacultativeSystem.Api.Controllers;

[ApiController]
[Route("api")]
public class SystemController : ControllerBase
{
    private readonly FacultativeService _service;

    public SystemController(FacultativeService service)
    {
        _service = service;
    }

    [HttpGet("health")]
    public IActionResult Health()
        => Ok(new { status = "ok", database = Path.GetFileName(_service.Database.DatabasePath) });

    [HttpGet("system/stats")]
    public ActionResult<SystemStats> GetStats()
        => Ok(_service.GetSystemStats());

    [HttpGet("system/backup/download")]
    public IActionResult DownloadBackup()
    {
        var tempBackup = Path.Combine(Path.GetTempPath(), $"facultative_backup_{Guid.NewGuid():N}.db");
        try
        {
            _service.Database.CreateBackup(tempBackup);
            var bytes = System.IO.File.ReadAllBytes(tempBackup);
            return File(bytes, "application/octet-stream", $"facultative_backup_{DateTime.Now:yyyyMMdd_HHmm}.db");
        }
        finally
        {
            if (System.IO.File.Exists(tempBackup))
                System.IO.File.Delete(tempBackup);
        }
    }
}
