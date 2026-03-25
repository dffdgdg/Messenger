using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public abstract partial class DialogBaseViewModel : BaseViewModel
{
    public Action? CloseRequested { get; set; }

    [ObservableProperty] public partial string Title { get; set; } = "Диалог";
    [ObservableProperty] public partial bool CanCloseOnBackgroundClick { get; set; } = true;
    [ObservableProperty] public partial bool IsInitialized { get; set; }

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
    protected virtual void CloseOnBackgroundClick()
    {
        if (CanCloseOnBackgroundClick)
            RequestClose();
    }
}