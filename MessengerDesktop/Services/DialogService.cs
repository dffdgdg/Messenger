using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.ViewModels.Dialog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    /// <summary>
    /// Сервис управления диалогами на уровне приложения.
    /// Обеспечивает централизованное управление открытием/закрытием диалогов
    /// с затемнением фона на весь экран.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Стек активных диалогов (для поддержки модальных диалогов друг над другом)</summary>
        IReadOnlyList<DialogBaseViewModel> DialogStack { get; }

        /// <summary>Событие при изменении стека диалогов</summary>
        event Action? OnDialogStackChanged;

        /// <summary>Открыть диалог</summary>
        Task ShowAsync<TViewModel>(TViewModel dialogViewModel) where TViewModel : DialogBaseViewModel;

        /// <summary>Закрыть верхний диалог</summary>
        Task CloseAsync();

        /// <summary>Закрыть все диалоги</summary>
        Task CloseAllAsync();

        /// <summary>Есть ли открытые диалоги</summary>
        bool HasOpenDialogs { get; }
    }

    public partial class DialogService : ObservableObject, IDialogService
    {
        private readonly List<DialogBaseViewModel> _dialogStack = [];

        [ObservableProperty]
        private DialogBaseViewModel? _currentDialog;

        public IReadOnlyList<DialogBaseViewModel> DialogStack => _dialogStack.AsReadOnly();
        public event Action? OnDialogStackChanged;
        public bool HasOpenDialogs => _dialogStack.Count > 0;

        public async Task ShowAsync<TViewModel>(TViewModel dialogViewModel) where TViewModel : DialogBaseViewModel
        {
            ArgumentNullException.ThrowIfNull(dialogViewModel);

            System.Diagnostics.Debug.WriteLine($"[DialogService] ShowAsync called for: {dialogViewModel.GetType().Name} - {dialogViewModel.Title}");

            if (CurrentDialog != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DialogService] Existing dialog found: {CurrentDialog.GetType().Name}");
                CurrentDialog.CloseRequested -= OnDialogClosed;
            }

            _dialogStack.Add(dialogViewModel);
            CurrentDialog = dialogViewModel;

            System.Diagnostics.Debug.WriteLine($"[DialogService] Dialog added to stack. Stack size: {_dialogStack.Count}");
            System.Diagnostics.Debug.WriteLine($"[DialogService] HasOpenDialogs: {HasOpenDialogs}");

            dialogViewModel.CloseRequested += OnDialogClosed;

            OnDialogStackChanged?.Invoke();

            await Task.Delay(50);

            System.Diagnostics.Debug.WriteLine($"[DialogService] ShowAsync completed");
        }

        public async Task CloseAsync()
        {
            if (CurrentDialog == null)
                return;

            CurrentDialog.CloseRequested -= OnDialogClosed;
            _dialogStack.Remove(CurrentDialog);

            CurrentDialog = _dialogStack.Count > 0 ? _dialogStack[^1] : null;

            if (CurrentDialog != null)
            {
                CurrentDialog.CloseRequested += OnDialogClosed;
            }

            OnDialogStackChanged?.Invoke();
            await Task.Delay(50);
        }

        public async Task CloseAllAsync()
        {
            foreach (var dialog in _dialogStack)
            {
                dialog.CloseRequested -= OnDialogClosed;
            }

            _dialogStack.Clear();
            CurrentDialog = null;
            OnDialogStackChanged?.Invoke();

            await Task.Delay(50);
        }

        private async void OnDialogClosed()
        {
            await CloseAsync();
        }
    }
}
