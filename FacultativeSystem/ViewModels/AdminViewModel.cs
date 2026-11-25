using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using FacultativeSystem.Infrastructure;
using Microsoft.Win32;

namespace FacultativeSystem.ViewModels;

/// <summary>
/// Рабочее пространство актера «Администратор»:
/// 1) Факультативы (поиск по названию и преподавателю, без лишних уточнений и лимитов);
/// 2) Группы (создание/редактирование/удаление, просмотр студентов группы, привязка к факультативам);
/// 3) Пользователи и изменения доступов (поиск/фильтр ролей, автоподбор группы по вводу);
/// 4) Единообразный модуль «Управление факультетом».
/// </summary>
public class AdminViewModel : ViewModelBase
{
    private readonly IFacultativeClient _client;
    private readonly User _currentUser;
    private readonly Action<string, bool> _notify;
    private List<User> _allUsersCache = new();
    private List<Course> _allCoursesCache = new();
    private List<CourseGroup> _allGroupsCache = new();
    private List<Course> _allGroupCoursesCache = new();
    private List<Course> _allAvailableCoursesToBindCache = new();

    private string _adminCourseSearchQuery = string.Empty;
    private string _groupSearchQuery = string.Empty;
    private string _groupBindCourseSearchQuery = string.Empty;
    private int _groupRightTabIndex;
    private Course? _selectedCourse;
    private CourseGroup? _selectedGroup;
    private Course? _selectedGroupCourse;
    private Course? _courseToBind;
    private User? _selectedUser;
    private int _selectedTabIndex;

    // Форма факультатива
    private string _courseTitle = string.Empty;
    private string _courseDescription = string.Empty;
    private User? _selectedTeacher;
    private string _teacherSearchText = string.Empty;
    private int _courseHours = 36;

    // Форма групп
    private string _groupNameInput = string.Empty;

    // Фильтры пользователей
    private string _userSearchQuery = string.Empty;
    private string _selectedUserRoleFilter = "Все роли";

    // Форма пользователя и прав доступа
    private string _userLogin = string.Empty;
    private string _userPassword = string.Empty;
    private string _userFullName = string.Empty;
    private UserRole _userRole = UserRole.Teacher;
    private string _userGroup = string.Empty;
    private string _userEmail = string.Empty;
    private bool _userIsActive = true;

    public AdminViewModel(IFacultativeClient client, User currentUser, Action<string, bool> notify)
    {
        _client = client;
        _currentUser = currentUser;
        _notify = notify;

        CourseManagementVm = new TeacherViewModel(client, currentUser, notify, isAdminOverride: true, autoLoad: false);

        CreateCourseCommand = new AsyncRelayCommand(ExecuteCreateCourseAsync);
        UpdateCourseCommand = new AsyncRelayCommand(ExecuteUpdateCourseAsync, () => SelectedCourse is not null);
        DeleteCourseCommand = new AsyncRelayCommand(ExecuteDeleteCourseAsync, () => SelectedCourse is not null);
        ClearCourseFormCommand = new RelayCommand(ClearCourseForm);

        CreateGroupCommand = new AsyncRelayCommand(ExecuteCreateGroupAsync);
        RenameGroupCommand = new AsyncRelayCommand(ExecuteRenameGroupAsync, () => SelectedGroup is not null);
        DeleteGroupCommand = new AsyncRelayCommand(ExecuteDeleteGroupAsync, () => SelectedGroup is not null);
        BindGroupToCourseCommand = new AsyncRelayCommand(ExecuteBindGroupToCourseAsync, () => SelectedGroup is not null && CourseToBind is not null);
        UnbindGroupFromCourseCommand = new AsyncRelayCommand(ExecuteUnbindGroupFromCourseAsync, () => SelectedGroup is not null && SelectedGroupCourse is not null);

        CreateUserCommand = new AsyncRelayCommand(ExecuteCreateUserAsync);
        UpdateUserAccessCommand = new AsyncRelayCommand(ExecuteUpdateUserAccessAsync, () => SelectedUser is not null);
        DeleteUserCommand = new AsyncRelayCommand(ExecuteDeleteUserAsync, () => SelectedUser is not null);
        ClearUserFormCommand = new RelayCommand(ClearUserForm);

        BackupDatabaseCommand = new AsyncRelayCommand(ExecuteBackupAsync);
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);

