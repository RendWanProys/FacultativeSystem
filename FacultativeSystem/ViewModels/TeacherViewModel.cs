using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using FacultativeSystem.Infrastructure;
using FacultativeSystem.Views;

namespace FacultativeSystem.ViewModels;

/// <summary>
/// Рабочее пространство управления факультативом (единообразное для Преподавателя и Администратора):
/// 1) Группы на факультативе (студенты попадают на курс только через группы) и итоговые зачёты;
/// 2) Поля факультета (пары / занятия с привязкой к группам);
/// 3) Оценки за конкретные пары.
/// </summary>
public class TeacherViewModel : ViewModelBase
{
    private readonly IFacultativeClient _client;
    private readonly User _currentUser;
    private readonly Action<string, bool> _notify;
    private readonly bool _isAdminOverride;

    private string _courseSearchQuery = string.Empty;
    private Course? _selectedCourse;
    private CourseGroup? _selectedCourseGroup;
    private CourseField? _selectedField;
    private Enrollment? _selectedEnrollment;
    private LessonMatrixCell? _selectedMatrixCell;
    private bool _showAllCoursesReadOnly;
    private int _selectedTabIndex;
    private bool _suppressGroupScopeRefresh;

    private List<CourseField> _allCourseFieldsCache = new();
    private List<Enrollment> _allCourseEnrollmentsCache = new();
    private List<LessonGradeEntry> _allCourseLessonGradesCache = new();

    // Форма пары (поля факультета)
    private string _fieldName = string.Empty;
    private string _fieldValue = string.Empty;
    private string _fieldType = "Лекция";
    private string _lessonDate = DateTime.Now.ToString("dd.MM.yyyy 15:30");
    private int _fieldOrder = 1;

    // Форма итоговой оценки / зачёта по предмету
    private string _finalGradeValue = GradeOptions.NotSet;
    private string _finalCommentText = string.Empty;

    // Заметка к оценке за конкретную пару
    private string _lessonCommentText = string.Empty;

    public TeacherViewModel(IFacultativeClient client, User currentUser, Action<string, bool> notify, bool isAdminOverride = false, bool autoLoad = true)
    {
        _client = client;
        _currentUser = currentUser;
        _notify = notify;
        _isAdminOverride = isAdminOverride;

        AddGroupCommand = new AsyncRelayCommand(ExecuteAddGroupDialogAsync, () => CanEditSelectedCourse);
        RemoveGroupCommand = new AsyncRelayCommand(ExecuteRemoveGroupFromCourseAsync, () => CanEditSelectedCourse && SelectedCourseGroup is not null);

        AddFieldCommand = new AsyncRelayCommand(ExecuteAddFieldAsync, () => CanEditSelectedCourse && SelectedCourseGroup is not null);
        UpdateFieldCommand = new AsyncRelayCommand(ExecuteUpdateFieldAsync, () => CanEditSelectedCourse && SelectedField is not null);
        DeleteFieldCommand = new AsyncRelayCommand(ExecuteDeleteFieldAsync, () => CanEditSelectedCourse && SelectedField is not null);
        ClearFieldFormCommand = new RelayCommand(ClearFieldForm);
        GoToLessonGradesCommand = new RelayCommand(ExecuteGoToLessonGrades, () => SelectedField is not null);

        SaveGradeCommand = new AsyncRelayCommand(ExecuteSaveFinalGradeAsync, () => CanEditSelectedCourse && SelectedEnrollment is not null);
        SaveLessonGradeCommand = new AsyncRelayCommand(ExecuteSaveLessonGradeAsync, () => CanEditSelectedCourse && SelectedMatrixCell is { IsApplicable: true });
        QuickSetLessonGradeCommand = new AsyncRelayCommand(ExecuteQuickSetLessonGradeAsync, param => CanEditSelectedCourse && SelectedMatrixCell is { IsApplicable: true });
        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);

