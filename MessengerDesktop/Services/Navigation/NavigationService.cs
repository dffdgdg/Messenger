using MessengerDesktop.Services.Auth;
using MessengerDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace MessengerDesktop.Services.Navigation;

public interface INavigationService
{
    void NavigateToLogin();
    void NavigateToMainMenu();
    void NavigateTo<T>() where T : BaseViewModel;
    event Action<BaseViewModel>? CurrentViewModelChanged;
    BaseViewModel? CurrentViewModel { get; }
}

public class NavigationService(IServiceProvider serviceProvider, IAuthService authService) : INavigationService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly IAuthService _authService = authService ?? throw new ArgumentNullException(nameof(authService));

    public BaseViewModel? CurrentViewModel { get; private set; }
    public event Action<BaseViewModel>? CurrentViewModelChanged;

    public void NavigateToLogin()
    {
        CurrentViewModel = _serviceProvider.GetRequiredService<LoginViewModel>();
        CurrentViewModelChanged?.Invoke(CurrentViewModel);
    }

    public void NavigateToMainMenu()
    {
        if (!_authService.IsAuthenticated || !_authService.UserId.HasValue)
        {
            NavigateToLogin();
            return;
        }

        CurrentViewModel = _serviceProvider.GetRequiredService<MainMenuViewModel>();
        CurrentViewModelChanged?.Invoke(CurrentViewModel);
    }

    public void NavigateTo<T>() where T : BaseViewModel
    {
        CurrentViewModel = _serviceProvider.GetRequiredService<T>();
        CurrentViewModelChanged?.Invoke(CurrentViewModel);
    }
}