using CommunityToolkit.Mvvm.Input;

namespace MessengerDesktop.ViewModels;

/// <summary>
/// Контракт для ViewModel, поддерживающих обновление данных.
/// </summary>
public interface IRefreshable
{
    IAsyncRelayCommand RefreshCommand { get; }
}