using System.Windows.Media;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Infrastructure;

namespace FacultativeSystem.ViewModels;

/// <summary>
/// Ячейка матрицы «Студент × Пара (дата)» в журнале оценок.
/// </summary>
public class LessonMatrixCell : ViewModelBase
{
    private static readonly Brush NotApplicableBg = FreezeBrush(241, 245, 249);
    private static readonly Brush NotApplicableFg = FreezeBrush(148, 163, 184);
    private static readonly Brush EmptyFg = FreezeBrush(15, 23, 42);
    private static readonly Brush GoodBg = FreezeBrush(236, 253, 245);
    private static readonly Brush GoodFg = FreezeBrush(21, 128, 61);
    private static readonly Brush FourBg = FreezeBrush(239, 246, 255);
    private static readonly Brush FourFg = FreezeBrush(29, 78, 216);
    private static readonly Brush ThreeBg = FreezeBrush(255, 251, 235);
    private static readonly Brush ThreeFg = FreezeBrush(180, 83, 9);
    private static readonly Brush BadBg = FreezeBrush(254, 242, 242);
    private static readonly Brush BadFg = FreezeBrush(185, 28, 28);
    private static readonly Brush AbsentBg = FreezeBrush(245, 243, 255);
    private static readonly Brush AbsentFg = FreezeBrush(109, 40, 217);
    private static readonly Brush DashBg = FreezeBrush(241, 245, 249);
    private static readonly Brush DashFg = FreezeBrush(100, 116, 139);

    private string? _grade;
    private string? _comment;

    public int FieldId { get; init; }
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public string LessonTitle { get; init; } = string.Empty;
    public string LessonType { get; init; } = string.Empty;
    public string LessonDate { get; init; } = string.Empty;
    public string LessonNote { get; init; } = string.Empty;
    public string TargetGroupName { get; init; } = "Все группы";
    public bool IsApplicable { get; init; }

    public string? Grade
    {
        get => _grade;
        set
        {
            if (SetProperty(ref _grade, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(GradeDisplayText));
                OnPropertyChanged(nameof(CellBackground));
                OnPropertyChanged(nameof(CellForeground));
                OnPropertyChanged(nameof(CellToolTip));
            }
        }
    }

    public string? Comment
    {
        get => _comment;
        set
        {
            if (SetProperty(ref _comment, value))
            {
                OnPropertyChanged(nameof(HasComment));
                OnPropertyChanged(nameof(CellToolTip));
            }
        }
    }

    public string DisplayText => !IsApplicable
        ? "—"
        : string.IsNullOrWhiteSpace(Grade)
            ? string.Empty
            : Grade;

    public bool HasComment => IsApplicable && !string.IsNullOrWhiteSpace(Comment);

    public Brush CellBackground
    {
        get
        {
            if (!IsApplicable)
                return NotApplicableBg;

            return Grade switch
            {
                "5" or "Зачёт" => GoodBg,
                "4" => FourBg,
                "3" => ThreeBg,
                "2" or "Незачёт" => BadBg,
                "н/я" => AbsentBg,
                "—" => DashBg,
                _ => Brushes.Transparent
            };
        }
    }

    public Brush CellForeground
    {
        get
        {
            if (!IsApplicable)
                return NotApplicableFg;

            return Grade switch
            {
                "5" or "Зачёт" => GoodFg,
                "4" => FourFg,
                "3" => ThreeFg,
                "2" or "Незачёт" => BadFg,
                "н/я" => AbsentFg,
                "—" => DashFg,
                _ => EmptyFg
            };
        }
    }

    public string GradeDisplayText => !IsApplicable ? "—" : GradeOptions.FormatDisplay(Grade);

    public string CellToolTip
    {
        get
        {
            if (!IsApplicable)
                return $"{LessonDate} • {LessonTitle}\nЗанятие назначено группе: {TargetGroupName}";

            var text = $"{LessonDate} ({LessonType}) — {LessonTitle}\n" +
                       $"Студент: {StudentName} ({GroupName})\n" +
                       $"Оценка: {GradeOptions.FormatDisplay(Grade)}";
            if (!string.IsNullOrWhiteSpace(Comment))
                text += $"\nЗаметка: {Comment}";
            return text;
        }
    }

    private static Brush FreezeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Строка студента в матрице оценок за пары.
/// </summary>
public class LessonMatrixRow : ViewModelBase
{
    public int StudentId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public int? GroupId { get; init; }
    public Dictionary<int, LessonMatrixCell> CellsByField { get; } = new();
}
