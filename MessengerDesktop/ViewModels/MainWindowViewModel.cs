using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Auth;  
using MessengerDesktop.Services.Navigation;
using MessengerDesktop.ViewModels.Dialog;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class MainWindowViewModel : BaseViewModel
    {
        private readonly INavigationService _navigation;
        private readonly IDialogService _dialogService;
        private readonly IAuthManager _authManager;  

        [ObservableProperty]
        private BaseViewModel? _currentViewModel;

        [ObservableProperty]
        private DialogBaseViewModel? _currentDialog;

        [ObservableProperty]
        private bool _hasOpenDialogs;

        [ObservableProperty]
        private bool _isDialogVisible;

        public MainWindowViewModel(INavigationService navigation,IDialogService dialogService,IAuthManager authManager)  
        {
            _navigation = navigation ?? throw new System.ArgumentNullException(nameof(navigation));
            _dialogService = dialogService ?? throw new System.ArgumentNullException(nameof(dialogService));
            _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));

            _navigation.CurrentViewModelChanged += OnNavigationViewModelChanged;
            _dialogService.OnDialogStackChanged += OnDialogStackChanged;

            if (_dialogService is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged += OnDialogServicePropertyChanged;
            }

            _navigation.NavigateToLogin();
        }

        [RelayCommand]
        public async Task Logout() => await SafeExecuteAsync(async () =>
        {
            await _dialogService.CloseAllAsync();
            await _authManager.LogoutAsync();
            _navigation.NavigateToLogin();
            SuccessMessage = "Выход выполнен успешно";
        });

        private void OnDialogServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(DialogService.IsDialogVisible):
                    IsDialogVisible = _dialogService.IsDialogVisible;
                    break;

                case nameof(DialogService.CurrentDialog):
                    CurrentDialog = _dialogService.CurrentDialog;
                    break;
            }
        }

        private void OnNavigationViewModelChanged(BaseViewModel vm)
        {
            CurrentViewModel = vm;
            ClearMessages();
        }

        private void OnDialogStackChanged()
        {
            CurrentDialog = _dialogService.CurrentDialog;
            HasOpenDialogs = _dialogService.HasOpenDialogs;
            IsDialogVisible = _dialogService.IsDialogVisible;
        }

        [RelayCommand]
        public static void ToggleTheme() => App.Current.ToggleTheme();

        [RelayCommand]
        public void ClearNotifications() => ClearMessages();

        [RelayCommand]
        public async Task RefreshCurrentView() => await SafeExecuteAsync(async () =>
        {
            if (CurrentViewModel is ChatsViewModel chatsViewModel)
            {
                if (chatsViewModel.LoadChatsCommand?.CanExecute(null) == true)
                    await chatsViewModel.LoadChatsCommand.ExecuteAsync(null);
            }
            else if (CurrentViewModel is AdminViewModel adminViewModel)
            {
                if (adminViewModel.RefreshCommand?.CanExecute(null) == true)
                    await adminViewModel.RefreshCommand.ExecuteAsync(null);
            }
            else if (CurrentViewModel is ProfileViewModel)
            {
                OnPropertyChanged(nameof(CurrentViewModel));
            }
            
            SuccessMessage = "Данные обновлены";
        });

        [RelayCommand]
        public void ShowSettings()
        {
            if (CurrentViewModel is MainMenuViewModel mainMenu)
                mainMenu.SetItemCommand.Execute(0);
        }

        public async Task ShowDialogAsync<TViewModel>(TViewModel dialogViewModel) where TViewModel : DialogBaseViewModel
            => await _dialogService.ShowAsync(dialogViewModel);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _navigation.CurrentViewModelChanged -= OnNavigationViewModelChanged;
                _dialogService.OnDialogStackChanged -= OnDialogStackChanged;

                if (_dialogService is INotifyPropertyChanged notifyPropertyChanged)
                {
                    notifyPropertyChanged.PropertyChanged -= OnDialogServicePropertyChanged;
                }

                (CurrentViewModel as System.IDisposable)?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}