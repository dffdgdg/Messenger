using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace MessengerDesktop.ViewModels.Dialog
{
    /// <summary>
    /// Базовая ViewModel для всех диалогов.
    /// Обеспечивает стандартное поведение: закрытие, отмена, обработка ошибок.
    /// </summary>
    public abstract partial class DialogBaseViewModel : BaseViewModel
    {
        /// <summary>Событие запроса на закрытие диалога</summary>
        public Action? CloseRequested { get; set; }

        /// <summary>Заголовок диалога</summary>
        [ObservableProperty]
        private string title = "Диалог";

        /// <summary>Может ли быть закрыт нажатием на фон</summary>
        [ObservableProperty]
        private bool canCloseOnBackgroundClick = true;

        /// <summary>Запросить закрытие диалога</summary>
        protected void RequestClose()
        {
            CloseRequested?.Invoke();
        }

        /// <summary>Команда отмены (закрытие диалога)</summary>
        [RelayCommand]
        protected virtual void Cancel()
        {
            RequestClose();
        }

        /// <summary>Команда закрытия диалога через фон</summary>
        [RelayCommand]
        public void CloseOnBackgroundClick()
        {
            if (CanCloseOnBackgroundClick)
            {
                RequestClose();
            }
        }
    }
}