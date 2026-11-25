using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FacultativeSystem.Core.Services;
using FacultativeSystem.ViewModels;

namespace FacultativeSystem;

public partial class App : Application
{
    private Process? _ownedApiProcess;

    public static bool ResolveDevMode(string[] args)
    {
        if (args.Any(a => a.Equals("--public", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (args.Any(a => a.Equals("--dev", StringComparison.OrdinalIgnoreCase)))
            return true;

        var env = Environment.GetEnvironmentVariable("FACULTATIVE_MODE");
        if (string.Equals(env, "public", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(env, "dev", StringComparison.OrdinalIgnoreCase))
            return true;

#if DEBUG
        return true;
#else
        return false;
#endif
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0] == "--export-screenshots")
        {
            await ExportScreenshotsAsync(e.Args[1]);
            Shutdown(0);
            return;
        }

        var isDevMode = ResolveDevMode(e.Args);
        var apiUrl = Environment.GetEnvironmentVariable("FACULTATIVE_API_URL") ?? "http://localhost:5150";
        var apiClient = new FacultativeApiClient(apiUrl);

        if (!await apiClient.CheckHealthAsync())
        {
            _ownedApiProcess = TryStartLocalApi(apiUrl, dbPathOverride: null);
            var ready = await WaitForApiAsync(apiClient, TimeSpan.FromSeconds(10));
            if (!ready)
            {
                MessageBox.Show(
                    $"Не удалось подключиться к серверу Web API по адресу {apiUrl}.\nЗапустите проект FacultativeSystem.Api и повторите попытку.",
                    "ИС «Факультатив» — Ошибка подключения к API",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        var mainVm = new MainViewModel(apiClient, isDevMode);
        var window = new MainWindow { DataContext = mainVm };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownedApiProcess is { HasExited: false })
        {
            try { _ownedApiProcess.Kill(entireProcessTree: true); } catch { }
        }
        base.OnExit(e);
    }

    private static Process? TryStartLocalApi(string apiUrl, string? dbPathOverride)
    {
        var apiExe = FindApiExecutable();
        if (apiExe is null) return null;

        var psi = new ProcessStartInfo
        {
            FileName = apiExe,
            WorkingDirectory = Path.GetDirectoryName(apiExe)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["ASPNETCORE_URLS"] = apiUrl;
        if (!string.IsNullOrWhiteSpace(dbPathOverride))
            psi.Environment["DatabasePath"] = dbPathOverride;

        return Process.Start(psi);
    }

    private static string? FindApiExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "FacultativeSystem.Api.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "FacultativeSystem.Api", "bin", "Debug", "net9.0", "FacultativeSystem.Api.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "FacultativeSystem.Api", "bin", "Release", "net9.0", "FacultativeSystem.Api.exe"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<bool> WaitForApiAsync(IFacultativeClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await client.CheckHealthAsync())
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    private static async Task ExportScreenshotsAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var tempDb = Path.Combine(outputDir, "temp_preview.db");
        if (File.Exists(tempDb)) File.Delete(tempDb);

        const string previewUrl = "http://127.0.0.1:5198";
        using var apiProc = TryStartLocalApi(previewUrl, tempDb)
            ?? throw new InvalidOperationException("FacultativeSystem.Api.exe не найден для снятия скриншотов.");

        try
        {
            var client = new FacultativeApiClient(previewUrl);
            if (!await WaitForApiAsync(client, TimeSpan.FromSeconds(10)))
                throw new TimeoutException("API не ответил вовремя при снятии скриншотов.");

            var mainVm = new MainViewModel(client);
            var window = new MainWindow
            {
                DataContext = mainVm,
                Width = 1280,
                Height = 820,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false
            };
            window.Show();

            async Task PumpAndSaveAsync(string fileName)
            {
                await Task.Delay(150);
                for (int i = 0; i < 4; i++)
                {
                    var frame = new DispatcherFrame();
                    _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new DispatcherOperationCallback(_ =>
                    {
                        frame.Continue = false;
                        return null;
                    }), null);
                    Dispatcher.PushFrame(frame);
                    window.UpdateLayout();
                }

                var w = (int)window.ActualWidth;
                var h = (int)window.ActualHeight;
                if (w <= 0) w = 1280;
                if (h <= 0) h = 820;
                var rtb = new RenderTargetBitmap(w, h, 96.0, 96.0, PixelFormats.Pbgra32);
                rtb.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(Path.Combine(outputDir, fileName));
                encoder.Save(fs);
            }

            // Экспортируем Public (строго по логину и паролю) и Dev (с быстрым входом) экраны авторизации
            var publicMainVm = new MainViewModel(client, isDevMode: false);
            window.DataContext = publicMainVm;
            await PumpAndSaveAsync("ui_login.png");

            mainVm = new MainViewModel(client, isDevMode: true);
            window.DataContext = mainVm;
            await PumpAndSaveAsync("ui_login_dev.png");

            var student = (await client.AuthenticateAsync("gabitov", "123456"))!;
            mainVm.OnAuthenticated(student);
            if (mainVm.CurrentViewModel is StudentViewModel svm)
            {
                await Task.Delay(250);
                await svm.LoadDataAsync();
                await PumpAndSaveAsync("ui_student_catalog.png");
                svm.SelectedTabIndex = 1;
                await PumpAndSaveAsync("ui_student_grades.png");
            }

            var teacher = (await client.AuthenticateAsync("paramonov", "123456"))!;
            mainVm.OnAuthenticated(teacher);
            if (mainVm.CurrentViewModel is TeacherViewModel tvm)
            {
                await Task.Delay(250);
                await tvm.LoadDataAsync();

                // Вкладка 1: Студенты, группы и зачёты (выберем первого студента для примера панели внизу)
                tvm.SelectedTabIndex = 0;
                tvm.SelectedEnrollment = tvm.Enrollments.FirstOrDefault();
                await PumpAndSaveAsync("ui_teacher_students.png");

                // Вкладка 2: Поля факультета (пары и расписание)
                tvm.SelectedTabIndex = 1;
                await PumpAndSaveAsync("ui_teacher_fields.png");

                // Вкладка 3: Оценки за пары (матрица как в Excel)
                tvm.SelectedTabIndex = 2;
                await tvm.LoadLessonMatrixAsync();
                tvm.SelectedMatrixCell = tvm.LessonMatrixRows.FirstOrDefault()?.CellsByField.Values.FirstOrDefault(c => c.IsApplicable);
                await PumpAndSaveAsync("ui_teacher_lesson_grades.png");

                if (tvm.SelectedCourse is not null)
                {
                    var boundIds = tvm.CourseGroups.Select(g => g.Id).ToHashSet();
                    var availableGroups = tvm.AllGroups.Where(g => !boundIds.Contains(g.Id)).ToList();
                    var dlg = new Views.AddGroupDialog(tvm.SelectedCourse.Title, availableGroups)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = -32000,
                        Top = -32000,
                        ShowInTaskbar = false
                    };
                    dlg.Show();
                    await Task.Delay(120);
                    for (int i = 0; i < 3; i++)
                    {
                        var frame = new DispatcherFrame();
                        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new DispatcherOperationCallback(_ =>
                        {
                            frame.Continue = false;
                            return null;
                        }), null);
                        Dispatcher.PushFrame(frame);
                        dlg.UpdateLayout();
                    }
                    var rtbDlg = new RenderTargetBitmap((int)dlg.ActualWidth, (int)dlg.ActualHeight, 96.0, 96.0, PixelFormats.Pbgra32);
                    rtbDlg.Render(dlg);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtbDlg));
                    using (var fs = File.Create(Path.Combine(outputDir, "ui_add_group_dialog.png")))
                        enc.Save(fs);
                    dlg.Close();
                }
            }

            var admin = (await client.AuthenticateAsync("admin", "admin123"))!;
            mainVm.OnAuthenticated(admin);
            if (mainVm.CurrentViewModel is AdminViewModel avm)
            {
                await Task.Delay(250);
                await avm.LoadDataAsync();
                await PumpAndSaveAsync("ui_admin_courses.png");
                avm.SelectedTabIndex = 1;
                await PumpAndSaveAsync("ui_admin_groups.png");
                avm.GroupRightTabIndex = 1;
                await PumpAndSaveAsync("ui_admin_groups_courses.png");
                avm.SelectedTabIndex = 2;
                await PumpAndSaveAsync("ui_admin_users.png");
            }

            window.Close();
        }
        finally
        {
            if (!apiProc.HasExited)
            {
                try { apiProc.Kill(entireProcessTree: true); } catch { }
            }
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }
}
