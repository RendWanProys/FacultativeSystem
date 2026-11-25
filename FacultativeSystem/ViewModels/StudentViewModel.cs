using System.Collections.ObjectModel;
using System.Windows.Input;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using FacultativeSystem.Infrastructure;

namespace FacultativeSystem.ViewModels;

/// <summary>
/// Рабочее пространство актера «Студент»:
/// отображает ТОЛЬКО факультативы, в которые студент добавлен через группу,
/// табличку его оценок по парам (как в Excel) и итоговые зачеты / оценки.
/// </summary>
public class StudentViewModel : ViewModelBase
{
    private readonly IFacultativeClient _client;
    private readonly User _currentUser;
    private readonly Action<string, bool> _notify;

    private string _searchQuery = string.Empty;
    private Course? _selectedCourse;
    private Enrollment? _selectedCourseEnrollment;
    private Enrollment? _selectedEnrollment;
    private LessonMatrixCell? _selectedMatrixCell;
    private int _selectedTabIndex;

    public StudentViewModel(IFacultativeClient client, User currentUser, Action<string, bool> notify)
    {
        _client = client;
        _currentUser = currentUser;
        _notify = notify;

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        _ = LoadDataAsync();
    }

    public event EventHandler? MatrixColumnsChanged;

    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<CourseField> MatrixColumns { get; } = new();
    public ObservableCollection<LessonMatrixRow> StudentMatrixRows { get; } = new();
    public ObservableCollection<Enrollment> MyEnrollments { get; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
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
                UpdateSelectedCourseEnrollment();
                _ = LoadSelectedCourseLessonsAsync();
            }
        }
    }

    public Enrollment? SelectedCourseEnrollment
    {
        get => _selectedCourseEnrollment;
        private set => SetProperty(ref _selectedCourseEnrollment, value);
    }

    public Enrollment? SelectedEnrollment
    {
        get => _selectedEnrollment;
        set => SetProperty(ref _selectedEnrollment, value);
    }

    public LessonMatrixCell? SelectedMatrixCell
    {
        get => _selectedMatrixCell;
        set => SetProperty(ref _selectedMatrixCell, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public bool HasNoCourses => Courses.Count == 0;
    public bool HasMatrixData => MatrixColumns.Count > 0 && StudentMatrixRows.Count > 0;

    public string StudentSummary => $"Обучающийся: {_currentUser.FullName} (группа: {_currentUser.StudentGroup ?? "—"}) • Назначено курсов: {MyEnrollments.Count}";

    public ICommand RefreshCommand { get; }

    public async Task LoadDataAsync()
    {
        try
        {
            var previousCourseId = SelectedCourse?.Id;
            await LoadMyEnrollmentsAsync();
            await LoadCoursesAsync();

            SelectedCourse = previousCourseId.HasValue
                ? Courses.FirstOrDefault(c => c.Id == previousCourseId.Value) ?? Courses.FirstOrDefault()
                : Courses.FirstOrDefault();

            UpdateSelectedCourseEnrollment();
            await LoadSelectedCourseLessonsAsync();
            OnPropertyChanged(nameof(StudentSummary));
            OnPropertyChanged(nameof(HasNoCourses));
        }
        catch (Exception ex)
        {
            _notify(ex.Message, true);
        }
    }

    private async Task LoadCoursesAsync()
    {
        var list = await _client.GetCoursesForStudentAsync(_currentUser.Id, SearchQuery);
        Courses.Clear();
        foreach (var c in list)
            Courses.Add(c);
        OnPropertyChanged(nameof(HasNoCourses));
    }

    private async Task LoadMyEnrollmentsAsync()
    {
        var list = await _client.GetStudentEnrollmentsAsync(_currentUser.Id);
        MyEnrollments.Clear();
        foreach (var e in list)
            MyEnrollments.Add(e);
        SelectedEnrollment = null;
    }

    private async Task LoadSelectedCourseLessonsAsync()
    {
        if (SelectedCourse is null)
        {
            MatrixColumns.Clear();
            StudentMatrixRows.Clear();
            SelectedMatrixCell = null;
            OnPropertyChanged(nameof(HasMatrixData));
            MatrixColumnsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var courseId = SelectedCourse.Id;
        var lessons = await _client.GetStudentLessonGradesAsync(courseId, _currentUser.Id);
        var fields = await _client.GetCourseFieldsAsync(courseId, _currentUser.Id);
        if (SelectedCourse?.Id != courseId) return;

        var noteMap = fields.ToDictionary(f => f.Id, f => f.FieldValue);

        MatrixColumns.Clear();
        foreach (var f in fields)
            MatrixColumns.Add(f);

        StudentMatrixRows.Clear();
        if (fields.Count > 0)
        {
            var groupName = SelectedCourseEnrollment?.CourseGroupName ?? _currentUser.StudentGroup ?? "Без группы";
            var gradeMap = lessons.ToDictionary(l => l.FieldId);
            var row = new LessonMatrixRow
            {
                StudentId = _currentUser.Id,
                StudentName = _currentUser.FullName,
                GroupName = groupName,
                GroupId = SelectedCourseEnrollment?.CourseGroupId
            };

            foreach (var f in fields)
            {
                gradeMap.TryGetValue(f.Id, out var entry);
                noteMap.TryGetValue(f.Id, out var lessonNote);
                row.CellsByField[f.Id] = new LessonMatrixCell
                {
                    FieldId = f.Id,
                    StudentId = _currentUser.Id,
                    StudentName = _currentUser.FullName,
                    GroupName = groupName,
                    LessonTitle = f.FieldName,
                    LessonType = f.FieldType,
                    LessonDate = f.LessonDate,
                    LessonNote = lessonNote ?? string.Empty,
                    TargetGroupName = f.GroupName,
                    IsApplicable = true,
                    Grade = entry?.Grade,
                    Comment = entry?.Comment
                };
            }

            StudentMatrixRows.Add(row);
        }

        SelectedMatrixCell = null;
        OnPropertyChanged(nameof(HasMatrixData));
        MatrixColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectedCourseEnrollment()
    {
        SelectedCourseEnrollment = SelectedCourse is null
            ? null
            : MyEnrollments.FirstOrDefault(e => e.CourseId == SelectedCourse.Id);
    }
}
