using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FacultativeSystem.Core.Models;

namespace FacultativeSystem.Views;

public partial class AddGroupDialog : Window
{
    private readonly List<CourseGroup> _allAvailableGroups;

    public AddGroupDialog(string courseTitle, IEnumerable<CourseGroup> availableGroups)
    {
        InitializeComponent();
        _allAvailableGroups = availableGroups.ToList();
        CourseSubtitleText.Text = $"Факультатив: {courseTitle}";
        ApplyFilter(string.Empty);
    }

    public CourseGroup? SelectedGroup { get; private set; }

    private void ApplyFilter(string query)
    {
        var q = query.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _allAvailableGroups
            : _allAvailableGroups.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        GroupsGrid.ItemsSource = filtered;
        if (filtered.Count > 0)
        {
            GroupsGrid.SelectedIndex = 0;
            EmptyText.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyText.Visibility = Visibility.Visible;
        }
        ConfirmButton.IsEnabled = GroupsGrid.SelectedItem is CourseGroup;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(SearchBox.Text);

    private void GroupsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = GroupsGrid.SelectedItem is CourseGroup;
    }

    private void GroupsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupsGrid.SelectedItem is CourseGroup group)
        {
            SelectedGroup = group;
            DialogResult = true;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsGrid.SelectedItem is CourseGroup group)
        {
            SelectedGroup = group;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
