using FacultativeSystem.Core.Services;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace FacultativeSystem.Core.Data;

/// <summary>
/// Управляет подключением, созданием/обновлением схемы SQLite (v5), начальным заполнением и резервным копированием БД.
/// </summary>
public class DatabaseService
{
    private const int CurrentSchemaVersion = 5;
    private readonly string _dbPath;
    private readonly string _connectionString;

    static DatabaseService()
    {
        Batteries_V2.Init();
    }

    public DatabaseService(string? dbPath = null)
    {
        _dbPath = string.IsNullOrWhiteSpace(dbPath)
            ? Path.Combine(AppContext.BaseDirectory, "facultative.db")
            : dbPath;

        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public string DatabasePath => _dbPath;

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public void Initialize()
    {
        using var conn = CreateConnection();

        int version;
        using (var verCmd = conn.CreateCommand())
        {
            verCmd.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt32(verCmd.ExecuteScalar());
        }

        bool hasExistingTables;
        using (var tblCmd = conn.CreateCommand())
        {
            tblCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Users';";
            hasExistingTables = Convert.ToInt32(tblCmd.ExecuteScalar()) > 0;
        }

        // Если БД создана совсем старой версией (< v3), пересоздаем таблицы под актуальную схему
        if (hasExistingTables && version < 3)
        {
            using var dropCmd = conn.CreateCommand();
            dropCmd.CommandText = """
                PRAGMA foreign_keys = OFF;
                DROP TABLE IF EXISTS LessonGrades;
                DROP TABLE IF EXISTS Enrollments;
                DROP TABLE IF EXISTS CourseFields;
                DROP TABLE IF EXISTS CourseGroupLinks;
                DROP TABLE IF EXISTS GroupStudents;
                DROP TABLE IF EXISTS CourseGroups;
                DROP TABLE IF EXISTS StudyGroups;
                DROP TABLE IF EXISTS Courses;
                DROP TABLE IF EXISTS Users;
                PRAGMA foreign_keys = ON;
                """;
            dropCmd.ExecuteNonQuery();
            version = 0;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Login TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                FullName TEXT NOT NULL,
                Role TEXT NOT NULL CHECK(Role IN ('Admin', 'Teacher', 'Student')),
                StudentGroup TEXT NULL,
                Email TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Courses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                TeacherId INTEGER NOT NULL,
                TotalHours INTEGER NOT NULL DEFAULT 36,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TeacherId) REFERENCES Users(Id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS StudyGroups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS GroupStudents (
                GroupId INTEGER NOT NULL,
                StudentId INTEGER NOT NULL,
                PRIMARY KEY (GroupId, StudentId),
                FOREIGN KEY (GroupId) REFERENCES StudyGroups(Id) ON DELETE CASCADE,
                FOREIGN KEY (StudentId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CourseGroupLinks (
                CourseId INTEGER NOT NULL,
                GroupId INTEGER NOT NULL,
                PRIMARY KEY (CourseId, GroupId),
                FOREIGN KEY (CourseId) REFERENCES Courses(Id) ON DELETE CASCADE,
                FOREIGN KEY (GroupId) REFERENCES StudyGroups(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS CourseFields (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                GroupId INTEGER NULL,
                FieldName TEXT NOT NULL,
                FieldValue TEXT NOT NULL DEFAULT '',
                FieldType TEXT NOT NULL DEFAULT 'Лекция',
                LessonDate TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (CourseId) REFERENCES Courses(Id) ON DELETE CASCADE,
                FOREIGN KEY (GroupId) REFERENCES StudyGroups(Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS Enrollments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                StudentId INTEGER NOT NULL,
                GroupId INTEGER NULL,
                EnrolledAt TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Active' CHECK(Status IN ('Active', 'Completed')),
                Grade TEXT NULL CHECK(Grade IS NULL OR Grade IN ('5','4','3','2','н/я','—','Зачёт','Незачёт')),
                Comment TEXT NULL,
                GradedAt TEXT NULL,
                FOREIGN KEY (CourseId) REFERENCES Courses(Id) ON DELETE CASCADE,
                FOREIGN KEY (StudentId) REFERENCES Users(Id) ON DELETE CASCADE,
                FOREIGN KEY (GroupId) REFERENCES StudyGroups(Id) ON DELETE SET NULL,
                UNIQUE (CourseId, StudentId)
            );

            CREATE TABLE IF NOT EXISTS LessonGrades (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FieldId INTEGER NOT NULL,
                StudentId INTEGER NOT NULL,
                Grade TEXT NULL CHECK(Grade IS NULL OR Grade IN ('5','4','3','2','н/я','—','Зачёт','Незачёт')),
                Comment TEXT NULL,
                GradedAt TEXT NULL,
                FOREIGN KEY (FieldId) REFERENCES CourseFields(Id) ON DELETE CASCADE,
                FOREIGN KEY (StudentId) REFERENCES Users(Id) ON DELETE CASCADE,
                UNIQUE (FieldId, StudentId)
            );

            CREATE INDEX IF NOT EXISTS IX_Courses_TeacherId ON Courses(TeacherId);
            CREATE INDEX IF NOT EXISTS IX_CourseFields_CourseId ON CourseFields(CourseId);
            CREATE INDEX IF NOT EXISTS IX_Enrollments_StudentId ON Enrollments(StudentId);
            CREATE INDEX IF NOT EXISTS IX_LessonGrades_FieldId ON LessonGrades(FieldId);
            """;
        cmd.ExecuteNonQuery();

        if (hasExistingTables && version == 3)
            MigrateV3FieldsToPerGroup(conn);
        if (hasExistingTables && version >= 3 && version < 5)
            MigrateV4AddDashGradeConstraint(conn);

        using (var setVer = conn.CreateCommand())
        {
            setVer.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            setVer.ExecuteNonQuery();
        }

        SeedIfEmpty(conn);
    }

    private static void MigrateV3FieldsToPerGroup(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        var nullFields = new List<(int Id, int CourseId, string Name, string Val, string Type, string Date, int SortOrder)>();
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT Id, CourseId, FieldName, FieldValue, FieldType, LessonDate, SortOrder FROM CourseFields WHERE GroupId IS NULL;";
            using var r = read.ExecuteReader();
            while (r.Read())
                nullFields.Add((r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6)));
        }

        foreach (var f in nullFields)
        {
            var groupIds = new List<int>();
            using (var gCmd = conn.CreateCommand())
            {
                gCmd.Transaction = tx;
                gCmd.CommandText = "SELECT GroupId FROM CourseGroupLinks WHERE CourseId = $cid ORDER BY GroupId ASC;";
                gCmd.Parameters.AddWithValue("$cid", f.CourseId);
                using var gr = gCmd.ExecuteReader();
                while (gr.Read())
                    groupIds.Add(gr.GetInt32(0));
            }

            if (groupIds.Count == 0)
                continue;

            foreach (var gid in groupIds)
            {
                int newFieldId;
                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = """
                        INSERT INTO CourseFields (CourseId, GroupId, FieldName, FieldValue, FieldType, LessonDate, SortOrder)
                        VALUES ($cid, $gid, $n, $v, $ft, $ld, $ord);
                        SELECT last_insert_rowid();
                        """;
                    ins.Parameters.AddWithValue("$cid", f.CourseId);
                    ins.Parameters.AddWithValue("$gid", gid);
                    ins.Parameters.AddWithValue("$n", f.Name);
                    ins.Parameters.AddWithValue("$v", f.Val);
                    ins.Parameters.AddWithValue("$ft", f.Type);
                    ins.Parameters.AddWithValue("$ld", f.Date);
                    ins.Parameters.AddWithValue("$ord", f.SortOrder);
                    newFieldId = Convert.ToInt32(ins.ExecuteScalar());
                }

                using var copyGrades = conn.CreateCommand();
                copyGrades.Transaction = tx;
                copyGrades.CommandText = """
                    INSERT OR IGNORE INTO LessonGrades (FieldId, StudentId, Grade, Comment, GradedAt)
                    SELECT $newFid, lg.StudentId, lg.Grade, lg.Comment, lg.GradedAt
                    FROM LessonGrades lg
                    JOIN Enrollments e ON e.StudentId = lg.StudentId AND e.CourseId = $cid AND e.GroupId = $gid
                    WHERE lg.FieldId = $oldFid;
                    """;
                copyGrades.Parameters.AddWithValue("$newFid", newFieldId);
                copyGrades.Parameters.AddWithValue("$cid", f.CourseId);
                copyGrades.Parameters.AddWithValue("$gid", gid);
                copyGrades.Parameters.AddWithValue("$oldFid", f.Id);
                copyGrades.ExecuteNonQuery();
            }

            using var delOld = conn.CreateCommand();
            delOld.Transaction = tx;
            delOld.CommandText = "DELETE FROM CourseFields WHERE Id = $id;";
            delOld.Parameters.AddWithValue("$id", f.Id);
            delOld.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void MigrateV4AddDashGradeConstraint(SqliteConnection conn)
    {
        using var fkOff = conn.CreateCommand();
        fkOff.CommandText = "PRAGMA foreign_keys = OFF;";
        fkOff.ExecuteNonQuery();

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE Enrollments_new (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CourseId INTEGER NOT NULL,
                    StudentId INTEGER NOT NULL,
                    GroupId INTEGER NULL,
                    EnrolledAt TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Active' CHECK(Status IN ('Active', 'Completed')),
                    Grade TEXT NULL CHECK(Grade IS NULL OR Grade IN ('5','4','3','2','н/я','—','Зачёт','Незачёт')),
                    Comment TEXT NULL,
                    GradedAt TEXT NULL,
                    FOREIGN KEY (CourseId) REFERENCES Courses(Id) ON DELETE CASCADE,
                    FOREIGN KEY (StudentId) REFERENCES Users(Id) ON DELETE CASCADE,
                    FOREIGN KEY (GroupId) REFERENCES StudyGroups(Id) ON DELETE SET NULL,
                    UNIQUE (CourseId, StudentId)
                );
                INSERT INTO Enrollments_new (Id, CourseId, StudentId, GroupId, EnrolledAt, Status, Grade, Comment, GradedAt)
                SELECT Id, CourseId, StudentId, GroupId, EnrolledAt, Status, Grade, Comment, GradedAt FROM Enrollments;
                DROP TABLE Enrollments;
                ALTER TABLE Enrollments_new RENAME TO Enrollments;
                CREATE INDEX IF NOT EXISTS IX_Enrollments_StudentId ON Enrollments(StudentId);

                CREATE TABLE LessonGrades_new (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FieldId INTEGER NOT NULL,
                    StudentId INTEGER NOT NULL,
                    Grade TEXT NULL CHECK(Grade IS NULL OR Grade IN ('5','4','3','2','н/я','—','Зачёт','Незачёт')),
                    Comment TEXT NULL,
                    GradedAt TEXT NULL,
                    FOREIGN KEY (FieldId) REFERENCES CourseFields(Id) ON DELETE CASCADE,
                    FOREIGN KEY (StudentId) REFERENCES Users(Id) ON DELETE CASCADE,
                    UNIQUE (FieldId, StudentId)
                );
                INSERT INTO LessonGrades_new (Id, FieldId, StudentId, Grade, Comment, GradedAt)
                SELECT Id, FieldId, StudentId, Grade, Comment, GradedAt FROM LessonGrades;
                DROP TABLE LessonGrades;
                ALTER TABLE LessonGrades_new RENAME TO LessonGrades;
                CREATE INDEX IF NOT EXISTS IX_LessonGrades_FieldId ON LessonGrades(FieldId);
                """;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();

        using var fkOn = conn.CreateCommand();
        fkOn.CommandText = "PRAGMA foreign_keys = ON;";
        fkOn.ExecuteNonQuery();
    }

    private static void SeedIfEmpty(SqliteConnection conn)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Users;";
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            return;

        using var tx = conn.BeginTransaction();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        int InsertUser(string login, string pwd, string fullName, string role, string? group, string email)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = """
                INSERT INTO Users (Login, PasswordHash, FullName, Role, StudentGroup, Email, IsActive, CreatedAt)
                VALUES ($login, $hash, $name, $role, $grp, $email, 1, $dt);
                SELECT last_insert_rowid();
                """;
            c.Parameters.AddWithValue("$login", login);
            c.Parameters.AddWithValue("$hash", PasswordHasher.Hash(pwd));
            c.Parameters.AddWithValue("$name", fullName);
            c.Parameters.AddWithValue("$role", role);
            c.Parameters.AddWithValue("$grp", (object?)group ?? DBNull.Value);
            c.Parameters.AddWithValue("$email", email);
            c.Parameters.AddWithValue("$dt", now);
            return Convert.ToInt32(c.ExecuteScalar());
        }

        _ = InsertUser("admin", "admin123", "Смирнов Алексей Викторович", "Admin", null, "admin@kbk.edu.ru");
        var tParamonov = InsertUser("paramonov", "123456", "Парамонов Дмитрий Дмитриевич", "Teacher", null, "paramonov@kbk.edu.ru");
        var tDubinin = InsertUser("dubinin", "123456", "Дубинин Андрей Владимирович", "Teacher", null, "dubinin@kbk.edu.ru");
        var tIvanova = InsertUser("ivanova", "123456", "Иванова Елена Сергеевна", "Teacher", null, "ivanova@kbk.edu.ru");

        var sGabitov = InsertUser("gabitov", "123456", "Габитов Руслан Тимурович", "Student", "24-ИСП-3/1", "gabitov@kbk.edu.ru");
        var sPetrov = InsertUser("petrov", "123456", "Петров Максим Олегович", "Student", "24-ИСП-3/1", "petrov@kbk.edu.ru");
        var sSidorova = InsertUser("sidorova", "123456", "Сидорова Анна Алексеевна", "Student", "24-ИСП-4/1", "sidorova@kbk.edu.ru");
        var sVolkov = InsertUser("volkov", "123456", "Волков Денис Игоревич", "Student", "24-ИСП-3/1", "volkov@kbk.edu.ru");
        var sKuznetsova = InsertUser("kuznetsova", "123456", "Кузнецова Мария Павловна", "Student", "24-ИСП-4/1", "kuznetsova@kbk.edu.ru");

        int InsertGroup(string name)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT INTO StudyGroups (Name) VALUES ($n); SELECT last_insert_rowid();";
            c.Parameters.AddWithValue("$n", name);
            return Convert.ToInt32(c.ExecuteScalar());
        }

        void AddStudentToGroup(int groupId, int studentId)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT OR IGNORE INTO GroupStudents (GroupId, StudentId) VALUES ($g, $s);";
            c.Parameters.AddWithValue("$g", groupId);
            c.Parameters.AddWithValue("$s", studentId);
            c.ExecuteNonQuery();
        }

        var g31 = InsertGroup("24-ИСП-3/1");
        var g41 = InsertGroup("24-ИСП-4/1");

        AddStudentToGroup(g31, sGabitov);
        AddStudentToGroup(g31, sPetrov);
        AddStudentToGroup(g31, sVolkov);
        AddStudentToGroup(g41, sSidorova);
        AddStudentToGroup(g41, sKuznetsova);

        int InsertCourse(string title, string desc, int teacherId, int hours)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = """
                INSERT INTO Courses (Title, Description, TeacherId, TotalHours, CreatedAt)
                VALUES ($t, $d, $tid, $h, $dt);
                SELECT last_insert_rowid();
                """;
            c.Parameters.AddWithValue("$t", title);
            c.Parameters.AddWithValue("$d", desc);
            c.Parameters.AddWithValue("$tid", teacherId);
            c.Parameters.AddWithValue("$h", hours);
            c.Parameters.AddWithValue("$dt", now);
            return Convert.ToInt32(c.ExecuteScalar());
        }

        void LinkCourseGroup(int courseId, int groupId)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT OR IGNORE INTO CourseGroupLinks (CourseId, GroupId) VALUES ($c, $g);";
            c.Parameters.AddWithValue("$c", courseId);
            c.Parameters.AddWithValue("$g", groupId);
            c.ExecuteNonQuery();
        }

        int InsertField(int courseId, int? groupId, string name, string val, string ftype, string date, int order)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = """
                INSERT INTO CourseFields (CourseId, GroupId, FieldName, FieldValue, FieldType, LessonDate, SortOrder)
                VALUES ($cid, $gid, $n, $v, $ft, $ld, $ord);
                SELECT last_insert_rowid();
                """;
            c.Parameters.AddWithValue("$cid", courseId);
            c.Parameters.AddWithValue("$gid", (object?)groupId ?? DBNull.Value);
            c.Parameters.AddWithValue("$n", name);
            c.Parameters.AddWithValue("$v", val);
            c.Parameters.AddWithValue("$ft", ftype);
            c.Parameters.AddWithValue("$ld", date);
            c.Parameters.AddWithValue("$ord", order);
            return Convert.ToInt32(c.ExecuteScalar());
        }

        void InsertEnrollment(int courseId, int studentId, int? groupId, string status, string? grade, string? comment, string? gradedAt)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = """
                INSERT INTO Enrollments (CourseId, StudentId, GroupId, EnrolledAt, Status, Grade, Comment, GradedAt)
                VALUES ($cid, $sid, $gid, $dt, $st, $gr, $cm, $gdt);
                """;
            c.Parameters.AddWithValue("$cid", courseId);
            c.Parameters.AddWithValue("$sid", studentId);
            c.Parameters.AddWithValue("$gid", (object?)groupId ?? DBNull.Value);
            c.Parameters.AddWithValue("$dt", now);
            c.Parameters.AddWithValue("$st", status);
            c.Parameters.AddWithValue("$gr", (object?)grade ?? DBNull.Value);
            c.Parameters.AddWithValue("$cm", (object?)comment ?? DBNull.Value);
            c.Parameters.AddWithValue("$gdt", (object?)gradedAt ?? DBNull.Value);
            c.ExecuteNonQuery();
        }

