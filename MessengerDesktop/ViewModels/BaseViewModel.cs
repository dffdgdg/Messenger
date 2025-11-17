using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public abstract partial class BaseViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string? _successMessage;

        protected async Task SafeExecuteAsync(Func<Task> operation, string? successMessage = null, Action? finallyAction = null)
        {
            try
            {
                IsBusy = true;
                ErrorMessage = null;
                await operation();

                if (!string.IsNullOrEmpty(successMessage)) 
                    SuccessMessage = successMessage;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error in {GetType().Name}: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                finallyAction?.Invoke();
            }
        }

        [RelayCommand]
        protected virtual void ClearMessages()
        {
            ErrorMessage = null;
            SuccessMessage = null;
        }
    }
}