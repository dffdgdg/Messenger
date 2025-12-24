using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public abstract partial class DialogBaseViewModel : BaseViewModel
{
    public Action? CloseRequested { get; set; }

    [ObservableProperty]
    private string _title = "Диалог";

    [ObservableProperty]
    private bool _canCloseOnBackgroundClick = true;

    [ObservableProperty]
    private bool _isInitialized;

    protected void RequestClose() => CloseRequested?.Invoke();

    /// <summary>
    /// Обёртка для безопасной инициализации
    /// </summary>
    protected async Task InitializeAsync(Func<Task> initAction)
    {
        if (IsInitialized) return;

        await SafeExecuteAsync(async () =>
        {
            await initAction();
            IsInitialized = true;
        });
    }

    [RelayCommand]
    protected virtual void Cancel() => RequestClose();

    [RelayCommand]
    public void CloseOnBackgroundClick()
    {
        if (CanCloseOnBackgroundClick)
            RequestClose();
    }
}