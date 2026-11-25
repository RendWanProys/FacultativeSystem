using System.Windows.Input;
using FacultativeSystem.Core.Models;
using FacultativeSystem.Core.Services;
using FacultativeSystem.Infrastructure;

namespace FacultativeSystem.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private readonly IFacultativeClient _client;
    private readonly Action<User> _onLoginSuccess;
    private readonly bool _isDevMode;

    private string _login = string.Empty;
    private string _password = string.Empty;
    private string? _errorMessage;

    public LoginViewModel(IFacultativeClient client, Action<User> onLoginSuccess, bool isDevMode = true)
    {
        _client = client;
        _onLoginSuccess = onLoginSuccess;
        _isDevMode = isDevMode;

        SubmitCommand = new AsyncRelayCommand(ExecuteSubmitAsync);
        QuickDemoAdminCommand = new AsyncRelayCommand(() => QuickLoginAsync("admin", "admin123"), () => _isDevMode);
        QuickDemoTeacherCommand = new AsyncRelayCommand(() => QuickLoginAsync("paramonov", "123456"), () => _isDevMode);
        QuickDemoStudentCommand = new AsyncRelayCommand(() => QuickLoginAsync("gabitov", "123456"), () => _isDevMode);
    }

    public bool IsDevMode => _isDevMode;
    public string Login { get => _login; set => SetProperty(ref _login, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

    public ICommand SubmitCommand { get; }
    public ICommand QuickDemoAdminCommand { get; }
    public ICommand QuickDemoTeacherCommand { get; }
    public ICommand QuickDemoStudentCommand { get; }

    private async Task QuickLoginAsync(string login, string pwd)
    {
        try
        {
            ErrorMessage = null;
            var user = await _client.AuthenticateAsync(login, pwd);
            if (user is null)
            {
                ErrorMessage = "Демо-пользователь не найден или пароль изменен.";
                return;
            }
            _onLoginSuccess(user);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task ExecuteSubmitAsync()
    {
        try
        {
            ErrorMessage = null;
            var user = await _client.AuthenticateAsync(Login, Password);
            if (user is null)
            {
                ErrorMessage = "Неверный логин или пароль.";
                return;
            }
            _onLoginSuccess(user);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
