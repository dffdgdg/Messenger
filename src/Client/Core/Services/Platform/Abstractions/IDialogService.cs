using Core.Dialog.Shared;

namespace Core.Services.Platform.Abstractions;

public interface IDialogService
{
    IReadOnlyList<DialogBaseViewModel> DialogStack { get; }
    DialogBaseViewModel? CurrentDialog { get; }
    bool HasOpenDialogs { get; }
    bool IsDialogVisible { get; }

    event Action? OnDialogStackChanged;
    event Action<bool>? OnDialogAnimationRequested;

    Task ShowAsync<TViewModel>(TViewModel dialogViewModel)
        where TViewModel : DialogBaseViewModel;
    Task CloseAsync();
    Task CloseAllAsync();
    void NotifyAnimationComplete();
}
