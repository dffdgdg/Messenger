namespace Desktop.ViewModels.Shared;

/// <summary>
/// Контракт для ViewModel, поддерживающих обновление данных.
/// </summary>
public interface IRefreshable
{
    IAsyncRelayCommand RefreshCommand { get; }
}