        void InsertLessonGrade(int fieldId, int studentId, string? grade, string? comment)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = """
                INSERT INTO LessonGrades (FieldId, StudentId, Grade, Comment, GradedAt)
                VALUES ($fid, $sid, $gr, $cm, $dt);
                """;
            c.Parameters.AddWithValue("$fid", fieldId);
            c.Parameters.AddWithValue("$sid", studentId);
            c.Parameters.AddWithValue("$gr", (object?)grade ?? DBNull.Value);
            c.Parameters.AddWithValue("$cm", (object?)comment ?? DBNull.Value);
            c.Parameters.AddWithValue("$dt", now);
            c.ExecuteNonQuery();
        }

        // 1. Курс WPF (две группы с разным расписанием и своими оценками)
        var cWpf = InsertCourse(
            "Разработка настольных приложений на C# и WPF",
            "Изучение платформы .NET, разметки XAML, шаблона MVVM и работы с базой данных SQLite.",
            tParamonov, 48);
        LinkCourseGroup(cWpf, g31);
        LinkCourseGroup(cWpf, g41);

        var fWpf31_1 = InsertField(cWpf, g31, "Пара 1. Основы XAML и макетирование", "Контейнеры Grid, StackPanel, стили", "Лекция", "18.11.2026 15:30", 1);
        var fWpf31_2 = InsertField(cWpf, g31, "Пара 2. Практикум по DataBinding и MVVM", "Реализация ViewModel и команд", "Практика", "20.11.2026 15:30", 2);
        var fWpf31_3 = InsertField(cWpf, g31, "Пара 3. Подключение SQLite к WPF", "Таблицы и параметризованные запросы", "Лабораторная", "25.11.2026 15:30", 3);

        var fWpf41_1 = InsertField(cWpf, g41, "Пара 1. Введение в XAML (24-ИСП-4/1)", "Базовая верстка и привязка", "Лекция", "19.11.2026 13:40", 1);
        _ = InsertField(cWpf, g41, "Пара 2. Работа с SQLite", "SQL-запросы и CRUD", "Лабораторная", "26.11.2026 13:40", 2);

        InsertEnrollment(cWpf, sGabitov, g31, "Active", null, null, null);
        InsertEnrollment(cWpf, sPetrov, g31, "Active", null, null, null);
        InsertEnrollment(cWpf, sSidorova, g41, "Completed", "5", "Зачтено досрочно", now);

        InsertLessonGrade(fWpf31_1, sGabitov, "5", "Активная работа на паре");
        InsertLessonGrade(fWpf31_1, sPetrov, "н/я", "Отсутствовал по уважительной причине");
        InsertLessonGrade(fWpf31_2, sGabitov, "4", null);
        InsertLessonGrade(fWpf31_3, sGabitov, "Зачёт", "Лабораторная сдана");
        InsertLessonGrade(fWpf41_1, sSidorova, "5", "Отличное знание XAML");

        // 2. Курс SQLite
        var cSqlite = InsertCourse(
            "Проектирование реляционных баз данных (SQLite и SQL)",
            "Нормализация схем данных, внешние ключи, индексы и транзакции SQLite.",
            tDubinin, 36);
        LinkCourseGroup(cSqlite, g31);
        var fSql1 = InsertField(cSqlite, g31, "Пара 1. Нормализация и внешние ключи", "3НФ и PRAGMA foreign_keys", "Лекция", "19.11.2026 16:00", 1);
        _ = InsertField(cSqlite, g31, "Пара 2. Транзакции и Backup API", "ACID-транзакции в SQLite", "Практика", "26.11.2026 16:00", 2);
        InsertEnrollment(cSqlite, sGabitov, g31, "Active", null, null, null);
        InsertEnrollment(cSqlite, sVolkov, g31, "Active", null, null, null);
        InsertLessonGrade(fSql1, sGabitov, "5", null);

        // 3. Курс Python
        var cPython = InsertCourse(
            "Анализ данных и основы машинного обучения на Python",
            "Работа с pandas, scikit-learn и визуализация данных.",
            tIvanova, 40);
        LinkCourseGroup(cPython, g41);
        _ = InsertField(cPython, g41, "Пара 1. Обработка данных в pandas", "DataFrame и фильтрация", "Лекция", "23.11.2026 15:00", 1);
        InsertEnrollment(cPython, sSidorova, g41, "Active", null, null, null);

        // 4. Курс UML
        var cUml = InsertCourse(
            "UML-моделирование и документация по ЕСПД",
            "Диаграммы вариантов использования и ТЗ по ГОСТ 19.201-78.",
            tParamonov, 32);
        LinkCourseGroup(cUml, g31);
        var fUml1 = InsertField(cUml, g31, "Пара 1. Диаграмма вариантов использования", "Актеры и прецеденты", "Практика", "21.11.2026 14:30", 1);
        var fUml2 = InsertField(cUml, g31, "Итоговый зачёт", "Проверка ТЗ и диаграмм", "Зачёт", "24.11.2026 14:30", 2);
        InsertEnrollment(cUml, sGabitov, g31, "Completed", "Зачёт", "Работа сдана в срок", now);
        InsertLessonGrade(fUml1, sGabitov, "5", null);
        InsertLessonGrade(fUml2, sGabitov, "Зачёт", null);

        tx.Commit();
    }

    public void CreateBackup(string destinationFilePath)
    {
        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(destinationFilePath))
            File.Delete(destinationFilePath);

        using var source = CreateConnection();
        using var dest = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        dest.Open();
        source.BackupDatabase(dest);
    }
}
