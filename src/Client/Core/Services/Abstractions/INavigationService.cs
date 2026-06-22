using Core.ViewModels;

namespace Core.Services.Abstractions;

public interface INavigationService
{
    void NavigateToLogin();
    void NavigateToMainMenu();
    void NavigateTo<T>() where T : BaseViewModel;
    event Action<BaseViewModel>? CurrentViewModelChanged;
    BaseViewModel? CurrentViewModel { get; }
}