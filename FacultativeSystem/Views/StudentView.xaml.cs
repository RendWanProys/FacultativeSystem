using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FacultativeSystem.Infrastructure;
using FacultativeSystem.ViewModels;

namespace FacultativeSystem.Views;

public partial class StudentView : UserControl
{
    private readonly List<int> _columnFieldIds = new();
    private StudentViewModel? _subscribedVm;

    public StudentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => RebuildMatrixColumns();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.MatrixColumnsChanged -= OnMatrixColumnsChanged;

        _subscribedVm = DataContext as StudentViewModel;
        if (_subscribedVm is not null)
            _subscribedVm.MatrixColumnsChanged += OnMatrixColumnsChanged;

        RebuildMatrixColumns();
    }

    private void OnMatrixColumnsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RebuildMatrixColumns);
    }

    private void RebuildMatrixColumns()
    {
        if (StudentMatrixGrid is null || DataContext is not StudentViewModel vm)
            return;

        LessonMatrixColumnBuilder.RebuildColumns(
            StudentMatrixGrid,
            _columnFieldIds,
            vm.MatrixColumns,
            TryFindResource("BoolToVis") as IValueConverter,
            isEditable: false);
    }

    private void StudentMatrixGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        if (DataContext is StudentViewModel vm)
            vm.SelectedMatrixCell = LessonMatrixColumnBuilder.ResolveSelectedCell(StudentMatrixGrid, _columnFieldIds);
    }
}
