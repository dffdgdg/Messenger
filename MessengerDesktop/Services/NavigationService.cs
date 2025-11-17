using MessengerDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace MessengerDesktop.Services
{
    public interface INavigationService
    {
        void NavigateToLogin();
        void NavigateToMainMenu();
        void NavigateTo<T>() where T : BaseViewModel;
        event Action<BaseViewModel>? CurrentViewModelChanged;
        BaseViewModel? CurrentViewModel { get; }
    }

    public class NavigationService(IServiceProvider serviceProvider, AuthService authService) : INavigationService
    {
        public BaseViewModel? CurrentViewModel { get; private set; }
        public event Action<BaseViewModel>? CurrentViewModelChanged;

        public void NavigateToLogin()
        {
            CurrentViewModel = serviceProvider.GetRequiredService<LoginViewModel>();
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
        }

        public void NavigateToMainMenu()
        {
            if (!authService.IsAuthenticated || !authService.UserId.HasValue)
            {
                NavigateToLogin();
                return;
            }

            CurrentViewModel = serviceProvider.GetRequiredService<MainMenuViewModel>();
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
        }

        public void NavigateTo<T>() where T : BaseViewModel
        {
            CurrentViewModel = serviceProvider.GetRequiredService<T>();
            CurrentViewModelChanged?.Invoke(CurrentViewModel);
        }
    }
}