        if (autoLoad)
            _ = LoadDataAsync();
    }

    public event EventHandler? MatrixColumnsChanged;
    public event EventHandler<int>? FocusFieldRequested;

    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<CourseGroup> AllGroups { get; } = new();
    public ObservableCollection<CourseGroup> CourseGroups { get; } = new();
    public ObservableCollection<CourseField> CourseFields { get; } = new();
    public ObservableCollection<Enrollment> Enrollments { get; } = new();
    public ObservableCollection<CourseField> MatrixColumns { get; } = new();
    public ObservableCollection<LessonMatrixRow> LessonMatrixRows { get; } = new();

    public List<string> FieldTypes { get; } = new()
    {
        "Лекция",
        "Практика",
        "Лабораторная",
        "Семинар",
        "Зачёт"
    };

    public IReadOnlyList<string> GradeChoices => GradeOptions.All;

    public bool IsAdminOverride => _isAdminOverride;
    public Thickness RootMargin => _isAdminOverride ? new Thickness(0, 10, 0, 0) : new Thickness(16);

    public string CourseSearchQuery
    {
        get => _courseSearchQuery;
        set
        {
            if (SetProperty(ref _courseSearchQuery, value))
                _ = LoadCoursesAsync();
        }
    }

    public bool ShowAllCoursesReadOnly
    {
        get => _showAllCoursesReadOnly;
        set
        {
            if (SetProperty(ref _showAllCoursesReadOnly, value))
                _ = LoadCoursesAsync();
        }
    }

    public Course? SelectedCourse
    {
        get => _selectedCourse;
        set
        {
            if (SetProperty(ref _selectedCourse, value))
            {
                _ = LoadCourseDetailsAsync();
                OnPropertyChanged(nameof(CanEditSelectedCourse));
                OnPropertyChanged(nameof(SelectedCourseHeader));
                OnPropertyChanged(nameof(SelectedCourseTeacherDisplay));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public CourseGroup? SelectedCourseGroup
    {
        get => _selectedCourseGroup;
        set
        {
            if (SetProperty(ref _selectedCourseGroup, value))
            {
                if (!_suppressGroupScopeRefresh)
                    ApplySelectedGroupScope();
                OnPropertyChanged(nameof(BoundGroupsSummary));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanEditSelectedCourse =>
        SelectedCourse is not null && (_isAdminOverride || SelectedCourse.TeacherId == _currentUser.Id);

    public string SelectedCourseHeader => SelectedCourse is null
        ? "Выберите факультатив слева"
        : $"{SelectedCourse.Title} • Преподаватель: {SelectedCourse.TeacherName}" +
          (CanEditSelectedCourse ? "" : " (только просмотр)");

    public string BoundGroupsSummary => CourseGroups.Count == 0
        ? "Группы не привязаны (привязка выполняется администратором во вкладке «Группы»)"
        : $"Привязанные группы: {string.Join(", ", CourseGroups.Select(g => g.Name))}";

    public string SelectedCourseTeacherDisplay => SelectedCourse is null
        ? string.Empty
        : $"Преподаватель: {SelectedCourse.TeacherName}";

    public CourseField? SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetProperty(ref _selectedField, value))
            {
                if (value is not null)
                {
                    FieldName = value.FieldName;
                    FieldValue = value.FieldValue;
                    FieldType = value.FieldType;
                    LessonDate = value.LessonDate;
                    FieldOrder = value.SortOrder;
                }
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Enrollment? SelectedEnrollment
    {
        get => _selectedEnrollment;
        set
        {
            if (SetProperty(ref _selectedEnrollment, value))
            {
                if (value is not null)
                {
                    FinalGradeValue = value.Grade ?? GradeOptions.NotSet;
                    FinalCommentText = value.Comment ?? string.Empty;
                }
                else
                {
                    FinalGradeValue = GradeOptions.NotSet;
                    FinalCommentText = string.Empty;
                }
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public LessonMatrixCell? SelectedMatrixCell
    {
        get => _selectedMatrixCell;
        set
        {
            var normalized = value is { IsApplicable: true } ? value : null;
            if (SetProperty(ref _selectedMatrixCell, normalized))
            {
                LessonCommentText = normalized?.Comment ?? string.Empty;
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasMatrixData => MatrixColumns.Count > 0 && LessonMatrixRows.Count > 0;
    public string MatrixEmptyMessage => CourseGroups.Count == 0 || SelectedCourseGroup is null
        ? "К этому факультативу пока не привязаны учебные группы (привязка выполняется во вкладке «Группы»)."
        : Enrollments.Count == 0
            ? $"В группе {SelectedCourseGroup.Name} пока нет студентов."
            : $"Для группы {SelectedCourseGroup.Name} пока не добавлено ни одной пары (вкладка «Поля факультета»).";

    public string FieldName { get => _fieldName; set => SetProperty(ref _fieldName, value); }
    public string FieldValue { get => _fieldValue; set => SetProperty(ref _fieldValue, value); }
    public string FieldType { get => _fieldType; set => SetProperty(ref _fieldType, value); }
    public string LessonDate { get => _lessonDate; set => SetProperty(ref _lessonDate, value); }
    public int FieldOrder { get => _fieldOrder; set => SetProperty(ref _fieldOrder, value); }

    public string FinalGradeValue { get => _finalGradeValue; set => SetProperty(ref _finalGradeValue, value); }
    public string FinalCommentText { get => _finalCommentText; set => SetProperty(ref _finalCommentText, value); }

    public string LessonCommentText { get => _lessonCommentText; set => SetProperty(ref _lessonCommentText, value); }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public ICommand AddGroupCommand { get; }
    public ICommand RemoveGroupCommand { get; }
    public ICommand AddFieldCommand { get; }
    public ICommand UpdateFieldCommand { get; }
    public ICommand DeleteFieldCommand { get; }
    public ICommand ClearFieldFormCommand { get; }
    public ICommand GoToLessonGradesCommand { get; }
    public ICommand SaveGradeCommand { get; }
    public ICommand SaveLessonGradeCommand { get; }
    public ICommand QuickSetLessonGradeCommand { get; }
    public ICommand RefreshCommand { get; }

    public Task LoadDataAsync() => LoadCoursesAsync();

    private async Task LoadCoursesAsync()
    {
        try
        {
            var prevId = SelectedCourse?.Id;
            var items = (_isAdminOverride || ShowAllCoursesReadOnly)
                ? await _client.GetCoursesAsync(search: CourseSearchQuery)
                : await _client.GetCoursesAsync(teacherId: _currentUser.Id, search: CourseSearchQuery);

            Courses.Clear();
            foreach (var c in items)
                Courses.Add(c);

            SelectedCourse = prevId.HasValue
                ? Courses.FirstOrDefault(c => c.Id == prevId.Value) ?? Courses.FirstOrDefault()
                : Courses.FirstOrDefault();

            await LoadCourseDetailsAsync();
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task LoadCourseDetailsAsync(int? preferredGroupId = null)
    {
        if (SelectedCourse is null)
        {
            _suppressGroupScopeRefresh = true;
            try
            {
                AllGroups.Clear();
                CourseGroups.Clear();
                _allCourseFieldsCache.Clear();
                _allCourseEnrollmentsCache.Clear();
                _allCourseLessonGradesCache.Clear();
                SelectedCourseGroup = null;
            }
            finally
            {
                _suppressGroupScopeRefresh = false;
            }

            ApplySelectedGroupScope();
            return;
        }

        var courseId = SelectedCourse.Id;
        var targetGroupId = preferredGroupId ?? SelectedCourseGroup?.Id;

        try
        {
            var allGroups = await _client.GetAllGroupsAsync();
            var courseGroups = await _client.GetCourseGroupsAsync(courseId);
            var fields = await _client.GetCourseFieldsAsync(courseId);
            var enrollments = await _client.GetCourseEnrollmentsAsync(courseId);
            var lessonGrades = await _client.GetLessonGradesForCourseAsync(courseId);

            if (SelectedCourse?.Id != courseId) return;

            _suppressGroupScopeRefresh = true;
            try
            {
                AllGroups.Clear();
                foreach (var g in allGroups) AllGroups.Add(g);

                CourseGroups.Clear();
                foreach (var g in courseGroups)
                    CourseGroups.Add(g);

                _allCourseFieldsCache = fields;
                _allCourseEnrollmentsCache = enrollments;
                _allCourseLessonGradesCache = lessonGrades;

                SelectedCourseGroup = targetGroupId.HasValue
                    ? CourseGroups.FirstOrDefault(g => g.Id == targetGroupId.Value) ?? CourseGroups.FirstOrDefault()
                    : CourseGroups.FirstOrDefault();
            }
            finally
            {
                _suppressGroupScopeRefresh = false;
            }

            OnPropertyChanged(nameof(BoundGroupsSummary));
            ApplySelectedGroupScope();
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    public Task LoadLessonMatrixAsync() => LoadCourseDetailsAsync();

    private void ApplySelectedGroupScope()
    {
        var prevFieldId = SelectedMatrixCell?.FieldId;
        var prevStudentId = SelectedMatrixCell?.StudentId;
        var gid = SelectedCourseGroup?.Id;

        CourseFields.Clear();
        if (gid.HasValue)
        {
            foreach (var f in _allCourseFieldsCache.Where(f => f.GroupId == gid.Value))
                CourseFields.Add(f);
        }

        ClearFieldForm();
        FieldOrder = CourseFields.Count + 1;

        Enrollments.Clear();
        if (gid.HasValue)
        {
            foreach (var e in _allCourseEnrollmentsCache
                         .Where(e => e.CourseGroupId == gid.Value)
                         .OrderBy(e => e.StudentName, StringComparer.CurrentCultureIgnoreCase))
            {
                Enrollments.Add(e);
            }
        }
        SelectedEnrollment = null;

        MatrixColumns.Clear();
        foreach (var f in CourseFields)
            MatrixColumns.Add(f);

        var entryMap = _allCourseLessonGradesCache.ToDictionary(x => (x.FieldId, x.StudentId));
        LessonMatrixRows.Clear();

        foreach (var e in Enrollments)
        {
            var row = new LessonMatrixRow
            {
                StudentId = e.StudentId,
                StudentName = e.StudentName,
                GroupName = e.CourseGroupName,
                GroupId = e.CourseGroupId
            };

            foreach (var f in MatrixColumns)
            {
                entryMap.TryGetValue((f.Id, e.StudentId), out var existing);
                var cell = new LessonMatrixCell
                {
                    FieldId = f.Id,
                    StudentId = e.StudentId,
                    StudentName = e.StudentName,
                    GroupName = e.CourseGroupName,
                    LessonTitle = f.FieldName,
                    LessonType = f.FieldType,
                    LessonDate = f.LessonDate,
                    TargetGroupName = SelectedCourseGroup?.Name ?? f.GroupName,
                    IsApplicable = true,
                    Grade = existing?.Grade,
                    Comment = existing?.Comment
                };
                row.CellsByField[f.Id] = cell;
            }

            LessonMatrixRows.Add(row);
        }

        SelectedMatrixCell = (prevFieldId.HasValue && prevStudentId.HasValue)
            ? LessonMatrixRows.FirstOrDefault(r => r.StudentId == prevStudentId.Value) is { } rMatch &&
              rMatch.CellsByField.TryGetValue(prevFieldId.Value, out var cMatch)
                ? cMatch
                : null
            : null;

        OnPropertyChanged(nameof(HasMatrixData));
        OnPropertyChanged(nameof(MatrixEmptyMessage));
        MatrixColumnsChanged?.Invoke(this, EventArgs.Empty);
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void ClearFieldForm()
    {
        SelectedField = null;
        FieldName = string.Empty;
        FieldValue = string.Empty;
        FieldType = "Лекция";
        LessonDate = DateTime.Now.ToString("dd.MM.yyyy 15:30");
        FieldOrder = CourseFields.Count + 1;
    }

    private void ExecuteGoToLessonGrades()
    {
        if (SelectedField is null) return;
        var fieldId = SelectedField.Id;
        SelectedTabIndex = 2;
        FocusFieldRequested?.Invoke(this, fieldId);
    }

    private async Task ExecuteAddGroupDialogAsync()
    {
        if (SelectedCourse is null) return;
        try
        {
            var boundIds = CourseGroups.Select(g => g.Id).ToHashSet();
            var available = AllGroups.Where(g => !boundIds.Contains(g.Id)).ToList();

            var dlg = new AddGroupDialog(SelectedCourse.Title, available);
            if (Application.Current?.MainWindow is { IsVisible: true } owner && owner != dlg)
                dlg.Owner = owner;

            if (dlg.ShowDialog() == true && dlg.SelectedGroup is { } group)
            {
                await _client.BindGroupToCourseAsync(SelectedCourse.Id, group.Id);
                await LoadCourseDetailsAsync(group.Id);
                await RefreshCourseListCountsOnlyAsync();
                _notify($"Группа {group.Name} добавлена на факультатив.", false);
            }
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteRemoveGroupFromCourseAsync()
    {
        if (SelectedCourse is null || SelectedCourseGroup is null) return;
        try
        {
            var name = SelectedCourseGroup.Name;
            await _client.UnbindGroupFromCourseAsync(SelectedCourse.Id, SelectedCourseGroup.Id);
            await LoadCourseDetailsAsync();
            await RefreshCourseListCountsOnlyAsync();
            _notify($"Группа {name} убрана с факультатива.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteAddFieldAsync()
    {
        if (SelectedCourse is null || SelectedCourseGroup is null) return;
        try
        {
            var gid = SelectedCourseGroup.Id;
            await _client.AddCourseFieldAsync(SelectedCourse.Id, FieldName, FieldValue, FieldType, LessonDate, gid, FieldOrder);
            await LoadCourseDetailsAsync(gid);
            await RefreshCourseListCountsOnlyAsync();
            ClearFieldForm();
            _notify($"Пара добавлена для группы {SelectedCourseGroup.Name}.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteUpdateFieldAsync()
    {
        if (SelectedField is null) return;
        try
        {
            var gid = SelectedCourseGroup?.Id ?? SelectedField.GroupId;
            await _client.UpdateCourseFieldAsync(SelectedField.Id, FieldName, FieldValue, FieldType, LessonDate, gid, FieldOrder);
            await LoadCourseDetailsAsync(gid);
            _notify("Пара (поле факультатива) обновлена.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task ExecuteDeleteFieldAsync()
    {
        if (SelectedField is null) return;
        try
        {
            var name = SelectedField.FieldName;
            var gid = SelectedCourseGroup?.Id;
            await _client.DeleteCourseFieldAsync(SelectedField.Id);
            await LoadCourseDetailsAsync(gid);
            await RefreshCourseListCountsOnlyAsync();
            ClearFieldForm();
            _notify($"Пара «{name}» удалена.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task RefreshCourseListCountsOnlyAsync()
    {
        if (SelectedCourse is null) return;
        var currentCourseId = SelectedCourse.Id;
        var items = (_isAdminOverride || ShowAllCoursesReadOnly)
            ? await _client.GetCoursesAsync(search: CourseSearchQuery)
            : await _client.GetCoursesAsync(teacherId: _currentUser.Id, search: CourseSearchQuery);

        var updated = items.FirstOrDefault(c => c.Id == currentCourseId);
        if (updated is null) return;

        var idx = Courses.ToList().FindIndex(c => c.Id == currentCourseId);
        if (idx >= 0)
        {
            _suppressGroupScopeRefresh = true;
            try
            {
                Courses[idx] = updated;
                _selectedCourse = updated;
                OnPropertyChanged(nameof(SelectedCourse));
            }
            finally
            {
                _suppressGroupScopeRefresh = false;
            }
        }
    }

    private async Task ExecuteSaveFinalGradeAsync()
    {
        if (SelectedEnrollment is null) return;
        try
        {
            var eid = SelectedEnrollment.Id;
            var gid = SelectedCourseGroup?.Id;
            await _client.SetEnrollmentGradeAsync(eid, FinalGradeValue, FinalCommentText);
            await LoadCourseDetailsAsync(gid);
            SelectedEnrollment = Enrollments.FirstOrDefault(e => e.Id == eid);
            _notify($"Итоговая оценка ({FinalGradeValue}) для студента {SelectedEnrollment?.StudentName} сохранена.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private Task ExecuteSaveLessonGradeAsync()
    {
        if (SelectedMatrixCell is null) return Task.CompletedTask;
        return SetCellGradeAsync(SelectedMatrixCell, SelectedMatrixCell.Grade ?? GradeOptions.NotSet, LessonCommentText);
    }

    private Task ExecuteQuickSetLessonGradeAsync(object? param)
    {
        if (SelectedMatrixCell is null || param is not string gradeRaw)
            return Task.CompletedTask;

        return SetCellGradeAsync(SelectedMatrixCell, gradeRaw, LessonCommentText);
    }

    public async Task SetCellGradeAsync(LessonMatrixCell cell, string? gradeRaw, string? comment)
    {
        if (!CanEditSelectedCourse || !cell.IsApplicable)
            return;

        try
        {
            var normalized = GradeOptions.Normalize(gradeRaw);
            var cleanComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            await _client.SetLessonGradeAsync(cell.FieldId, cell.StudentId, gradeRaw, cleanComment);

            cell.Grade = normalized;
            cell.Comment = cleanComment;

            var cached = _allCourseLessonGradesCache.FirstOrDefault(x => x.FieldId == cell.FieldId && x.StudentId == cell.StudentId);
            if (cached is not null)
            {
                cached.Grade = normalized;
                cached.Comment = cleanComment;
            }
            else
            {
                _allCourseLessonGradesCache.Add(new LessonGradeEntry
                {
                    FieldId = cell.FieldId,
                    StudentId = cell.StudentId,
                    Grade = normalized,
                    Comment = cleanComment
                });
            }

            if (ReferenceEquals(SelectedMatrixCell, cell))
                LessonCommentText = cleanComment ?? string.Empty;

            _notify($"Оценка за {cell.LessonDate} ({cell.StudentName}): {GradeOptions.FormatDisplay(normalized)}.", false);
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }
}
