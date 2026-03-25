using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public abstract partial class DialogBaseViewModel : BaseViewModel
{
    public Func<Task>? CloseRequested { get; set; }

    [ObservableProperty] public partial string Title { get; set; } = "Диалог";
    [ObservableProperty] public partial bool CanCloseOnBackgroundClick { get; set; } = true;
    [ObservableProperty] public partial bool IsInitialized { get; set; }

    protected Task RequestCloseAsync() => CloseRequested?.Invoke() ?? Task.CompletedTask;
    protected void RequestClose() => _ = RequestCloseAsync();

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
    protected virtual Task Cancel() => RequestCloseAsync();

    [RelayCommand]
    protected virtual Task CloseOnBackgroundClick()
    {
        if (CanCloseOnBackgroundClick)
            return RequestCloseAsync();

        return Task.CompletedTask;
    }
}