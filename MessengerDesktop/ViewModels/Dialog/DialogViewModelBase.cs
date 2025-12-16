using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace MessengerDesktop.ViewModels.Dialog
{
    public abstract partial class DialogBaseViewModel : BaseViewModel
    {
        public Action? CloseRequested { get; set; }

        [ObservableProperty]
        private string title = "Диалог";

        [ObservableProperty]
        private bool canCloseOnBackgroundClick = true;

        protected void RequestClose() 
            => CloseRequested?.Invoke();

        [RelayCommand]
        protected virtual void Cancel()
            => RequestClose();

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