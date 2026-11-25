using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FacultativeSystem.Core.Models;
using FacultativeSystem.ViewModels;

namespace FacultativeSystem.Infrastructure;

/// <summary>
/// Построитель динамических столбцов дат и ячеек для Excel-матрицы оценок (общий для преподавателя, администратора и студента).
/// </summary>
public static class LessonMatrixColumnBuilder
{
    public static void RebuildColumns(
        DataGrid grid,
        List<int> columnFieldIds,
        IEnumerable<CourseField> fields,
        IValueConverter? boolToVis,
        bool isEditable,
        Func<LessonMatrixCell, string, Task>? onInlineGradeChosen = null)
    {
        while (grid.Columns.Count > 2)
            grid.Columns.RemoveAt(grid.Columns.Count - 1);
        columnFieldIds.Clear();

        foreach (var field in fields)
        {
            var col = new DataGridTemplateColumn
            {
                Header = BuildLessonHeader(field),
                CanUserReorder = false,
                CanUserResize = true,
                CanUserSort = false,
                IsReadOnly = !isEditable,
                Width = new DataGridLength(116),
                MinWidth = 86,
                CellTemplate = BuildCellDisplayTemplate(field.Id, boolToVis),
                CellEditingTemplate = isEditable ? BuildCellEditingTemplate(grid, field.Id, onInlineGradeChosen) : null
            };

            columnFieldIds.Add(field.Id);
            grid.Columns.Add(col);
        }
    }

    public static LessonMatrixCell? ResolveSelectedCell(DataGrid grid, IReadOnlyList<int> columnFieldIds)
    {
        var cellInfo = grid.CurrentCell;
        if (!cellInfo.IsValid || cellInfo.Column is null)
            return null;

        var colIndex = grid.Columns.IndexOf(cellInfo.Column);
        if (colIndex < 2 || colIndex - 2 >= columnFieldIds.Count || cellInfo.Item is not LessonMatrixRow row)
            return null;

        var fieldId = columnFieldIds[colIndex - 2];
        return row.CellsByField.TryGetValue(fieldId, out var cell) ? cell : null;
    }

    private static FrameworkElement BuildLessonHeader(CourseField field)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = $"{field.LessonDate} • {field.FieldType}\n{field.FieldName}" +
                      (string.IsNullOrWhiteSpace(field.FieldValue) ? string.Empty : $"\nЗаметка: {field.FieldValue}")
        };

        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(field.LessonDate) ? $"Пара {field.SortOrder}" : field.LessonDate,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = field.FieldName,
            FontWeight = FontWeights.Normal,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            MaxWidth = 104,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });

        return panel;
    }

    private static DataTemplate BuildCellDisplayTemplate(int fieldId, IValueConverter? boolToVis)
    {
        var pathPrefix = $"CellsByField[{fieldId}]";

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetBinding(Border.BackgroundProperty, new Binding($"{pathPrefix}.CellBackground"));
        borderFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding($"{pathPrefix}.CellToolTip"));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));

        var gridFactory = new FrameworkElementFactory(typeof(Grid));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding($"{pathPrefix}.DisplayText"));
        textFactory.SetBinding(TextBlock.ForegroundProperty, new Binding($"{pathPrefix}.CellForeground"));
        textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        textFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
        textFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        textFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        gridFactory.AppendChild(textFactory);

        if (boolToVis is not null)
        {
            var noteDotFactory = new FrameworkElementFactory(typeof(TextBlock));
            noteDotFactory.SetValue(TextBlock.TextProperty, "●");
            noteDotFactory.SetValue(TextBlock.FontSizeProperty, 7.0);
            noteDotFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(37, 99, 235)));
            noteDotFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            noteDotFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            noteDotFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 2, 0));
            noteDotFactory.SetBinding(UIElement.VisibilityProperty, new Binding($"{pathPrefix}.HasComment")
            {
                Converter = boolToVis
            });
            gridFactory.AppendChild(noteDotFactory);
        }

        borderFactory.AppendChild(gridFactory);
        return new DataTemplate { VisualTree = borderFactory };
    }

    private static DataTemplate BuildCellEditingTemplate(
        DataGrid grid,
        int fieldId,
        Func<LessonMatrixCell, string, Task>? onInlineGradeChosen)
    {
        var comboFactory = new FrameworkElementFactory(typeof(ComboBox));
        comboFactory.SetValue(ItemsControl.ItemsSourceProperty, GradeOptions.All);
        comboFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        comboFactory.SetValue(Control.FontSizeProperty, 12.5);
        comboFactory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is not ComboBox combo) return;
            if (combo.DataContext is LessonMatrixRow row && row.CellsByField.TryGetValue(fieldId, out var cell))
            {
                combo.Tag = null;
                combo.SelectedItem = cell.Grade ?? GradeOptions.NotSet;
                combo.Tag = cell;
                combo.IsDropDownOpen = true;
            }
        }));
        comboFactory.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler(async (s, _) =>
        {
            if (s is not ComboBox combo || combo.Tag is not LessonMatrixCell cell)
                return;
            if (combo.SelectedItem is not string chosen)
                return;

            var currentDisplay = cell.Grade ?? GradeOptions.NotSet;
            if (!string.Equals(chosen, currentDisplay, StringComparison.Ordinal) && onInlineGradeChosen is not null)
                await onInlineGradeChosen(cell, chosen);

            grid.CommitEdit(DataGridEditingUnit.Cell, true);
        }));

        return new DataTemplate { VisualTree = comboFactory };
    }
}
