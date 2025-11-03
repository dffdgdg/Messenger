using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly INavigationService _navigation;

        [ObservableProperty]
        private ViewModelBase? currentViewModel;

        public MainWindowViewModel(INavigationService navigation)
        {
            _navigation = navigation;
            _navigation.CurrentViewModelChanged += vm => CurrentViewModel = vm;
            _navigation.NavigateToLogin();
        }

        [RelayCommand]
        public static void ToggleTheme() => App.Current.ToggleTheme();

        [RelayCommand]
        public async Task Logout()
        {
            try
            {
                var authService = App.Current.Services.GetRequiredService<AuthService>();
                authService.ClearAuth();
                _navigation.NavigateToLogin();
            }
            catch (Exception ex)
            {
               await NotificationService.ShowError($"Ошибка при выходе: {ex.Message}");
            }
        }
    }
}