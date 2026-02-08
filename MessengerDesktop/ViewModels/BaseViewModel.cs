using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public abstract partial class BaseViewModel : ObservableObject, IDisposable
    {
        private CancellationTokenSource? _cts;
        private bool _disposed;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string? _successMessage;

        // ========== Хуки для подклассов ==========
        // CommunityToolkit генерирует partial void OnXxxChanged в ЭТОМ классе.
        // Мы пробрасываем их в виртуальные методы,
        // чтобы подклассы могли реагировать через override.

        partial void OnIsBusyChanged(bool value) => OnIsBusyUpdated(value);

        partial void OnErrorMessageChanged(string? value) => OnErrorMessageUpdated(value);

        partial void OnSuccessMessageChanged(string? value) => OnSuccessMessageUpdated(value);

        /// <summary>
        /// Вызывается при изменении IsBusy. 
        /// Переопределите для обновления CanExecute команд.
        /// </summary>
        protected virtual void OnIsBusyUpdated(bool value) { }

        /// <summary>
        /// Вызывается при изменении ErrorMessage.
        /// Переопределите для взаимоисключения сообщений и т.д.
        /// </summary>
        protected virtual void OnErrorMessageUpdated(string? value) { }

        /// <summary>
        /// Вызывается при изменении SuccessMessage.
        /// Переопределите для взаимоисключения сообщений и т.д.
        /// </summary>
        protected virtual void OnSuccessMessageUpdated(string? value) { }

        // ========== Cancellation ==========

        protected CancellationToken GetCancellationToken()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }

        // ========== Safe Execute ==========

        protected async Task SafeExecuteAsync(
            Func<Task> operation,
            string? successMessage = null,
            Action? finallyAction = null)
        {
            try
            {
                IsBusy = true;
                ErrorMessage = null;
                await operation();
                if (!string.IsNullOrEmpty(successMessage))
                    SuccessMessage = successMessage;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Debug.WriteLine($"Error in {GetType().Name}: {ex}");
            }
            finally
            {
                IsBusy = false;
                finallyAction?.Invoke();
            }
        }

        protected async Task SafeExecuteAsync(
            Func<CancellationToken, Task> operation,
            string? successMessage = null,
            Action? finallyAction = null)
        {
            var ct = GetCancellationToken();
            try
            {
                IsBusy = true;
                ErrorMessage = null;
                await operation(ct);
                if (!string.IsNullOrEmpty(successMessage) && !ct.IsCancellationRequested)
                    SuccessMessage = successMessage;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    ErrorMessage = ex.Message;
                    Debug.WriteLine($"Error in {GetType().Name}: {ex}");
                }
            }
            finally
            {
                IsBusy = false;
                finallyAction?.Invoke();
            }
        }

        // ========== Helpers ==========

        protected static string? GetAbsoluteUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
            return $"{App.ApiUrl.TrimEnd('/')}/{url.TrimStart('/')}";
        }

        [RelayCommand]
        protected virtual void ClearMessages()
        {
            ErrorMessage = null;
            SuccessMessage = null;
        }

        // ========== Dispose ==========

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            _disposed = true;
        }
    }
}