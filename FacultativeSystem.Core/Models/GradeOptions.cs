namespace FacultativeSystem.Core.Models;

/// <summary>
/// Допустимые значения оценок и зачетов в системе.
/// </summary>
public static class GradeOptions
{
    public const string NotSet = "Не выставлена";

    public static readonly IReadOnlyList<string> All = new[]
    {
        NotSet,
        "5",
        "4",
        "3",
        "2",
        "н/я",
        "—",
        "Зачёт",
        "Незачёт"
    };

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals(NotSet, StringComparison.OrdinalIgnoreCase))
            return null;

        var val = raw.Trim();
        if (val == "-")
            val = "—";

        return val switch
        {
            "5" or "4" or "3" or "2" or "н/я" or "—" or "Зачёт" or "Незачёт" => val,
            _ => throw new ArgumentException($"Недопустимая оценка «{val}». Допустимо: 5, 4, 3, 2, н/я, —, Зачёт, Незачёт или «Не выставлена».")
        };
    }

    public static string FormatDisplay(string? grade) => string.IsNullOrWhiteSpace(grade)
        ? NotSet
        : grade switch
        {
            "5" => "5 (Отлично)",
            "4" => "4 (Хорошо)",
            "3" => "3 (Удовлетв.)",
            "2" => "2 (Неудовлетв.)",
            "н/я" => "н/я",
            "—" => "— (Пропуск)",
            "Зачёт" => "Зачёт",
            "Незачёт" => "Незачёт",
            _ => grade
        };
}
