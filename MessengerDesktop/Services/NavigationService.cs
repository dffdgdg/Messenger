using MessengerDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace MessengerDesktop.Services
{
    public interface INavigationService
    {
        void NavigateToLogin();
        void NavigateToMainMenu(int userId);
        void NavigateTo<T>() where T : ViewModelBase;
        event Action<ViewModelBase>? CurrentViewModelChanged;
        ViewModelBase? CurrentViewModel { get; }
    }

    public class NavigationService(IServiceProvider serviceProvider, AuthService authService, HttpClient httpClient) : INavigationService
    {
        public ViewModelBase? CurrentViewModel { get; private set; }
        public event Action<ViewModelBase>? CurrentViewModelChanged;

        public void NavigateToLogin()
        {
            CurrentViewModel = serviceProvider.GetRequiredService<LoginViewModel>();
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
        }

        public void NavigateToMainMenu(int userId)
        {
            var menu = ActivatorUtilities.CreateInstance<MainMenuViewModel>(
                serviceProvider, httpClient, userId);
            CurrentViewModel = menu;
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
        }

        public void NavigateTo<T>() where T : ViewModelBase
        {
            CurrentViewModel = serviceProvider.GetRequiredService<T>();
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
        }
    }
}