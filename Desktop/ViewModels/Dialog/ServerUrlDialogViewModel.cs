namespace Desktop.ViewModels.Dialog;

public partial class ServerUrlDialogViewModel : DialogBaseViewModel
{
    [ObservableProperty] public partial string ServerUrl { get; set; } = string.Empty;
    [ObservableProperty] public partial string ValidationError { get; set; } = string.Empty;

    public ServerUrlDialogViewModel(string currentUrl)
    {
        Title = "Указать адрес сервера";
        ServerUrl = currentUrl;
        CanCloseOnBackgroundClick = true;
    }

    partial void OnServerUrlChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(ValidationError))
            ValidationError = string.Empty;
    }

    /// <summary>
    /// Переопределяем Cancel из DialogBaseViewModel — 
    /// очищаем URL чтобы вызывающий код понял, что диалог отменён
    /// </summary>
    protected override Task Cancel()
    {
        ServerUrl = string.Empty;
        return base.Cancel();
    }

    [RelayCommand]
    private void Confirm()
    {
        var url = ServerUrl?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            ValidationError = "Введите адрес сервера";
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }

        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            ValidationError = "Некорректный URL. Пример: 192.168.1.100:5274";
            return;
        }

        ServerUrl = uri.ToString();
        ValidationError = string.Empty;
        RequestClose();
    }
}