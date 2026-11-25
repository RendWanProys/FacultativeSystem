using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Xunit;

namespace FacultativeSystem.Tests;

public class ApiClientIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Process _apiProcess;
    private readonly FacultativeApiClient _client;

    public ApiClientIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fac_api_test_{Guid.NewGuid():N}.db");
        var port = GetFreeTcpPort();
        var url = $"http://127.0.0.1:{port}";

        var apiExe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FacultativeSystem.Api", "bin", "Debug", "net9.0", "FacultativeSystem.Api.exe"));

        if (!File.Exists(apiExe))
            throw new FileNotFoundException("FacultativeSystem.Api.exe не найден для интеграционного теста.", apiExe);

        var psi = new ProcessStartInfo
        {
            FileName = apiExe,
            WorkingDirectory = Path.GetDirectoryName(apiExe)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["ASPNETCORE_URLS"] = url;
        psi.Environment["DatabasePath"] = _dbPath;

        _apiProcess = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить API.");
        _client = new FacultativeApiClient(url);

        WaitUntilReady(_client).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (!_apiProcess.HasExited)
        {
            try { _apiProcess.Kill(entireProcessTree: true); } catch { }
        }
        _apiProcess.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntilReady(IFacultativeClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await client.CheckHealthAsync())
                return;
            await Task.Delay(150);
        }
        throw new TimeoutException("Тестовый сервер Web API не ответил вовремя.");
    }

    [Fact]
    public async Task ApiEndToEnd_GroupsLessonsGradesAndVisibility_WorkThroughHttp()
    {
        Assert.True(await _client.CheckHealthAsync());

        var admin = await _client.AuthenticateAsync("admin", "admin123");
        Assert.NotNull(admin);
        var stats = await _client.GetSystemStatsAsync();
        Assert.True(stats.TotalCourses >= 4);

        var teachers = await _client.GetActiveTeachersAsync();
        var teacher = teachers.First();
        var createdCourse = await _client.CreateCourseAsync("API + WPF курс", "Интеграционный курс", teacher.Id, 24);
        var group = await _client.CreateGroupAsync("24-ИСП-9/1");
        await _client.BindGroupToCourseAsync(createdCourse.Id, group.Id);

        var field = await _client.AddCourseFieldAsync(createdCourse.Id, "Пара 1. REST JSON", null, "Практика", "25.11.2026 17:00", group.Id, 1);
        Assert.Single(await _client.GetCourseFieldsAsync(createdCourse.Id));

        // Студент создается с привязкой к группе -> автоматически видит курс этой группы
        var student = await _client.CreateUserAsync("api_student", "pass123", "Соколов Артем", UserRole.Student, "24-ИСП-9/1", null);
        Assert.Single(await _client.GetCoursesForStudentAsync(student.Id));
        var enrollments = await _client.GetStudentEnrollmentsAsync(student.Id);
        var enrollment = enrollments.Single();

        await _client.SetLessonGradeAsync(field.Id, student.Id, "н/я", null);
        var studentLessons = await _client.GetStudentLessonGradesAsync(createdCourse.Id, student.Id);
        Assert.Single(studentLessons);
        Assert.Equal("н/я", studentLessons[0].Grade);

        var courseMatrix = await _client.GetLessonGradesForCourseAsync(createdCourse.Id);
        Assert.Single(courseMatrix);
        Assert.Equal("н/я", courseMatrix[0].Grade);

        await _client.SetEnrollmentGradeAsync(enrollment.Id, "Зачёт", "Хорошая работа");
        var studentEnrollments = await _client.GetStudentEnrollmentsAsync(student.Id);
        Assert.Single(studentEnrollments);
        Assert.Equal("Зачёт", studentEnrollments[0].Grade);

        var backupBytes = await _client.DownloadBackupAsync();
        Assert.True(backupBytes.Length > 4096);

        await _client.DeleteCourseFieldAsync(field.Id);
        await _client.DeleteGroupAsync(group.Id);
        await _client.DeleteCourseAsync(createdCourse.Id);
        Assert.DoesNotContain(await _client.GetCoursesAsync(), c => c.Id == createdCourse.Id);
    }
}
