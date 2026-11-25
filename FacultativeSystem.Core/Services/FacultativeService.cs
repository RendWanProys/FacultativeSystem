using FacultativeSystem.Core.Data;
using FacultativeSystem.Core.Models;
using Microsoft.Data.Sqlite;

namespace FacultativeSystem.Core.Services;

/// <summary>
/// Основной сервис бизнес-логики ИС «Факультатив».
/// </summary>
public class FacultativeService
{
    private readonly DatabaseService _db;

    public FacultativeService(DatabaseService db)
    {
        _db = db;
    }

    public DatabaseService Database => _db;

    // ------------------------------------------------------------------
    // 1. АУТЕНТИФИКАЦИЯ
    // ------------------------------------------------------------------

    public User? Authenticate(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            return null;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Login, PasswordHash, FullName, Role, StudentGroup, Email, IsActive, CreatedAt
            FROM Users
            WHERE lower(Login) = lower($login)
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$login", login.Trim());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var user = MapUser(reader);
        if (!user.IsActive)
            throw new InvalidOperationException("Учетная запись заблокирована администратором.");

        return PasswordHasher.Verify(password, user.PasswordHash) ? user : null;
    }

    // ------------------------------------------------------------------
    // 2. АДМИНИСТРАТОР: УПРАВЛЕНИЕ БД И ДОСТУПАМИ
    // ------------------------------------------------------------------

