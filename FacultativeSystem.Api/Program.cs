using System.Text.Json;
using FacultativeSystem.Core.Data;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var configuredDbPath = builder.Configuration["DatabasePath"];
var dbPath = string.IsNullOrWhiteSpace(configuredDbPath)
    ? Path.Combine(AppContext.BaseDirectory, "facultative.db")
    : configuredDbPath;

builder.Services.AddSingleton(_ =>
{
    var db = new DatabaseService(dbPath);
    db.Initialize();
    return db;
});
builder.Services.AddSingleton<FacultativeService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ИС «Факультатив» — Web API (SQLite)",
        Version = "v1",
        Description = "REST API сервер для WPF-клиента «Факультатив» с хранением данных в SQLite."
    });
});

if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5150");
}

var app = builder.Build();

// Инициализируем SQLite БД на старте сервера
_ = app.Services.GetRequiredService<DatabaseService>();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.Response.ContentType = "application/json; charset=utf-8";

        if (ex is ArgumentException or InvalidOperationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiErrorResponse(ex.Message)));
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ApiErrorResponse("Внутренняя ошибка сервера: " + ex?.Message)));
        }
    });
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ИС «Факультатив» API v1");
    c.RoutePrefix = "swagger";
});

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

app.Run();

public partial class Program { }
