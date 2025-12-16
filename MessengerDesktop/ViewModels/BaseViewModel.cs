using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
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

        /// <summary>
        /// Получает новый CancellationToken, отменяя предыдущий
        /// </summary>
        protected CancellationToken GetCancellationToken()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }

        /// <summary>
        /// Безопасное выполнение асинхронной операции с обработкой ошибок
        /// </summary>
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
            catch (OperationCanceledException)
            {
                // Операция отменена - не показываем ошибку
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Error in {GetType().Name}: {ex}");
            }
            finally
            {
                IsBusy = false;
                finallyAction?.Invoke();
            }
        }

        /// <summary>
        /// Безопасное выполнение с CancellationToken
        /// </summary>
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
            catch (OperationCanceledException)
            {
                // Операция отменена - не показываем ошибку
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    ErrorMessage = ex.Message;
                    System.Diagnostics.Debug.WriteLine($"Error in {GetType().Name}: {ex}");
                }
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}