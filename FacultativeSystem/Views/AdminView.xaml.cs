using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FacultativeSystem.Core.Models;
using FacultativeSystem.ViewModels;

namespace FacultativeSystem.Views;

public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
    }

    private void GroupInputBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is AdminViewModel vm && vm.IsStudentRoleSelected)
        {
            if (string.IsNullOrWhiteSpace(GroupInputBox.Text))
                vm.ShowAllGroupSuggestions();
            else
                vm.UpdateGroupSuggestions(GroupInputBox.Text);

            GroupPopup.IsOpen = vm.FilteredGroupSuggestions.Count > 0;
        }
    }

    private void GroupInputBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Tab)
            return;

        if (DataContext is AdminViewModel vm && vm.IsStudentRoleSelected)
        {
            vm.UpdateGroupSuggestions(GroupInputBox.Text);
            GroupPopup.IsOpen = vm.FilteredGroupSuggestions.Count > 0;
        }
    }

    private void GroupInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        => HandlePickerKeyDown(e, GroupPopup, GroupSuggestionsList);

    private void GroupDropdownBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AdminViewModel vm || !vm.IsStudentRoleSelected)
            return;

        TogglePickerDropdown(GroupPopup, () =>
        {
            vm.ShowAllGroupSuggestions();
            return vm.FilteredGroupSuggestions.Count > 0;
        });
    }

    private void GroupSuggestionsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindClickedListItem<string>(e.OriginalSource) is { } chosen)
            CommitGroupSelection(chosen);
    }

    private void GroupSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && GroupSuggestionsList.SelectedItem is string chosen)
        {
            CommitGroupSelection(chosen);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            GroupPopup.IsOpen = false;
            GroupInputBox.Focus();
            e.Handled = true;
        }
    }

    private void CommitGroupSelection(string groupName)
    {
        if (DataContext is AdminViewModel vm)
            vm.UserGroup = groupName;

        GroupPopup.IsOpen = false;
        GroupInputBox.CaretIndex = groupName.Length;
        GroupInputBox.Focus();
    }

    private void TeacherInputBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is AdminViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(TeacherInputBox.Text))
                vm.ShowAllTeacherSuggestions();
            else
                vm.UpdateTeacherSuggestions(TeacherInputBox.Text);

            TeacherPopup.IsOpen = vm.FilteredTeacherSuggestions.Count > 0;
        }
    }

    private void TeacherInputBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Tab)
            return;

        if (DataContext is AdminViewModel vm)
        {
            vm.UpdateTeacherSuggestions(TeacherInputBox.Text);
            TeacherPopup.IsOpen = vm.FilteredTeacherSuggestions.Count > 0;
        }
    }

    private void TeacherInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        => HandlePickerKeyDown(e, TeacherPopup, TeacherSuggestionsList);

    private void TeacherDropdownBtn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AdminViewModel vm)
            return;

        TogglePickerDropdown(TeacherPopup, () =>
        {
            vm.ShowAllTeacherSuggestions();
            return vm.FilteredTeacherSuggestions.Count > 0;
        });
    }

    private void TeacherSuggestionsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindClickedListItem<User>(e.OriginalSource) is { } chosen)
            CommitTeacherSelection(chosen);
    }

    private void TeacherSuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && TeacherSuggestionsList.SelectedItem is User chosen)
        {
            CommitTeacherSelection(chosen);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TeacherPopup.IsOpen = false;
            TeacherInputBox.Focus();
            e.Handled = true;
        }
    }

    private void CommitTeacherSelection(User teacher)
    {
        if (DataContext is AdminViewModel vm)
            vm.SelectTeacher(teacher);

        TeacherPopup.IsOpen = false;
        TeacherInputBox.CaretIndex = teacher.FullName.Length;
        TeacherInputBox.Focus();
    }

    private static void HandlePickerKeyDown(KeyEventArgs e, Popup popup, ListBox listBox)
    {
        if (e.Key == Key.Down && popup.IsOpen && listBox.Items.Count > 0)
        {
            listBox.SelectedIndex = 0;
            listBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            popup.IsOpen = false;
        }
    }

    private static void TogglePickerDropdown(Popup popup, Func<bool> loadItemsAndHasAny)
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        popup.IsOpen = loadItemsAndHasAny();
    }

    private static T? FindClickedListItem<T>(object originalSource) where T : class
    {
        var dep = originalSource as DependencyObject;
        while (dep is not null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        return dep is ListBoxItem item ? item.Content as T : null;
    }
}
