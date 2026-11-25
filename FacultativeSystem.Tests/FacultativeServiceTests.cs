using FacultativeSystem.Core.Data;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FacultativeSystem.Tests;

public class FacultativeServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;
    private readonly FacultativeService _service;

    public FacultativeServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fac_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseService(_dbPath);
        _db.Initialize();
        _service = new FacultativeService(_db);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void SeedAndAuthenticate_AllThreeRolesCanSignIn()
    {
        var admin = _service.Authenticate("admin", "admin123");
        var teacher = _service.Authenticate("paramonov", "123456");
        var student = _service.Authenticate("gabitov", "123456");
        var invalid = _service.Authenticate("gabitov", "wrong");

        Assert.NotNull(admin);
        Assert.Equal(UserRole.Admin, admin!.Role);
        Assert.NotNull(teacher);
        Assert.Equal(UserRole.Teacher, teacher!.Role);
        Assert.NotNull(student);
        Assert.Equal(UserRole.Student, student!.Role);
        Assert.Null(invalid);
    }

    [Fact]
    public void Admin_CreateAndDeleteCourse_EnforcesSingleTeacherRule()
    {
        var teacher = _service.Authenticate("dubinin", "123456")!;
        var student = _service.Authenticate("gabitov", "123456")!;

        Assert.Throws<InvalidOperationException>(() =>
            _service.CreateCourse("Тестовый курс", "Описание", student.Id));

        var created = _service.CreateCourse("Паттерны проектирования C#", "SOLID и GoF паттерны", teacher.Id, 44);
        Assert.Equal(teacher.Id, created.TeacherId);
        Assert.Equal(teacher.FullName, created.TeacherName);

        _service.DeleteCourse(created.Id);
        Assert.DoesNotContain(_service.GetCourses(), c => c.Id == created.Id);
    }

    [Fact]
    public void Admin_GroupsAndCourseBinding_AutoEnrollStudentsWithUnlimitedCapacity()
    {
        var teacher = _service.Authenticate("dubinin", "123456")!;
        var course = _service.CreateCourse("Искусственный интеллект", "Факультатив ИО", teacher.Id, 30);

        // Админ создает группу и привязывает ее к факультативу
        var group = _service.CreateGroup("24-ИСП-9/1");
        _service.BindGroupToCourse(course.Id, group.Id);

        // При создании студента с этой группой он автоматически попадает в группу и на ее курсы
        var raul = _service.CreateUser("raul", "pass123", "Рауль Студент", UserRole.Student, "24-ИСП-9/1", null);
        Assert.Contains(_service.GetGroupStudents(group.Id), s => s.Id == raul.Id);
        var raulCourses = _service.GetCoursesForStudent(raul.Id);
        Assert.Single(raulCourses);
        Assert.Equal(course.Id, raulCourses[0].Id);

        // При смене группы студента его членство и курсы обновляются автоматически
        var otherGroup = _service.CreateGroup("24-ИСП-9/2");
        _service.UpdateUserAccess(raul.Id, UserRole.Student, true, studentGroup: "24-ИСП-9/2");
        Assert.DoesNotContain(_service.GetGroupStudents(group.Id), s => s.Id == raul.Id);
        Assert.Contains(_service.GetGroupStudents(otherGroup.Id), s => s.Id == raul.Id);
        Assert.Empty(_service.GetCoursesForStudent(raul.Id));

        _service.DeleteGroup(otherGroup.Id);
        _service.DeleteGroup(group.Id);
        _service.DeleteCourse(course.Id);
    }

    [Fact]
    public void Teacher_LessonsAndGrades_WorkWithOptionalCommentAndAbsentOrCredit()
    {
        var teacher = _service.Authenticate("dubinin", "123456")!;
        var course = _service.GetCourses(teacher.Id).First();

        var group = _service.CreateGroup("24-ИСП-5/1");
        _service.BindGroupToCourse(course.Id, group.Id);

        var lesson = _service.AddCourseField(course.Id, "Пара 3. Индексы SQLite", "", "Практика", "27.11.2026 16:00", group.Id, 10);
        Assert.Equal(group.Id, lesson.GroupId);

        _service.UpdateCourseField(lesson.Id, "Пара 3. Индексы и планы", "Заметка к паре", "Лабораторная", "27.11.2026 16:30", group.Id, 11);
        var updated = _service.GetCourseFields(course.Id).First(f => f.Id == lesson.Id);
        Assert.Equal("Пара 3. Индексы и планы", updated.FieldName);

        var student = _service.CreateUser("student_grp", "pass123", "Тестов Студент", UserRole.Student, "24-ИСП-5/1", null);
        var enrollment = _service.GetStudentEnrollments(student.Id).Single();

        _service.SetLessonGrade(lesson.Id, student.Id, "н/я", null);
        var lessonRows = _service.GetLessonGradesForField(lesson.Id);
        Assert.Single(lessonRows);
        Assert.Equal("н/я", lessonRows[0].Grade);

        var courseLessons = _service.GetLessonGradesForCourse(course.Id);
        Assert.Contains(courseLessons, x => x.FieldId == lesson.Id && x.StudentId == student.Id && x.Grade == "н/я");

        _service.SetLessonGrade(lesson.Id, student.Id, "-", null);
        Assert.Equal("—", _service.GetLessonGradesForField(lesson.Id)[0].Grade);

        _service.SetLessonGrade(lesson.Id, student.Id, GradeOptions.NotSet, "");
        Assert.Null(_service.GetLessonGradesForField(lesson.Id)[0].Grade);

        _service.SetEnrollmentGrade(enrollment.Id, "Зачёт", null);
        var graded = _service.GetStudentEnrollments(student.Id).First(e => e.Id == enrollment.Id);
        Assert.Equal("Зачёт", graded.Grade);
        Assert.Null(graded.Comment);

        _service.DeleteCourseField(lesson.Id);
        _service.DeleteGroup(group.Id);
    }

    [Fact]
    public void LegacyV0DatabaseWithoutGroupId_MigratesCleanlyToV3()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), $"fac_legacy_{Guid.NewGuid():N}.db");
        try
        {
            // Имитируем старую БД v1 (user_version = 0, без GroupId в Enrollments)
            using (var conn = new SqliteConnection($"Data Source={legacyPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE Users (Id INTEGER PRIMARY KEY, Login TEXT, PasswordHash TEXT, FullName TEXT, Role TEXT, StudentGroup TEXT, Email TEXT, IsActive INTEGER, CreatedAt TEXT);
                    CREATE TABLE Enrollments (Id INTEGER PRIMARY KEY, CourseId INTEGER, StudentId INTEGER);
                    PRAGMA user_version = 0;
                    """;
                cmd.ExecuteNonQuery();
            }

            var upgradedDb = new DatabaseService(legacyPath);
            upgradedDb.Initialize();
            var svc = new FacultativeService(upgradedDb);

            // Запросы с e.GroupId не падают
            var enrollments = svc.GetCourseEnrollments(1);
            Assert.NotEmpty(enrollments);
        }
        finally
        {
            if (File.Exists(legacyPath))
                try { File.Delete(legacyPath); } catch { }
        }
    }

    [Fact]
    public void GroupsOnSameCourse_HaveSeparateFieldsAndStudentSchedules()
    {
        var teacher = _service.Authenticate("ivanova", "123456")!;
        var course = _service.CreateCourse("Английский язык", "Раздельные группы", teacher.Id, 32);

        var g1 = _service.CreateGroup("24-АНГ-1");
        var g2 = _service.CreateGroup("24-АНГ-2");
        _service.BindGroupToCourse(course.Id, g1.Id);
        _service.BindGroupToCourse(course.Id, g2.Id);

        var st1 = _service.CreateUser("eng_s1", "123456", "Студент Первый", UserRole.Student, "24-АНГ-1", null);
        var st2 = _service.CreateUser("eng_s2", "123456", "Студент Второй", UserRole.Student, "24-АНГ-2", null);

        var f1 = _service.AddCourseField(course.Id, "Speaking Group 1", "", "Практика", "01.12.2026 10:00", g1.Id, 1);
        var f2 = _service.AddCourseField(course.Id, "Grammar Group 2", "", "Лекция", "02.12.2026 14:00", g2.Id, 1);

        var st1Fields = _service.GetCourseFields(course.Id, st1.Id);
        Assert.Single(st1Fields);
        Assert.Equal(f1.Id, st1Fields[0].Id);

        var st2Fields = _service.GetCourseFields(course.Id, st2.Id);
        Assert.Single(st2Fields);
        Assert.Equal(f2.Id, st2Fields[0].Id);

        // Регистронезависимый поиск по кириллическому ФИО преподавателя
        var foundByTeacherLower = _service.GetCourses(search: "иванова");
        Assert.Contains(foundByTeacherLower, c => c.Id == course.Id);

        _service.DeleteGroup(g1.Id);
        _service.DeleteGroup(g2.Id);
        _service.DeleteCourse(course.Id);
    }

    [Fact]
    public void Database_CreateBackup_CreatesValidCopy()
    {
        var backupPath = Path.Combine(Path.GetTempPath(), $"fac_backup_{Guid.NewGuid():N}.db");
        try
        {
            _db.CreateBackup(backupPath);
            Assert.True(File.Exists(backupPath));

            var backupDb = new DatabaseService(backupPath);
            var backupService = new FacultativeService(backupDb);
            Assert.True(backupService.GetCourses().Count >= 4);
        }
        finally
        {
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
    }
}
