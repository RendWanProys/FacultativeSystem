using System.Windows.Input;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using FacultativeSystem.Infrastructure;

namespace FacultativeSystem.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IFacultativeClient _client;
    private readonly bool _isDevMode;

    private User? _currentUser;
    private ViewModelBase? _currentViewModel;
    private string? _statusMessage;
    private bool _isStatusError;

    public MainViewModel(IFacultativeClient client, bool isDevMode = true)
    {
        _client = client;
        _isDevMode = isDevMode;
        LogoutCommand = new RelayCommand(ExecuteLogout);
        DismissStatusCommand = new RelayCommand(() => StatusMessage = null);
        ShowLogin();
    }

    public User? CurrentUser
    {
        get => _currentUser;
        private set
        {
            if (SetProperty(ref _currentUser, value))
            {
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(CurrentUserDisplay));
                OnPropertyChanged(nameof(CurrentRoleBadge));
            }
        }
    }

    public bool IsAuthenticated => CurrentUser is not null;
    public string CurrentUserDisplay => CurrentUser is null ? string.Empty : CurrentUser.FullName;
    public string CurrentRoleBadge => CurrentUser is null ? string.Empty : CurrentUser.RoleDisplay;

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        set => SetProperty(ref _isStatusError, value);
    }

    public ICommand LogoutCommand { get; }
    public ICommand DismissStatusCommand { get; }

    public void ShowLogin()
    {
        CurrentUser = null;
        StatusMessage = null;
        CurrentViewModel = new LoginViewModel(_client, OnAuthenticated, _isDevMode);
    }

    public void OnAuthenticated(User user)
    {
        CurrentUser = user;
        StatusMessage = null;
        CurrentViewModel = user.Role switch
        {
            UserRole.Admin => new AdminViewModel(_client, user, Notify),
            UserRole.Teacher => new TeacherViewModel(_client, user, Notify),
            UserRole.Student => new StudentViewModel(_client, user, Notify),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void ExecuteLogout() => ShowLogin();

    private void Notify(string message, bool isError)
    {
        IsStatusError = isError;
        StatusMessage = message;
    }
}
