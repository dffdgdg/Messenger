using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class ConfirmDialogViewModel : DialogBaseViewModel
{
    private readonly TaskCompletionSource<bool> _resultTcs = new();

    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial string ConfirmText { get; set; } = "Да";
    [ObservableProperty] public partial string CancelText { get; set; } = "Отмена";

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
    protected override Task CloseOnBackgroundClick()
    {
        _resultTcs.TrySetResult(false);
        return base.CloseOnBackgroundClick();
    }

    protected override Task Cancel()
    {
        _resultTcs.TrySetResult(false);
        return base.Cancel();
    }
}