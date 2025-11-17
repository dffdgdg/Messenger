using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.ViewModels.Dialog;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class MainWindowViewModel : BaseViewModel
    {
        private readonly INavigationService _navigation;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private BaseViewModel? _currentViewModel;

        [ObservableProperty]
        private DialogBaseViewModel? _currentDialog;

        [ObservableProperty]
        private bool _hasOpenDialogs;

        public MainWindowViewModel(INavigationService navigation, IDialogService dialogService)
        {
            System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Constructor called");
            System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] navigation: {navigation?.GetType().Name ?? "null"}");
            System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] dialogService: {dialogService?.GetType().Name ?? "null"}");

            _navigation = navigation;
            _dialogService = dialogService;

            System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Subscribing to events...");
            _navigation.CurrentViewModelChanged += OnNavigationViewModelChanged;
            _dialogService.OnDialogStackChanged += OnDialogStackChanged;

            System.Diagnostics.Debug.WriteLine("[MainWindowViewModel] Constructor completed, navigating to login");
            _navigation.NavigateToLogin();
        }

        private void OnNavigationViewModelChanged(BaseViewModel vm)
        {
            CurrentViewModel = vm;
            ClearMessages();
        }

        private void OnDialogStackChanged()
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] OnDialogStackChanged called. Stack size: {_dialogService.DialogStack.Count}");

            var newDialog = _dialogService.DialogStack.Count > 0 ? 
                _dialogService.DialogStack[_dialogService.DialogStack.Count - 1] : null;

            CurrentDialog = newDialog;
            HasOpenDialogs = _dialogService.HasOpenDialogs;

            if (CurrentDialog != null)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] CurrentDialog set to: {CurrentDialog.GetType().Name} - {CurrentDialog.Title}");
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] HasOpenDialogs: {HasOpenDialogs}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] CurrentDialog cleared");
            }
        }

        [RelayCommand]
        public static void ToggleTheme() => App.Current.ToggleTheme();

        [RelayCommand]
        public async Task Logout()
        {
            await SafeExecuteAsync(async () =>
            {
                await _dialogService.CloseAllAsync();
                var authService = App.Current.Services.GetRequiredService<AuthService>();
                await authService.ClearAuthAsync();
                _navigation.NavigateToLogin();
                SuccessMessage = "Выход выполнен успешно";
            });
        }

        [RelayCommand]
        public void ClearNotifications() => ClearMessages();

        [RelayCommand]
        public async Task RefreshCurrentView()
        {
            await SafeExecuteAsync(async () =>
            {
                if (CurrentViewModel is ChatsViewModel chatsViewModel)
                    if (chatsViewModel.LoadChatsCommand?.CanExecute(null) == true) 
                        await chatsViewModel.LoadChatsCommand.ExecuteAsync(null);
                else if (CurrentViewModel is AdminViewModel adminViewModel)
                    if (adminViewModel.RefreshCommand?.CanExecute(null) == true) 
                        await adminViewModel.RefreshCommand.ExecuteAsync(null);
                else if (CurrentViewModel is ProfileViewModel profileViewModel) 
                    OnPropertyChanged(nameof(CurrentViewModel));

                SuccessMessage = "Данные обновлены";
            });
        }

        [RelayCommand]
        public void ShowSettings()
        {
            if (CurrentViewModel is MainMenuViewModel mainMenu)
                mainMenu.SetItem(0); 
        }

        /// <summary>Показать диалог</summary>
        public async Task ShowDialogAsync<TViewModel>(TViewModel dialogViewModel) where TViewModel : DialogBaseViewModel
        {
            await _dialogService.ShowAsync(dialogViewModel);
        }

        protected override void ClearMessages()
        {
            base.ClearMessages();
            System.Diagnostics.Debug.WriteLine("MainWindow messages cleared");
        }
    }
}