        _ = LoadDataAsync();
    }

    public TeacherViewModel CourseManagementVm { get; }

    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<User> Teachers { get; } = new();
    public ObservableCollection<User> FilteredTeacherSuggestions { get; } = new();
    public ObservableCollection<CourseGroup> Groups { get; } = new();
    public ObservableCollection<string> FilteredGroupSuggestions { get; } = new();
    public ObservableCollection<User> GroupStudents { get; } = new();
    public ObservableCollection<Course> GroupCourses { get; } = new();
    public ObservableCollection<Course> AvailableCoursesToBind { get; } = new();
    public ObservableCollection<User> Users { get; } = new();

    public List<UserRole> RoleOptions { get; } = new() { UserRole.Admin, UserRole.Teacher, UserRole.Student };
    public List<string> UserRoleFilterOptions { get; } = new() { "Все роли", "Администратор", "Преподаватель", "Студент" };

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string AdminCourseSearchQuery
    {
        get => _adminCourseSearchQuery;
        set
        {
            if (SetProperty(ref _adminCourseSearchQuery, value))
                ApplyCourseFilter();
        }
    }

    public string GroupSearchQuery
    {
        get => _groupSearchQuery;
        set
        {
            if (SetProperty(ref _groupSearchQuery, value))
                ApplyGroupFilter();
        }
    }

    public string GroupBindCourseSearchQuery
    {
        get => _groupBindCourseSearchQuery;
        set
        {
            if (SetProperty(ref _groupBindCourseSearchQuery, value))
                ApplyGroupCourseFilter();
        }
    }

    public int GroupRightTabIndex
    {
        get => _groupRightTabIndex;
        set => SetProperty(ref _groupRightTabIndex, value);
    }

    public Course? SelectedCourse
    {
        get => _selectedCourse;
        set
        {
            if (SetProperty(ref _selectedCourse, value))
            {
                if (value is not null)
                {
                    CourseTitle = value.Title;
                    CourseDescription = value.Description;
                    SelectedTeacher = Teachers.FirstOrDefault(t => t.Id == value.TeacherId);
                    CourseHours = value.TotalHours;
                }
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public CourseGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                GroupNameInput = value?.Name ?? string.Empty;
                _ = LoadSelectedGroupDetailsAsync();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Course? SelectedGroupCourse
    {
        get => _selectedGroupCourse;
        set
        {
            if (SetProperty(ref _selectedGroupCourse, value))
                RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public Course? CourseToBind
    {
        get => _courseToBind;
        set
        {
            if (SetProperty(ref _courseToBind, value))
                RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public User? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value))
            {
                if (value is not null)
                {
                    UserLogin = value.Login;
                    UserPassword = string.Empty;
                    UserFullName = value.FullName;
                    UserRole = value.Role;
                    UserGroup = value.StudentGroup ?? string.Empty;
                    UserEmail = value.Email ?? string.Empty;
                    UserIsActive = value.IsActive;
                }
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CourseTitle { get => _courseTitle; set => SetProperty(ref _courseTitle, value); }
    public string CourseDescription { get => _courseDescription; set => SetProperty(ref _courseDescription, value); }

    public User? SelectedTeacher
    {
        get => _selectedTeacher;
        set
        {
            if (SetProperty(ref _selectedTeacher, value))
            {
                var name = value?.FullName ?? string.Empty;
                if (!string.Equals(_teacherSearchText, name, StringComparison.Ordinal))
                {
                    _teacherSearchText = name;
                    OnPropertyChanged(nameof(TeacherSearchText));
                }
            }
        }
    }

    public string TeacherSearchText
    {
        get => _teacherSearchText;
        set
        {
            if (SetProperty(ref _teacherSearchText, value))
            {
                var trimmed = value.Trim();
                var exact = Teachers.FirstOrDefault(t => string.Equals(t.FullName, trimmed, StringComparison.OrdinalIgnoreCase));
                if (!Equals(_selectedTeacher, exact))
                {
                    _selectedTeacher = exact;
                    OnPropertyChanged(nameof(SelectedTeacher));
                }
                UpdateTeacherSuggestions(value);
            }
        }
    }

    public int CourseHours { get => _courseHours; set => SetProperty(ref _courseHours, value); }

    public string GroupNameInput { get => _groupNameInput; set => SetProperty(ref _groupNameInput, value); }

    public string UserSearchQuery
    {
        get => _userSearchQuery;
        set
        {
            if (SetProperty(ref _userSearchQuery, value))
                ApplyUserFilter();
        }
    }

    public string SelectedUserRoleFilter
    {
        get => _selectedUserRoleFilter;
        set
        {
            if (SetProperty(ref _selectedUserRoleFilter, value))
                ApplyUserFilter();
        }
    }

    public string UserLogin { get => _userLogin; set => SetProperty(ref _userLogin, value); }
    public string UserPassword { get => _userPassword; set => SetProperty(ref _userPassword, value); }
    public string UserFullName { get => _userFullName; set => SetProperty(ref _userFullName, value); }

    public UserRole UserRole
    {
        get => _userRole;
        set
        {
            if (SetProperty(ref _userRole, value))
            {
                OnPropertyChanged(nameof(IsStudentRoleSelected));
                if (value != UserRole.Student)
                    UserGroup = string.Empty;
            }
        }
    }

    public bool IsStudentRoleSelected => UserRole == UserRole.Student;

    public string UserGroup
    {
        get => _userGroup;
        set
        {
            if (SetProperty(ref _userGroup, value))
                UpdateGroupSuggestions(value);
        }
    }

    public string UserEmail { get => _userEmail; set => SetProperty(ref _userEmail, value); }
    public bool UserIsActive { get => _userIsActive; set => SetProperty(ref _userIsActive, value); }

    public ICommand CreateCourseCommand { get; }
    public ICommand UpdateCourseCommand { get; }
    public ICommand DeleteCourseCommand { get; }
    public ICommand ClearCourseFormCommand { get; }

    public ICommand CreateGroupCommand { get; }
    public ICommand RenameGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand BindGroupToCourseCommand { get; }
    public ICommand UnbindGroupFromCourseCommand { get; }

    public ICommand CreateUserCommand { get; }
    public ICommand UpdateUserAccessCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand ClearUserFormCommand { get; }

    public ICommand BackupDatabaseCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task LoadDataAsync()
    {
        try
        {
            var prevGroupId = SelectedGroup?.Id;

            var teachers = await _client.GetActiveTeachersAsync();
            var courses = await _client.GetCoursesAsync();
            var groups = await _client.GetAllGroupsAsync();
            var users = await _client.GetAllUsersAsync();

            Teachers.Clear();
            foreach (var t in teachers) Teachers.Add(t);
            UpdateTeacherSuggestions(TeacherSearchText);

            _allCoursesCache = courses;
            ApplyCourseFilter();

            _allGroupsCache = groups;
            ApplyGroupFilter();
            UpdateGroupSuggestions(UserGroup);

            _allUsersCache = users;
            ApplyUserFilter();

            if (SelectedTeacher is null && string.IsNullOrWhiteSpace(TeacherSearchText))
                SelectedTeacher = Teachers.FirstOrDefault();

            if (prevGroupId.HasValue)
                SelectedGroup = Groups.FirstOrDefault(g => g.Id == prevGroupId.Value) ?? Groups.FirstOrDefault();

            await LoadSelectedGroupDetailsAsync();
            await CourseManagementVm.LoadDataAsync();
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    public void ShowAllTeacherSuggestions()
    {
        FilteredTeacherSuggestions.Clear();
        foreach (var t in Teachers)
            FilteredTeacherSuggestions.Add(t);
    }

    public void UpdateTeacherSuggestions(string? query)
    {
        var q = (query ?? string.Empty).Trim();
        var matches = string.IsNullOrEmpty(q)
            ? Teachers.ToList()
            : Teachers.Where(t => t.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                  t.Login.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredTeacherSuggestions.Clear();
        foreach (var m in matches)
            FilteredTeacherSuggestions.Add(m);
    }

    public void SelectTeacher(User teacher)
    {
        SelectedTeacher = teacher;
    }

    public void ShowAllGroupSuggestions()
    {
        FilteredGroupSuggestions.Clear();
        foreach (var g in _allGroupsCache)
            FilteredGroupSuggestions.Add(g.Name);
    }

    public void UpdateGroupSuggestions(string? query)
    {
        var q = (query ?? string.Empty).Trim();
        var matches = string.IsNullOrEmpty(q)
            ? _allGroupsCache.Select(g => g.Name).ToList()
            : _allGroupsCache.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(g => g.Name).ToList();

        FilteredGroupSuggestions.Clear();
        foreach (var m in matches)
            FilteredGroupSuggestions.Add(m);
    }

    private void ApplyGroupFilter()
    {
        var prevGroupId = SelectedGroup?.Id;
        var q = GroupSearchQuery.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _allGroupsCache
            : _allGroupsCache.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        Groups.Clear();
        foreach (var g in filtered)
            Groups.Add(g);

        SelectedGroup = prevGroupId.HasValue
            ? Groups.FirstOrDefault(g => g.Id == prevGroupId.Value) ?? Groups.FirstOrDefault()
            : Groups.FirstOrDefault();
    }

    private void ApplyCourseFilter()
    {
        var prevCourseId = SelectedCourse?.Id;
        var q = AdminCourseSearchQuery.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _allCoursesCache
            : _allCoursesCache.Where(c =>
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.TeacherName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        Courses.Clear();
        foreach (var c in filtered)
            Courses.Add(c);

        SelectedCourse = prevCourseId.HasValue
            ? Courses.FirstOrDefault(c => c.Id == prevCourseId.Value) ?? Courses.FirstOrDefault()
            : Courses.FirstOrDefault();
    }

    private void ApplyUserFilter()
    {
        var prevUserId = SelectedUser?.Id;
        var q = UserSearchQuery.Trim();
        UserRole? roleFilter = SelectedUserRoleFilter switch
        {
            "Администратор" => UserRole.Admin,
            "Преподаватель" => UserRole.Teacher,
            "Студент" => UserRole.Student,
            _ => null
        };

        var filtered = _allUsersCache.Where(u =>
            (!roleFilter.HasValue || u.Role == roleFilter.Value) &&
            (string.IsNullOrEmpty(q) ||
             u.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
             u.Login.Contains(q, StringComparison.OrdinalIgnoreCase) ||
             (u.StudentGroup?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (u.Email?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();

        Users.Clear();
        foreach (var u in filtered)
            Users.Add(u);

        SelectedUser = prevUserId.HasValue
            ? Users.FirstOrDefault(u => u.Id == prevUserId.Value) ?? Users.FirstOrDefault()
            : Users.FirstOrDefault();
    }

    private async Task LoadSelectedGroupDetailsAsync()
    {
        if (SelectedGroup is null)
        {
            GroupStudents.Clear();
            _allGroupCoursesCache.Clear();
            _allAvailableCoursesToBindCache.Clear();
            ApplyGroupCourseFilter();
            return;
        }

        var gid = SelectedGroup.Id;
        try
        {
            var students = await _client.GetGroupStudentsAsync(gid);
            var linkedCourses = await _client.GetGroupCoursesAsync(gid);

            if (SelectedGroup?.Id != gid) return;

            GroupStudents.Clear();
            foreach (var s in students) GroupStudents.Add(s);

            _allGroupCoursesCache = linkedCourses;
            var linkedIds = linkedCourses.Select(c => c.Id).ToHashSet();
            _allAvailableCoursesToBindCache = _allCoursesCache.Where(c => !linkedIds.Contains(c.Id)).ToList();

            ApplyGroupCourseFilter();
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private void ApplyGroupCourseFilter()
    {
        var prevBoundId = SelectedGroupCourse?.Id;
        var prevAvailId = CourseToBind?.Id;
        var q = GroupBindCourseSearchQuery.Trim();

        bool Matches(Course c) =>
            string.IsNullOrEmpty(q) ||
            c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.TeacherName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.Description.Contains(q, StringComparison.OrdinalIgnoreCase);

        GroupCourses.Clear();
        foreach (var c in _allGroupCoursesCache.Where(Matches))
            GroupCourses.Add(c);
        SelectedGroupCourse = prevBoundId.HasValue
            ? GroupCourses.FirstOrDefault(c => c.Id == prevBoundId.Value) ?? GroupCourses.FirstOrDefault()
            : GroupCourses.FirstOrDefault();

        AvailableCoursesToBind.Clear();
        foreach (var c in _allAvailableCoursesToBindCache.Where(Matches))
            AvailableCoursesToBind.Add(c);
        CourseToBind = prevAvailId.HasValue
            ? AvailableCoursesToBind.FirstOrDefault(c => c.Id == prevAvailId.Value) ?? AvailableCoursesToBind.FirstOrDefault()
            : AvailableCoursesToBind.FirstOrDefault();
    }

    private void ClearCourseForm()
    {
        SelectedCourse = null;
        CourseTitle = string.Empty;
        CourseDescription = string.Empty;
        SelectedTeacher = Teachers.FirstOrDefault();
        CourseHours = 36;
    }

    private void ClearUserForm()
    {
        SelectedUser = null;
        UserLogin = string.Empty;
        UserPassword = string.Empty;
        UserFullName = string.Empty;
        UserRole = UserRole.Student;
        UserGroup = string.Empty;
        UserEmail = string.Empty;
        UserIsActive = true;
    }

    private async Task ExecuteCreateCourseAsync()
    {
        try
        {
            if (SelectedTeacher is null)
                throw new InvalidOperationException("Выберите ответственного преподавателя.");
            var created = await _client.CreateCourseAsync(CourseTitle, CourseDescription, SelectedTeacher.Id, CourseHours);
            await LoadDataAsync();
            ClearCourseForm();
            _notify($"Факультатив «{created.Title}» создан (преподаватель: {created.TeacherName}).", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteUpdateCourseAsync()
    {
        if (SelectedCourse is null) return;
        try
        {
            if (SelectedTeacher is null)
                throw new InvalidOperationException("Выберите ответственного преподавателя.");
            await _client.UpdateCourseAsync(SelectedCourse.Id, CourseTitle, CourseDescription, SelectedTeacher.Id, CourseHours);
            await LoadDataAsync();
            _notify("Параметры факультатива обновлены.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteDeleteCourseAsync()
    {
        if (SelectedCourse is null) return;
        try
        {
            var title = SelectedCourse.Title;
            await _client.DeleteCourseAsync(SelectedCourse.Id);
            await LoadDataAsync();
            ClearCourseForm();
            _notify($"Факультатив «{title}» удален.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteCreateGroupAsync()
    {
        try
        {
            var created = await _client.CreateGroupAsync(GroupNameInput);
            await LoadDataAsync();
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == created.Id);
            _notify($"Группа «{created.Name}» создана.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteRenameGroupAsync()
    {
        if (SelectedGroup is null) return;
        try
        {
            var gid = SelectedGroup.Id;
            await _client.RenameGroupAsync(gid, GroupNameInput);
            await LoadDataAsync();
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == gid);
            _notify("Название группы обновлено.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteDeleteGroupAsync()
    {
        if (SelectedGroup is null) return;
        try
        {
            var name = SelectedGroup.Name;
            await _client.DeleteGroupAsync(SelectedGroup.Id);
            await LoadDataAsync();
            _notify($"Группа «{name}» удалена.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteBindGroupToCourseAsync()
    {
        if (SelectedGroup is null || CourseToBind is null) return;
        try
        {
            var title = CourseToBind.Title;
            await _client.BindGroupToCourseAsync(CourseToBind.Id, SelectedGroup.Id);
            await LoadDataAsync();
            _notify($"Группа {SelectedGroup.Name} привязана к факультативу «{title}».", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteUnbindGroupFromCourseAsync()
    {
        if (SelectedGroup is null || SelectedGroupCourse is null) return;
        try
        {
            var title = SelectedGroupCourse.Title;
            await _client.UnbindGroupFromCourseAsync(SelectedGroupCourse.Id, SelectedGroup.Id);
            await LoadDataAsync();
            _notify($"Группа {SelectedGroup.Name} отвязана от факультатива «{title}».", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteCreateUserAsync()
    {
        try
        {
            var created = await _client.CreateUserAsync(UserLogin, UserPassword, UserFullName, UserRole, UserGroup, UserEmail, UserIsActive);
            await LoadDataAsync();
            ClearUserForm();
            _notify($"Пользователь «{created.FullName}» ({created.RoleDisplay}) создан.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteUpdateUserAccessAsync()
    {
        if (SelectedUser is null) return;
        try
        {
            var uid = SelectedUser.Id;
            await _client.UpdateUserAccessAsync(uid, UserRole, UserIsActive, UserFullName, UserGroup, UserEmail, UserPassword);
            await LoadDataAsync();
            SelectedUser = Users.FirstOrDefault(u => u.Id == uid);
            UserPassword = string.Empty;
            _notify("Права доступа и профиль пользователя обновлены.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteDeleteUserAsync()
    {
        if (SelectedUser is null) return;
        try
        {
            var name = SelectedUser.FullName;
            await _client.DeleteUserAsync(SelectedUser.Id, _currentUser.Id);
            await LoadDataAsync();
            ClearUserForm();
            _notify($"Пользователь «{name}» удален.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteBackupAsync()
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Сохранить резервную копию",
                Filter = "Файл резервной копии (*.db)|*.db",
                FileName = $"facultative_backup_{DateTime.Now:yyyyMMdd_HHmm}.db"
            };
            if (dlg.ShowDialog() == true)
            {
                var bytes = await _client.DownloadBackupAsync();
                await File.WriteAllBytesAsync(dlg.FileName, bytes);
                _notify("Резервная копия успешно сохранена.", false);
            }
        }
        catch (Exception ex)
        {
            _notify($"Ошибка резервного копирования: {ex.Message}", true);
        }
    }
}
