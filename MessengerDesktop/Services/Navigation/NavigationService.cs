using MessengerDesktop.Services.Auth;
using MessengerDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.Services.Navigation;

public interface INavigationService
{
    void NavigateToLogin();
    void NavigateToMainMenu();
    void NavigateTo<T>() where T : BaseViewModel;
    event Action<BaseViewModel>? CurrentViewModelChanged;
    BaseViewModel? CurrentViewModel { get; }
}

public class NavigationService(IServiceProvider serviceProvider, IAuthManager authManager) : INavigationService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly IAuthManager _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));

    public BaseViewModel? CurrentViewModel { get; private set; }
    public event Action<BaseViewModel>? CurrentViewModelChanged;

    public void NavigateToLogin()
    {
        CurrentViewModel = _serviceProvider.GetRequiredService<LoginViewModel>();
        CurrentViewModelChanged?.Invoke(CurrentViewModel);
    }
    private readonly Stack<Type> _navigationHistory = new();

    public bool CanGoBack => _navigationHistory.Count > 1;

    public void NavigateTo<T>() where T : BaseViewModel
    {
        _navigationHistory.Push(typeof(T));
        CurrentViewModel = _serviceProvider.GetRequiredService<T>();
        CurrentViewModelChanged?.Invoke(CurrentViewModel);
    }

    public void GoBack()
    {
        if (!CanGoBack) return;

        _navigationHistory.Pop(); 
        var previousType = _navigationHistory.Peek();
        CurrentViewModel = (BaseViewModel)_serviceProvider.GetRequiredService(previousType);
        CurrentViewModelChanged?.Invoke(CurrentViewModel);
    }
    public void NavigateToMainMenu()
    {
        if (!_authManager.Session.IsAuthenticated || !_authManager.Session.UserId.HasValue)
        {
            NavigateToLogin();
            return;
        }

        CurrentViewModel = _serviceProvider.GetRequiredService<MainMenuViewModel>();
        CurrentViewModelChanged?.Invoke(CurrentViewModel);
    }
}