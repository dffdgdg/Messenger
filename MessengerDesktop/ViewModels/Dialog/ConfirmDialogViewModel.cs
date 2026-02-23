using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class ConfirmDialogViewModel : DialogBaseViewModel
{
    private readonly TaskCompletionSource<bool> _resultTcs = new();

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _confirmText = "Да";

    [ObservableProperty]
    private string _cancelText = "Отмена";

    /// <summary>
    /// Ожидание результата диалога: true = подтверждено, false = отменено
    /// </summary>
    public Task<bool> Result => _resultTcs.Task;

    public ConfirmDialogViewModel(string title, string message)
    {
        Title = title;
        Message = message;
        CanCloseOnBackgroundClick = true;
    }

    public ConfirmDialogViewModel(string title, string message, string confirmText, string cancelText) : this(title, message)
    {
        ConfirmText = confirmText;
        CancelText = cancelText;
    }

    [RelayCommand]
    private void Confirm()
    {
        _resultTcs.TrySetResult(true);
        RequestClose();
    }
    protected override void CloseOnBackgroundClick()
    {
        _resultTcs.TrySetResult(false);
        base.CloseOnBackgroundClick();
    }

    protected override void Cancel()
    {
        _resultTcs.TrySetResult(false);
        base.Cancel();
    }
}