    public SystemStats GetSystemStats()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM Courses),
                (SELECT COUNT(*) FROM Users WHERE Role = 'Teacher' AND IsActive = 1),
                (SELECT COUNT(*) FROM Users WHERE Role = 'Student' AND IsActive = 1),
                (SELECT COUNT(*) FROM Users WHERE Role = 'Admin' AND IsActive = 1),
                (SELECT COUNT(*) FROM Enrollments),
                (SELECT COUNT(*) FROM Enrollments WHERE Grade IS NOT NULL),
                (SELECT COALESCE(AVG(CAST(Grade AS REAL)), 0) FROM Enrollments WHERE Grade IN ('2','3','4','5'));
            """;
        using var r = cmd.ExecuteReader();
        r.Read();
        return new SystemStats
        {
            TotalCourses = r.GetInt32(0),
            TotalTeachers = r.GetInt32(1),
            TotalStudents = r.GetInt32(2),
            TotalAdmins = r.GetInt32(3),
            TotalEnrollments = r.GetInt32(4),
            CompletedEnrollments = r.GetInt32(5),
            AverageGrade = Math.Round(r.GetDouble(6), 2)
        };
    }

    public List<User> GetAllUsers(UserRole? roleFilter = null)
    {
        var result = new List<User>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = roleFilter.HasValue
            ? "SELECT Id, Login, PasswordHash, FullName, Role, StudentGroup, Email, IsActive, CreatedAt FROM Users WHERE Role = $role ORDER BY Role, FullName;"
            : "SELECT Id, Login, PasswordHash, FullName, Role, StudentGroup, Email, IsActive, CreatedAt FROM Users ORDER BY Role, FullName;";

        if (roleFilter.HasValue)
            cmd.Parameters.AddWithValue("$role", roleFilter.Value.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapUser(reader));
        return result;
    }

    public List<User> GetActiveTeachers() => GetAllUsers(UserRole.Teacher).Where(u => u.IsActive).ToList();
    public List<User> GetActiveStudents() => GetAllUsers(UserRole.Student).Where(u => u.IsActive).ToList();

    public User CreateUser(string login, string password, string fullName, UserRole role, string? studentGroup, string? email, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(login))
            throw new ArgumentException("Логин не может быть пустым.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            throw new ArgumentException("Пароль должен содержать не менее 4 символов.");
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("ФИО пользователя обязательно для заполнения.");

        using var conn = _db.CreateConnection();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Users WHERE lower(Login) = lower($login);";
        check.Parameters.AddWithValue("$login", login.Trim());
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            throw new InvalidOperationException($"Пользователь с логином «{login.Trim()}» уже существует.");

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        int id;
        using (var tx = conn.BeginTransaction())
        {
            int? targetGroupId = null;
            string? cleanGroupName = string.IsNullOrWhiteSpace(studentGroup) ? null : studentGroup.Trim();
            if (role == UserRole.Student && cleanGroupName is not null)
            {
                targetGroupId = ResolveExistingGroupId(conn, tx, cleanGroupName);
                cleanGroupName = GetGroupNameById(conn, tx, targetGroupId.Value);
            }
            else if (role != UserRole.Student)
            {
                cleanGroupName = null;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Users (Login, PasswordHash, FullName, Role, StudentGroup, Email, IsActive, CreatedAt)
                VALUES ($login, $hash, $name, $role, $grp, $email, $active, $dt);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$login", login.Trim());
            cmd.Parameters.AddWithValue("$hash", PasswordHasher.Hash(password));
            cmd.Parameters.AddWithValue("$name", fullName.Trim());
            cmd.Parameters.AddWithValue("$role", role.ToString());
            cmd.Parameters.AddWithValue("$grp", (object?)cleanGroupName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim());
            cmd.Parameters.AddWithValue("$active", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$dt", now);

            id = Convert.ToInt32(cmd.ExecuteScalar());

            if (role == UserRole.Student)
                SyncStudentGroupMembership(conn, tx, id, targetGroupId, isActive);

            tx.Commit();
        }
        return GetUserById(id)!;
    }

    public void UpdateUserAccess(int userId, UserRole newRole, bool isActive, string? fullName = null, string? studentGroup = null, string? email = null, string? newPassword = null)
    {
        using var conn = _db.CreateConnection();
        var existing = GetUserById(userId) ?? throw new InvalidOperationException("Пользователь не найден.");

        if (existing.Role == UserRole.Teacher && (newRole != UserRole.Teacher || !isActive))
        {
            using var cCheck = conn.CreateCommand();
            cCheck.CommandText = "SELECT COUNT(*) FROM Courses WHERE TeacherId = $tid;";
            cCheck.Parameters.AddWithValue("$tid", userId);
            if (Convert.ToInt32(cCheck.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Невозможно изменить роль или закрыть доступ преподавателю, пока за ним закреплены факультативные курсы.");
        }

        if (existing.Role == UserRole.Admin && (newRole != UserRole.Admin || !isActive))
        {
            using var aCheck = conn.CreateCommand();
            aCheck.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = 'Admin' AND IsActive = 1 AND Id <> $id;";
            aCheck.Parameters.AddWithValue("$id", userId);
            if (Convert.ToInt32(aCheck.ExecuteScalar()) == 0)
                throw new InvalidOperationException("В системе должен оставаться хотя бы один активный администратор.");
        }

        using var tx = conn.BeginTransaction();
        int? targetGroupId = null;
        string? cleanGroupName = newRole == UserRole.Student && !string.IsNullOrWhiteSpace(studentGroup)
            ? studentGroup.Trim()
            : null;

        if (newRole == UserRole.Student && cleanGroupName is not null)
        {
            targetGroupId = ResolveExistingGroupId(conn, tx, cleanGroupName);
            cleanGroupName = GetGroupNameById(conn, tx, targetGroupId.Value);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            var setPasswordClause = !string.IsNullOrWhiteSpace(newPassword) ? ", PasswordHash = $hash" : "";
            cmd.CommandText = $"""
                UPDATE Users
                SET Role = $role,
                    IsActive = $active,
                    FullName = $name,
                    StudentGroup = $grp,
                    Email = $email
                    {setPasswordClause}
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$role", newRole.ToString());
            cmd.Parameters.AddWithValue("$active", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(fullName) ? existing.FullName : fullName.Trim());
            cmd.Parameters.AddWithValue("$grp", (object?)cleanGroupName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim());
            cmd.Parameters.AddWithValue("$id", userId);
            if (!string.IsNullOrWhiteSpace(newPassword))
                cmd.Parameters.AddWithValue("$hash", PasswordHasher.Hash(newPassword));

            cmd.ExecuteNonQuery();
        }

        SyncStudentGroupMembership(conn, tx, userId, newRole == UserRole.Student ? targetGroupId : null, isActive);
        tx.Commit();
    }

    private static int ResolveExistingGroupId(SqliteConnection conn, SqliteTransaction tx, string groupName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id FROM StudyGroups WHERE lower(Name) = lower($nm) LIMIT 1;";
        cmd.Parameters.AddWithValue("$nm", groupName);
        var obj = cmd.ExecuteScalar();
        if (obj is null || obj == DBNull.Value)
            throw new InvalidOperationException($"Учебная группа «{groupName}» не найдена. Выберите существующую группу из списка или создайте её на вкладке «Группы».");
        return Convert.ToInt32(obj);
    }

    private static string GetGroupNameById(SqliteConnection conn, SqliteTransaction tx, int groupId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Name FROM StudyGroups WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", groupId);
        return (string)cmd.ExecuteScalar()!;
    }

    private static void SyncStudentGroupMembership(SqliteConnection conn, SqliteTransaction tx, int studentId, int? targetGroupId, bool isActive)
    {
        using (var delOld = conn.CreateCommand())
        {
            delOld.Transaction = tx;
            delOld.CommandText = targetGroupId.HasValue
                ? "DELETE FROM GroupStudents WHERE StudentId = $sid AND GroupId <> $gid;"
                : "DELETE FROM GroupStudents WHERE StudentId = $sid;";
            delOld.Parameters.AddWithValue("$sid", studentId);
            if (targetGroupId.HasValue)
                delOld.Parameters.AddWithValue("$gid", targetGroupId.Value);
            delOld.ExecuteNonQuery();
        }

        using (var delUngraded = conn.CreateCommand())
        {
            delUngraded.Transaction = tx;
            delUngraded.CommandText = targetGroupId.HasValue
                ? "DELETE FROM Enrollments WHERE StudentId = $sid AND Grade IS NULL AND (GroupId IS NULL OR GroupId <> $gid);"
                : "DELETE FROM Enrollments WHERE StudentId = $sid AND Grade IS NULL;";
            delUngraded.Parameters.AddWithValue("$sid", studentId);
            if (targetGroupId.HasValue)
                delUngraded.Parameters.AddWithValue("$gid", targetGroupId.Value);
            delUngraded.ExecuteNonQuery();
        }

        if (targetGroupId.HasValue)
        {
            using (var insMem = conn.CreateCommand())
            {
                insMem.Transaction = tx;
                insMem.CommandText = "INSERT OR IGNORE INTO GroupStudents (GroupId, StudentId) VALUES ($gid, $sid);";
                insMem.Parameters.AddWithValue("$gid", targetGroupId.Value);
                insMem.Parameters.AddWithValue("$sid", studentId);
                insMem.ExecuteNonQuery();
            }

            if (isActive)
            {
                using var insEnr = conn.CreateCommand();
                insEnr.Transaction = tx;
                insEnr.CommandText = """
                    INSERT OR IGNORE INTO Enrollments (CourseId, StudentId, GroupId, EnrolledAt, Status)
                    SELECT cgl.CourseId, $sid, $gid, $dt, 'Active'
                    FROM CourseGroupLinks cgl
                    WHERE cgl.GroupId = $gid;
                    """;
                insEnr.Parameters.AddWithValue("$sid", studentId);
                insEnr.Parameters.AddWithValue("$gid", targetGroupId.Value);
                insEnr.Parameters.AddWithValue("$dt", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                insEnr.ExecuteNonQuery();
            }
        }
    }

    public void DeleteUser(int userId, int currentActorId)
    {
        if (userId == currentActorId)
            throw new InvalidOperationException("Нельзя удалить собственную активную учетную запись.");

        using var conn = _db.CreateConnection();
        var user = GetUserById(userId) ?? throw new InvalidOperationException("Пользователь не найден.");

        if (user.Role == UserRole.Teacher)
        {
            using var cCheck = conn.CreateCommand();
            cCheck.CommandText = "SELECT COUNT(*) FROM Courses WHERE TeacherId = $tid;";
            cCheck.Parameters.AddWithValue("$tid", userId);
            if (Convert.ToInt32(cCheck.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Нельзя удалить преподавателя, за которым закреплены курсы.");
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Users WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.ExecuteNonQuery();
    }

    public User? GetUserById(int id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Login, PasswordHash, FullName, Role, StudentGroup, Email, IsActive, CreatedAt FROM Users WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapUser(r) : null;
    }

    // ------------------------------------------------------------------
    // 3. ФАКУЛЬТАТИВЫ (ПРЕДМЕТЫ) — БЕЗ ОГРАНИЧЕНИЙ НА КОЛ-ВО СТУДЕНТОВ
    // ------------------------------------------------------------------

    public List<Course> GetCourses(int? teacherId = null, string? search = null) =>
        QueryCourses(teacherId: teacherId, studentId: null, search: search);

    public List<Course> GetCoursesForStudent(int studentId, string? search = null) =>
        QueryCourses(teacherId: null, studentId: studentId, search: search);

    private List<Course> QueryCourses(int? teacherId, int? studentId, string? search)
    {
        var result = new List<Course>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        var conditions = new List<string>();
        if (teacherId.HasValue)
        {
            conditions.Add("c.TeacherId = $tid");
            cmd.Parameters.AddWithValue("$tid", teacherId.Value);
        }
        if (studentId.HasValue)
        {
            conditions.Add("EXISTS (SELECT 1 FROM Enrollments se WHERE se.CourseId = c.Id AND se.StudentId = $sid)");
            cmd.Parameters.AddWithValue("$sid", studentId.Value);
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        cmd.CommandText = $"""
            SELECT c.Id, c.Title, c.Description, c.TeacherId, u.FullName AS TeacherName,
                   c.TotalHours,
                   (SELECT COUNT(*) FROM Enrollments e WHERE e.CourseId = c.Id) AS EnrolledCount,
                   (SELECT COUNT(*) FROM CourseFields f WHERE f.CourseId = c.Id) AS FieldsCount,
                   (SELECT COUNT(*) FROM CourseGroupLinks gl WHERE gl.CourseId = c.Id) AS GroupsCount,
                   c.CreatedAt
            FROM Courses c
            JOIN Users u ON u.Id = c.TeacherId
            {where}
            ORDER BY c.Id DESC;
            """;

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new Course
            {
                Id = r.GetInt32(0),
                Title = r.GetString(1),
                Description = r.GetString(2),
                TeacherId = r.GetInt32(3),
                TeacherName = r.GetString(4),
                TotalHours = r.GetInt32(5),
                EnrolledCount = r.GetInt32(6),
                FieldsCount = r.GetInt32(7),
                GroupsCount = r.GetInt32(8),
                CreatedAt = r.GetString(9)
            });
        }

        // Фильтруем в C# с OrdinalIgnoreCase, т.к. SQLite lower()/LIKE не меняет регистр кириллицы
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            result = result.Where(c =>
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.TeacherName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return result;
    }

    public Course CreateCourse(string title, string description, int teacherId, int totalHours = 36)
    {
        ValidateCourseInput(title, description, teacherId, totalHours);
        using var conn = _db.CreateConnection();
        EnsureTeacherExists(conn, teacherId);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Courses (Title, Description, TeacherId, TotalHours, CreatedAt)
            VALUES ($title, $desc, $tid, $hours, $dt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$title", title.Trim());
        cmd.Parameters.AddWithValue("$desc", description.Trim());
        cmd.Parameters.AddWithValue("$tid", teacherId);
        cmd.Parameters.AddWithValue("$hours", totalHours);
        cmd.Parameters.AddWithValue("$dt", now);

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetCourses().First(c => c.Id == id);
    }

    public void UpdateCourse(int courseId, string title, string description, int teacherId, int totalHours)
    {
        ValidateCourseInput(title, description, teacherId, totalHours);
        using var conn = _db.CreateConnection();
        EnsureTeacherExists(conn, teacherId);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Courses
            SET Title = $title, Description = $desc, TeacherId = $tid, TotalHours = $hours
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$title", title.Trim());
        cmd.Parameters.AddWithValue("$desc", description.Trim());
        cmd.Parameters.AddWithValue("$tid", teacherId);
        cmd.Parameters.AddWithValue("$hours", totalHours);
        cmd.Parameters.AddWithValue("$id", courseId);

        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Факультатив не найден.");
    }

    public void DeleteCourse(int courseId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Courses WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", courseId);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Факультатив не найден.");
    }

    // ------------------------------------------------------------------
    // 4. ГРУППЫ (УПРАВЛЯЮТСЯ АДМИНИСТРАТОРОМ, ПРИВЯЗЫВАЮТСЯ К ФАКУЛЬТАТИВАМ)
    // ------------------------------------------------------------------

    public List<CourseGroup> GetAllGroups()
    {
        var result = new List<CourseGroup>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT g.Id, g.Name,
                   (SELECT COUNT(*) FROM GroupStudents gs WHERE gs.GroupId = g.Id) AS StudentsCount,
                   (SELECT COUNT(*) FROM CourseGroupLinks cgl WHERE cgl.GroupId = g.Id) AS CoursesCount
            FROM StudyGroups g
            ORDER BY g.Name ASC;
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new CourseGroup
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                StudentsCount = r.GetInt32(2),
                CoursesCount = r.GetInt32(3)
            });
        }
        return result;
    }

    public List<CourseGroup> GetCourseGroups(int courseId)
    {
        var result = new List<CourseGroup>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT g.Id, g.Name,
                   (SELECT COUNT(*) FROM Enrollments e WHERE e.CourseId = $cid AND e.GroupId = g.Id) AS StudentsCount,
                   (SELECT COUNT(*) FROM CourseGroupLinks cgl WHERE cgl.GroupId = g.Id) AS CoursesCount
            FROM StudyGroups g
            JOIN CourseGroupLinks gl ON gl.GroupId = g.Id
            WHERE gl.CourseId = $cid
            ORDER BY g.Name ASC;
            """;
        cmd.Parameters.AddWithValue("$cid", courseId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new CourseGroup
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                StudentsCount = r.GetInt32(2),
                CoursesCount = r.GetInt32(3)
            });
        }
        return result;
    }

    public CourseGroup CreateGroup(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Название группы не может быть пустым.");

        using var conn = _db.CreateConnection();
        using var dup = conn.CreateCommand();
        dup.CommandText = "SELECT COUNT(*) FROM StudyGroups WHERE lower(Name) = lower($name);";
        dup.Parameters.AddWithValue("$name", name.Trim());
        if (Convert.ToInt32(dup.ExecuteScalar()) > 0)
            throw new InvalidOperationException($"Группа «{name.Trim()}» уже существует.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO StudyGroups (Name) VALUES ($name); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$name", name.Trim());
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetAllGroups().First(g => g.Id == id);
    }

    public void RenameGroup(int groupId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Название группы не может быть пустым.");

        using var conn = _db.CreateConnection();
        using var dup = conn.CreateCommand();
        dup.CommandText = "SELECT COUNT(*) FROM StudyGroups WHERE lower(Name) = lower($name) AND Id <> $id;";
        dup.Parameters.AddWithValue("$name", newName.Trim());
        dup.Parameters.AddWithValue("$id", groupId);
        if (Convert.ToInt32(dup.ExecuteScalar()) > 0)
            throw new InvalidOperationException($"Группа «{newName.Trim()}» уже существует.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE StudyGroups SET Name = $name WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$name", newName.Trim());
        cmd.Parameters.AddWithValue("$id", groupId);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Группа не найдена.");
    }

    public void DeleteGroup(int groupId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StudyGroups WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", groupId);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Группа не найдена.");
    }

    public List<User> GetGroupStudents(int groupId)
    {
        var result = new List<User>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.Id, u.Login, u.PasswordHash, u.FullName, u.Role, u.StudentGroup, u.Email, u.IsActive, u.CreatedAt
            FROM Users u
            JOIN GroupStudents gs ON gs.StudentId = u.Id
            WHERE gs.GroupId = $gid
            ORDER BY u.FullName ASC;
            """;
        cmd.Parameters.AddWithValue("$gid", groupId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(MapUser(r));
        return result;
    }

    public List<Course> GetGroupCourses(int groupId)
    {
        var result = new List<Course>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.Id, c.Title, c.Description, c.TeacherId, u.FullName AS TeacherName,
                   c.TotalHours,
                   (SELECT COUNT(*) FROM Enrollments e WHERE e.CourseId = c.Id) AS EnrolledCount,
                   (SELECT COUNT(*) FROM CourseFields f WHERE f.CourseId = c.Id) AS FieldsCount,
                   (SELECT COUNT(*) FROM CourseGroupLinks gl2 WHERE gl2.CourseId = c.Id) AS GroupsCount,
                   c.CreatedAt
            FROM Courses c
            JOIN Users u ON u.Id = c.TeacherId
            JOIN CourseGroupLinks gl ON gl.CourseId = c.Id
            WHERE gl.GroupId = $gid
            ORDER BY c.Title ASC;
            """;
        cmd.Parameters.AddWithValue("$gid", groupId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new Course
            {
                Id = r.GetInt32(0),
                Title = r.GetString(1),
                Description = r.GetString(2),
                TeacherId = r.GetInt32(3),
                TeacherName = r.GetString(4),
                TotalHours = r.GetInt32(5),
                EnrolledCount = r.GetInt32(6),
                FieldsCount = r.GetInt32(7),
                GroupsCount = r.GetInt32(8),
                CreatedAt = r.GetString(9)
            });
        }
        return result;
    }

    /// <summary>
    /// Добавляет студента в учебную группу и автоматически зачисляет его на все факультативы, к которым привязана группа.
    /// </summary>
    public void AddStudentToGroup(int groupId, int studentId)
    {
        using var conn = _db.CreateConnection();
        var student = GetUserById(studentId) ?? throw new InvalidOperationException("Студент не найден.");
        if (student.Role != UserRole.Student)
            throw new InvalidOperationException("В учебную группу можно добавить только пользователя с ролью «Студент».");

        using var gCheck = conn.CreateCommand();
        gCheck.CommandText = "SELECT Name FROM StudyGroups WHERE Id = $gid;";
        gCheck.Parameters.AddWithValue("$gid", groupId);
        var groupName = gCheck.ExecuteScalar() as string ?? throw new InvalidOperationException("Группа не найдена.");

        using var tx = conn.BeginTransaction();
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO GroupStudents (GroupId, StudentId) VALUES ($g, $s);";
            ins.Parameters.AddWithValue("$g", groupId);
            ins.Parameters.AddWithValue("$s", studentId);
            ins.ExecuteNonQuery();
        }

        if (string.IsNullOrWhiteSpace(student.StudentGroup))
        {
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE Users SET StudentGroup = $gn WHERE Id = $s;";
            upd.Parameters.AddWithValue("$gn", groupName);
            upd.Parameters.AddWithValue("$s", studentId);
            upd.ExecuteNonQuery();
        }

        // Автоматически зачисляем студента на все курсы, привязанные к группе
        using (var enr = conn.CreateCommand())
        {
            enr.Transaction = tx;
            enr.CommandText = """
                INSERT OR IGNORE INTO Enrollments (CourseId, StudentId, GroupId, EnrolledAt, Status)
                SELECT cgl.CourseId, $sid, $gid, $dt, 'Active'
                FROM CourseGroupLinks cgl
                WHERE cgl.GroupId = $gid;
                """;
            enr.Parameters.AddWithValue("$sid", studentId);
            enr.Parameters.AddWithValue("$gid", groupId);
            enr.Parameters.AddWithValue("$dt", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            enr.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void RemoveStudentFromGroup(int groupId, int studentId)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM GroupStudents WHERE GroupId = $g AND StudentId = $s;";
            del.Parameters.AddWithValue("$g", groupId);
            del.Parameters.AddWithValue("$s", studentId);
            del.ExecuteNonQuery();
        }

        using (var delEnr = conn.CreateCommand())
        {
            delEnr.Transaction = tx;
            delEnr.CommandText = "DELETE FROM Enrollments WHERE GroupId = $g AND StudentId = $s AND Grade IS NULL;";
            delEnr.Parameters.AddWithValue("$g", groupId);
            delEnr.Parameters.AddWithValue("$s", studentId);
            delEnr.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>
    /// Привязывает группу к факультативу и автоматически зачисляет студентов этой группы на факультатив.
    /// </summary>
    public void BindGroupToCourse(int courseId, int groupId)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO CourseGroupLinks (CourseId, GroupId) VALUES ($c, $g);";
            ins.Parameters.AddWithValue("$c", courseId);
            ins.Parameters.AddWithValue("$g", groupId);
            ins.ExecuteNonQuery();
        }

        using (var enr = conn.CreateCommand())
        {
            enr.Transaction = tx;
            enr.CommandText = """
                INSERT OR IGNORE INTO Enrollments (CourseId, StudentId, GroupId, EnrolledAt, Status)
                SELECT $cid, gs.StudentId, $gid, $dt, 'Active'
                FROM GroupStudents gs
                JOIN Users u ON u.Id = gs.StudentId AND u.IsActive = 1
                WHERE gs.GroupId = $gid;
                """;
            enr.Parameters.AddWithValue("$cid", courseId);
            enr.Parameters.AddWithValue("$gid", groupId);
            enr.Parameters.AddWithValue("$dt", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            enr.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void UnbindGroupFromCourse(int courseId, int groupId)
    {
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM CourseGroupLinks WHERE CourseId = $c AND GroupId = $g;";
            del.Parameters.AddWithValue("$c", courseId);
            del.Parameters.AddWithValue("$g", groupId);
            del.ExecuteNonQuery();
        }

        using (var updFields = conn.CreateCommand())
        {
            updFields.Transaction = tx;
            updFields.CommandText = "UPDATE CourseFields SET GroupId = NULL WHERE CourseId = $c AND GroupId = $g;";
            updFields.Parameters.AddWithValue("$c", courseId);
            updFields.Parameters.AddWithValue("$g", groupId);
            updFields.ExecuteNonQuery();
        }

        using (var delEnr = conn.CreateCommand())
        {
            delEnr.Transaction = tx;
            delEnr.CommandText = "DELETE FROM Enrollments WHERE CourseId = $c AND GroupId = $g AND Grade IS NULL;";
            delEnr.Parameters.AddWithValue("$c", courseId);
            delEnr.Parameters.AddWithValue("$g", groupId);
            delEnr.ExecuteNonQuery();
        }

        tx.Commit();
    }

    // ------------------------------------------------------------------
    // 5. ПОЛЯ ФАКУЛЬТЕТА (ПАРЫ / ЗАНЯТИЯ С ПРИВЯЗКОЙ К ГРУППАМ)
    // ------------------------------------------------------------------

    public List<CourseField> GetCourseFields(int courseId, int? filterForStudentId = null)
    {
        var result = new List<CourseField>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        var studentFilter = "";
        if (filterForStudentId.HasValue)
        {
            studentFilter = """
                AND (
                    f.GroupId IS NULL
                    OR f.GroupId = (SELECT e.GroupId FROM Enrollments e WHERE e.CourseId = f.CourseId AND e.StudentId = $sid LIMIT 1)
                )
                """;
            cmd.Parameters.AddWithValue("$sid", filterForStudentId.Value);
        }

        cmd.CommandText = $"""
            SELECT f.Id, f.CourseId, f.GroupId, COALESCE(g.Name, 'Все группы') AS GroupName,
                   f.FieldName, f.FieldValue, f.FieldType, f.LessonDate, f.SortOrder
            FROM CourseFields f
            LEFT JOIN StudyGroups g ON g.Id = f.GroupId
            WHERE f.CourseId = $cid {studentFilter}
            ORDER BY f.SortOrder ASC, f.Id ASC;
            """;
        cmd.Parameters.AddWithValue("$cid", courseId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new CourseField
            {
                Id = r.GetInt32(0),
                CourseId = r.GetInt32(1),
                GroupId = r.IsDBNull(2) ? null : r.GetInt32(2),
                GroupName = r.GetString(3),
                FieldName = r.GetString(4),
                FieldValue = r.GetString(5),
                FieldType = r.GetString(6),
                LessonDate = r.GetString(7),
                SortOrder = r.GetInt32(8)
            });
        }
        return result;
    }

    public CourseField AddCourseField(int courseId, string fieldName, string? fieldValue, string fieldType, string? lessonDate, int? groupId, int sortOrder = 1)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Название пары (поля) не может быть пустым.");

        using var conn = _db.CreateConnection();
        EnsureGroupExistsIfSet(conn, groupId);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CourseFields (CourseId, GroupId, FieldName, FieldValue, FieldType, LessonDate, SortOrder)
            VALUES ($cid, $gid, $name, $val, $type, $ld, $ord);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$cid", courseId);
        cmd.Parameters.AddWithValue("$gid", (object?)groupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", fieldName.Trim());
        cmd.Parameters.AddWithValue("$val", (fieldValue ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(fieldType) ? "Лекция" : fieldType.Trim());
        cmd.Parameters.AddWithValue("$ld", (lessonDate ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$ord", sortOrder);

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetCourseFields(courseId).First(f => f.Id == id);
    }

    public void UpdateCourseField(int fieldId, string fieldName, string? fieldValue, string fieldType, string? lessonDate, int? groupId, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Название пары (поля) не может быть пустым.");

        using var conn = _db.CreateConnection();
        EnsureGroupExistsIfSet(conn, groupId);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE CourseFields
            SET GroupId = $gid,
                FieldName = $name,
                FieldValue = $val,
                FieldType = $type,
                LessonDate = $ld,
                SortOrder = $ord
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$gid", (object?)groupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", fieldName.Trim());
        cmd.Parameters.AddWithValue("$val", (fieldValue ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(fieldType) ? "Лекция" : fieldType.Trim());
        cmd.Parameters.AddWithValue("$ld", (lessonDate ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("$ord", sortOrder);
        cmd.Parameters.AddWithValue("$id", fieldId);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Пара (поле факультатива) не найдена.");
    }

    public void DeleteCourseField(int fieldId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CourseFields WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", fieldId);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Пара (поле факультатива) не найдена.");
    }

    // ------------------------------------------------------------------
    // 6. ЗАЧИСЛЕНИЯ СТУДЕНТОВ (БЕЗ ЛИМИТА ПО ЧИСЛУ), ЗАЧЕТЫ И ОЦЕНКИ ЗА ПАРЫ
    // ------------------------------------------------------------------

    public List<Enrollment> GetCourseEnrollments(int courseId) =>
        QueryEnrollments("WHERE e.CourseId = $cid", ("$cid", courseId));

    public List<Enrollment> GetStudentEnrollments(int studentId) =>
        QueryEnrollments("WHERE e.StudentId = $sid", ("$sid", studentId));

    /// <summary>
    /// Добавляет студента на факультатив (и при указании группы — привязывает к ней и к группе курса).
    /// Ограничений на количество студентов нет.
    /// </summary>
    public Enrollment EnrollStudent(int courseId, int studentId, int? groupId = null)
    {
        using var conn = _db.CreateConnection();

        var student = GetUserById(studentId) ?? throw new InvalidOperationException("Студент не найден.");
        if (student.Role != UserRole.Student)
            throw new InvalidOperationException("На факультативный курс можно зачислить только пользователя с ролью «Студент».");
        if (!student.IsActive)
            throw new InvalidOperationException("Учетная запись студента неактивна.");

        using var courseCmd = conn.CreateCommand();
        courseCmd.CommandText = "SELECT COUNT(*) FROM Courses WHERE Id = $cid;";
        courseCmd.Parameters.AddWithValue("$cid", courseId);
        if (Convert.ToInt32(courseCmd.ExecuteScalar()) == 0)
            throw new InvalidOperationException("Выбранный факультатив не найден.");

        using var dupCmd = conn.CreateCommand();
        dupCmd.CommandText = "SELECT COUNT(*) FROM Enrollments WHERE CourseId = $cid AND StudentId = $sid;";
        dupCmd.Parameters.AddWithValue("$cid", courseId);
        dupCmd.Parameters.AddWithValue("$sid", studentId);
        if (Convert.ToInt32(dupCmd.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Студент уже добавлен на этот факультативный курс.");

        EnsureGroupExistsIfSet(conn, groupId);

        if (!groupId.HasValue)
        {
            // Пытаемся определить по таблице GroupStudents в рамках привязанных групп курса
            using var gFind = conn.CreateCommand();
            gFind.CommandText = """
                SELECT gs.GroupId
                FROM GroupStudents gs
                JOIN CourseGroupLinks cgl ON cgl.GroupId = gs.GroupId AND cgl.CourseId = $cid
                WHERE gs.StudentId = $sid
                LIMIT 1;
                """;
            gFind.Parameters.AddWithValue("$cid", courseId);
            gFind.Parameters.AddWithValue("$sid", studentId);
            var found = gFind.ExecuteScalar();
            if (found is not null && found != DBNull.Value)
                groupId = Convert.ToInt32(found);
        }

        using var tx = conn.BeginTransaction();
        if (groupId.HasValue)
        {
            using var linkCmd = conn.CreateCommand();
            linkCmd.Transaction = tx;
            linkCmd.CommandText = "INSERT OR IGNORE INTO CourseGroupLinks (CourseId, GroupId) VALUES ($c, $g);";
            linkCmd.Parameters.AddWithValue("$c", courseId);
            linkCmd.Parameters.AddWithValue("$g", groupId.Value);
            linkCmd.ExecuteNonQuery();

            using var memCmd = conn.CreateCommand();
            memCmd.Transaction = tx;
            memCmd.CommandText = "INSERT OR IGNORE INTO GroupStudents (GroupId, StudentId) VALUES ($g, $s);";
            memCmd.Parameters.AddWithValue("$g", groupId.Value);
            memCmd.Parameters.AddWithValue("$s", studentId);
            memCmd.ExecuteNonQuery();
        }

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO Enrollments (CourseId, StudentId, GroupId, EnrolledAt, Status)
            VALUES ($cid, $sid, $gid, $dt, 'Active');
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$cid", courseId);
        ins.Parameters.AddWithValue("$sid", studentId);
        ins.Parameters.AddWithValue("$gid", (object?)groupId ?? DBNull.Value);
        ins.Parameters.AddWithValue("$dt", now);

        var id = Convert.ToInt32(ins.ExecuteScalar());
        tx.Commit();
        return QueryEnrollments("WHERE e.Id = $eid", ("$eid", id)).First();
    }

    public void AssignStudentToGroup(int enrollmentId, int? groupId)
    {
        using var conn = _db.CreateConnection();
        EnsureGroupExistsIfSet(conn, groupId);

        using var getInfo = conn.CreateCommand();
        getInfo.CommandText = "SELECT CourseId, StudentId FROM Enrollments WHERE Id = $id;";
        getInfo.Parameters.AddWithValue("$id", enrollmentId);
        using var r = getInfo.ExecuteReader();
        if (!r.Read())
            throw new InvalidOperationException("Запись студента не найдена.");
        var courseId = r.GetInt32(0);
        var studentId = r.GetInt32(1);
        r.Close();

        using var tx = conn.BeginTransaction();
        if (groupId.HasValue)
        {
            using var linkCmd = conn.CreateCommand();
            linkCmd.Transaction = tx;
            linkCmd.CommandText = "INSERT OR IGNORE INTO CourseGroupLinks (CourseId, GroupId) VALUES ($c, $g);";
            linkCmd.Parameters.AddWithValue("$c", courseId);
            linkCmd.Parameters.AddWithValue("$g", groupId.Value);
            linkCmd.ExecuteNonQuery();

            using var memCmd = conn.CreateCommand();
            memCmd.Transaction = tx;
            memCmd.CommandText = "INSERT OR IGNORE INTO GroupStudents (GroupId, StudentId) VALUES ($g, $s);";
            memCmd.Parameters.AddWithValue("$g", groupId.Value);
            memCmd.Parameters.AddWithValue("$s", studentId);
            memCmd.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Enrollments SET GroupId = $gid WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$gid", (object?)groupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", enrollmentId);
        cmd.ExecuteNonQuery();

        tx.Commit();
    }

    public void RemoveEnrollment(int enrollmentId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Enrollments WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", enrollmentId);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Запись студента на курс не найдена.");
    }

    public void SetEnrollmentGrade(int enrollmentId, string? grade, string? comment)
    {
        var normalizedGrade = GradeOptions.Normalize(grade);
        var cleanComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Enrollments
            SET Grade = $grade,
                Comment = $comment,
                Status = $status,
                GradedAt = $dt
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$grade", (object?)normalizedGrade ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$comment", (object?)cleanComment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", normalizedGrade is null ? "Active" : "Completed");
        cmd.Parameters.AddWithValue("$dt", normalizedGrade is null ? DBNull.Value : DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        cmd.Parameters.AddWithValue("$id", enrollmentId);

        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Запись студента на курс не найдена.");
    }

    public List<LessonGradeEntry> GetLessonGradesForCourse(int courseId)
    {
        var result = new List<LessonGradeEntry>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.Id, f.CourseId, f.FieldName, f.FieldType, f.LessonDate,
                   COALESCE(fg.Name, 'Все группы') AS TargetGroupName,
                   s.Id AS StudentId, s.FullName AS StudentName,
                   COALESCE(eg.Name, COALESCE(s.StudentGroup, 'Без группы')) AS CourseGroupName,
                   lg.Grade, lg.Comment, lg.GradedAt
            FROM CourseFields f
            LEFT JOIN StudyGroups fg ON fg.Id = f.GroupId
            JOIN Enrollments e ON e.CourseId = f.CourseId
                AND (f.GroupId IS NULL OR e.GroupId = f.GroupId)
            JOIN Users s ON s.Id = e.StudentId
            LEFT JOIN StudyGroups eg ON eg.Id = e.GroupId
            LEFT JOIN LessonGrades lg ON lg.FieldId = f.Id AND lg.StudentId = s.Id
            WHERE f.CourseId = $cid
            ORDER BY CourseGroupName ASC, s.FullName ASC, f.SortOrder ASC, f.Id ASC;
            """;
        cmd.Parameters.AddWithValue("$cid", courseId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new LessonGradeEntry
            {
                FieldId = r.GetInt32(0),
                CourseId = r.GetInt32(1),
                LessonTitle = r.GetString(2),
                LessonType = r.GetString(3),
                LessonDate = r.GetString(4),
                TargetGroupName = r.GetString(5),
                StudentId = r.GetInt32(6),
                StudentName = r.GetString(7),
                CourseGroupName = r.GetString(8),
                Grade = r.IsDBNull(9) ? null : r.GetString(9),
                Comment = r.IsDBNull(10) ? null : r.GetString(10),
                GradedAt = r.IsDBNull(11) ? null : r.GetString(11)
            });
        }
        return result;
    }

    public List<LessonGradeEntry> GetLessonGradesForField(int fieldId)
    {
        var result = new List<LessonGradeEntry>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.Id, f.CourseId, f.FieldName, f.FieldType, f.LessonDate,
                   COALESCE(fg.Name, 'Все группы') AS TargetGroupName,
                   s.Id AS StudentId, s.FullName AS StudentName,
                   COALESCE(eg.Name, COALESCE(s.StudentGroup, 'Без группы')) AS CourseGroupName,
                   lg.Grade, lg.Comment, lg.GradedAt
            FROM CourseFields f
            LEFT JOIN StudyGroups fg ON fg.Id = f.GroupId
            JOIN Enrollments e ON e.CourseId = f.CourseId
                AND (f.GroupId IS NULL OR e.GroupId = f.GroupId)
            JOIN Users s ON s.Id = e.StudentId
            LEFT JOIN StudyGroups eg ON eg.Id = e.GroupId
            LEFT JOIN LessonGrades lg ON lg.FieldId = f.Id AND lg.StudentId = s.Id
            WHERE f.Id = $fid
            ORDER BY CourseGroupName ASC, s.FullName ASC;
            """;
        cmd.Parameters.AddWithValue("$fid", fieldId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new LessonGradeEntry
            {
                FieldId = r.GetInt32(0),
                CourseId = r.GetInt32(1),
                LessonTitle = r.GetString(2),
                LessonType = r.GetString(3),
                LessonDate = r.GetString(4),
                TargetGroupName = r.GetString(5),
                StudentId = r.GetInt32(6),
                StudentName = r.GetString(7),
                CourseGroupName = r.GetString(8),
                Grade = r.IsDBNull(9) ? null : r.GetString(9),
                Comment = r.IsDBNull(10) ? null : r.GetString(10),
                GradedAt = r.IsDBNull(11) ? null : r.GetString(11)
            });
        }
        return result;
    }

    public List<LessonGradeEntry> GetStudentLessonGrades(int courseId, int studentId)
    {
        var result = new List<LessonGradeEntry>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.Id, f.CourseId, f.FieldName, f.FieldType, f.LessonDate,
                   COALESCE(fg.Name, 'Все группы') AS TargetGroupName,
                   $sid AS StudentId, s.FullName AS StudentName,
                   COALESCE(eg.Name, COALESCE(s.StudentGroup, 'Без группы')) AS CourseGroupName,
                   lg.Grade, lg.Comment, lg.GradedAt
            FROM CourseFields f
            JOIN Enrollments e ON e.CourseId = f.CourseId AND e.StudentId = $sid
            JOIN Users s ON s.Id = $sid
            LEFT JOIN StudyGroups fg ON fg.Id = f.GroupId
            LEFT JOIN StudyGroups eg ON eg.Id = e.GroupId
            LEFT JOIN LessonGrades lg ON lg.FieldId = f.Id AND lg.StudentId = $sid
            WHERE f.CourseId = $cid
              AND (f.GroupId IS NULL OR e.GroupId = f.GroupId)
            ORDER BY f.SortOrder ASC, f.Id ASC;
            """;
        cmd.Parameters.AddWithValue("$cid", courseId);
        cmd.Parameters.AddWithValue("$sid", studentId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new LessonGradeEntry
            {
                FieldId = r.GetInt32(0),
                CourseId = r.GetInt32(1),
                LessonTitle = r.GetString(2),
                LessonType = r.GetString(3),
                LessonDate = r.GetString(4),
                TargetGroupName = r.GetString(5),
                StudentId = r.GetInt32(6),
                StudentName = r.GetString(7),
                CourseGroupName = r.GetString(8),
                Grade = r.IsDBNull(9) ? null : r.GetString(9),
                Comment = r.IsDBNull(10) ? null : r.GetString(10),
                GradedAt = r.IsDBNull(11) ? null : r.GetString(11)
            });
        }
        return result;
    }

    public void SetLessonGrade(int fieldId, int studentId, string? grade, string? comment)
    {
        var normalizedGrade = GradeOptions.Normalize(grade);
        var cleanComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        var now = normalizedGrade is null && cleanComment is null ? null : DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        using var conn = _db.CreateConnection();
        using var check = conn.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*)
            FROM CourseFields f
            JOIN Enrollments e ON e.CourseId = f.CourseId AND e.StudentId = $sid
            WHERE f.Id = $fid;
            """;
        check.Parameters.AddWithValue("$fid", fieldId);
        check.Parameters.AddWithValue("$sid", studentId);
        if (Convert.ToInt32(check.ExecuteScalar()) == 0)
            throw new InvalidOperationException("Студент не зачислен на факультатив, к которому относится эта пара.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LessonGrades (FieldId, StudentId, Grade, Comment, GradedAt)
            VALUES ($fid, $sid, $grade, $comment, $dt)
            ON CONFLICT(FieldId, StudentId) DO UPDATE SET
                Grade = excluded.Grade,
                Comment = excluded.Comment,
                GradedAt = excluded.GradedAt;
            """;
        cmd.Parameters.AddWithValue("$fid", fieldId);
        cmd.Parameters.AddWithValue("$sid", studentId);
        cmd.Parameters.AddWithValue("$grade", (object?)normalizedGrade ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$comment", (object?)cleanComment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dt", (object?)now ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ------------------------------------------------------------------

    private List<Enrollment> QueryEnrollments(string whereClause, params (string Key, object Value)[] parameters)
    {
        var result = new List<Enrollment>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT e.Id, e.CourseId, c.Title AS CourseTitle, t.FullName AS TeacherName,
                   e.StudentId, s.FullName AS StudentName, s.StudentGroup,
                   e.GroupId, COALESCE(g.Name, COALESCE(s.StudentGroup, 'Без группы')) AS CourseGroupName,
                   e.EnrolledAt, e.Status, e.Grade, e.Comment, e.GradedAt
            FROM Enrollments e
            JOIN Courses c ON c.Id = e.CourseId
            JOIN Users t ON t.Id = c.TeacherId
            JOIN Users s ON s.Id = e.StudentId
            LEFT JOIN StudyGroups g ON g.Id = e.GroupId
            {whereClause}
            ORDER BY e.Id DESC;
            """;
        foreach (var (k, v) in parameters)
            cmd.Parameters.AddWithValue(k, v);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new Enrollment
            {
                Id = r.GetInt32(0),
                CourseId = r.GetInt32(1),
                CourseTitle = r.GetString(2),
                TeacherName = r.GetString(3),
                StudentId = r.GetInt32(4),
                StudentName = r.GetString(5),
                StudentGroup = r.IsDBNull(6) ? null : r.GetString(6),
                CourseGroupId = r.IsDBNull(7) ? null : r.GetInt32(7),
                CourseGroupName = r.GetString(8),
                EnrolledAt = r.GetString(9),
                Status = r.GetString(10),
                Grade = r.IsDBNull(11) ? null : r.GetString(11),
                Comment = r.IsDBNull(12) ? null : r.GetString(12),
                GradedAt = r.IsDBNull(13) ? null : r.GetString(13)
            });
        }
        return result;
    }

    private static void EnsureGroupExistsIfSet(SqliteConnection conn, int? groupId)
    {
        if (!groupId.HasValue) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM StudyGroups WHERE Id = $gid;";
        cmd.Parameters.AddWithValue("$gid", groupId.Value);
        if (Convert.ToInt32(cmd.ExecuteScalar()) == 0)
            throw new InvalidOperationException("Выбранная учебная группа не найдена.");
    }

    private static void ValidateCourseInput(string title, string description, int teacherId, int totalHours)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Наименование факультатива не может быть пустым.");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Краткое описание факультатива обязательно.");
        if (teacherId <= 0)
            throw new ArgumentException("За факультативом должен быть закреплен ровно один преподаватель.");
        if (totalHours <= 0 || totalHours > 500)
            throw new ArgumentException("Количество часов должно быть от 1 до 500.");
    }

    private static void EnsureTeacherExists(SqliteConnection conn, int teacherId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Role, IsActive FROM Users WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", teacherId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            throw new InvalidOperationException("Выбранный преподаватель не найден.");
        if (r.GetString(0) != "Teacher")
            throw new InvalidOperationException("Закрепить за факультативом можно только пользователя с ролью «Преподаватель».");
        if (r.GetInt32(1) != 1)
            throw new InvalidOperationException("Выбранный преподаватель заблокирован.");
    }

    private static User MapUser(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Login = r.GetString(1),
        PasswordHash = r.GetString(2),
        FullName = r.GetString(3),
        Role = UserRoleExtensions.ParseRole(r.GetString(4)),
        StudentGroup = r.IsDBNull(5) ? null : r.GetString(5),
        Email = r.IsDBNull(6) ? null : r.GetString(6),
        IsActive = r.GetInt32(7) == 1,
        CreatedAt = r.GetString(8)
    };
}
