using MessengerDesktop.ViewModels.Dialog;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class MainWindowViewModel : BaseViewModel
    {
        private readonly INavigationService _navigation;
        private readonly IDialogService _dialogService;
        private readonly IAuthManager _authManager;
        private readonly IThemeService _themeService;

        [ObservableProperty]
        private BaseViewModel? _currentViewModel;

        [ObservableProperty]
        private DialogBaseViewModel? _currentDialog;

        [ObservableProperty]
        private bool _hasOpenDialogs;

        [ObservableProperty]
        private bool _isDialogVisible;

        public MainWindowViewModel(
            INavigationService navigation,
            IDialogService dialogService,
            IAuthManager authManager,
            IThemeService themeService)
        {
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

            _navigation.CurrentViewModelChanged += OnNavigationViewModelChanged;
            _dialogService.OnDialogStackChanged += OnDialogStackChanged;

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

        private void OnDialogStackChanged()
        {
            CurrentDialog = _dialogService.CurrentDialog;
            HasOpenDialogs = _dialogService.HasOpenDialogs;
            IsDialogVisible = _dialogService.IsDialogVisible;
        }

        private void OnNavigationViewModelChanged(BaseViewModel vm)
        {
            CurrentViewModel = vm;
            ClearMessages();
        }

        [RelayCommand]
        public void ToggleTheme() => _themeService.Toggle();

        [RelayCommand]
        public void ClearNotifications() => ClearMessages();

        [RelayCommand]
        public async Task RefreshCurrentView() => await SafeExecuteAsync(async () =>
        {
            if (CurrentViewModel is IRefreshable refreshable &&
                refreshable.RefreshCommand.CanExecute(null))
            {
                await refreshable.RefreshCommand.ExecuteAsync(null);
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

                (CurrentViewModel as IDisposable)?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}