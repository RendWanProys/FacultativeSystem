using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Infrastructure;
using FacultativeSystem.ViewModels;

namespace FacultativeSystem.Views;

public partial class TeacherView : UserControl
{
    private readonly List<int> _columnFieldIds = new();
    private TeacherViewModel? _subscribedVm;

    public TeacherView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RebuildMatrixColumns();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.MatrixColumnsChanged -= OnMatrixColumnsChanged;
            _subscribedVm.FocusFieldRequested -= OnFocusFieldRequested;
        }

        _subscribedVm = DataContext as TeacherViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.MatrixColumnsChanged += OnMatrixColumnsChanged;
            _subscribedVm.FocusFieldRequested += OnFocusFieldRequested;
        }

        RebuildMatrixColumns();
    }

    private void OnMatrixColumnsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RebuildMatrixColumns);
    }

    private void OnFocusFieldRequested(object? sender, int fieldId)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var idx = _columnFieldIds.IndexOf(fieldId);
            if (idx < 0 || LessonMatrixGrid.Columns.Count <= idx + 2)
                return;

            var column = LessonMatrixGrid.Columns[idx + 2];
            if (LessonMatrixGrid.Items.Count > 0)
            {
                var firstRow = LessonMatrixGrid.Items[0];
                LessonMatrixGrid.ScrollIntoView(firstRow, column);
                LessonMatrixGrid.CurrentCell = new DataGridCellInfo(firstRow, column);
            }
        });
    }

    private void RebuildMatrixColumns()
    {
        if (LessonMatrixGrid is null || DataContext is not TeacherViewModel vm)
            return;

        LessonMatrixColumnBuilder.RebuildColumns(
            LessonMatrixGrid,
            _columnFieldIds,
            vm.MatrixColumns,
            TryFindResource("BoolToVis") as IValueConverter,
            isEditable: true,
            onInlineGradeChosen: (cell, chosen) => vm.SetCellGradeAsync(cell, chosen, cell.Comment));
    }

    private void LessonMatrixGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (DataContext is TeacherViewModel vm)
            vm.SelectedMatrixCell = LessonMatrixColumnBuilder.ResolveSelectedCell(LessonMatrixGrid, _columnFieldIds);
    }

    private void LessonMatrixGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (DataContext is not TeacherViewModel vm || !vm.CanEditSelectedCourse)
        {
            e.Cancel = true;
            return;
        }

        var colIndex = LessonMatrixGrid.Columns.IndexOf(e.Column);
        if (colIndex < 2 || colIndex - 2 >= _columnFieldIds.Count || e.Row.Item is not LessonMatrixRow row)
        {
            e.Cancel = true;
            return;
        }

        var fieldId = _columnFieldIds[colIndex - 2];
        if (!row.CellsByField.TryGetValue(fieldId, out var cell) || !cell.IsApplicable)
            e.Cancel = true;
    }

    private async void LessonMatrixGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TeacherViewModel vm || !vm.CanEditSelectedCourse || vm.SelectedMatrixCell is not { IsApplicable: true } cell)
            return;

        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBoxItem)
            return;

        string? shortcutGrade = e.Key switch
        {
            Key.D5 or Key.NumPad5 => "5",
            Key.D4 or Key.NumPad4 => "4",
            Key.D3 or Key.NumPad3 => "3",
            Key.D2 or Key.NumPad2 => "2",
            Key.OemMinus or Key.Subtract => "—",
            Key.Delete or Key.Back => GradeOptions.NotSet,
            _ => null
        };

        if (shortcutGrade is null)
            return;

        e.Handled = true;
        await vm.SetCellGradeAsync(cell, shortcutGrade, cell.Comment);
    }
}
