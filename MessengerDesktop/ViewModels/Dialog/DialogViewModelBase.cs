using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace MessengerDesktop.ViewModels.Dialog
{
    public abstract partial class DialogViewModelBase : ViewModelBase
    {
        public Action? CloseRequested { get; set; }

        protected void RequestClose() => CloseRequested?.Invoke();

        [RelayCommand]
        protected virtual void Cancel() => RequestClose();

        [ObservableProperty]
        private bool _isBusy;
    }
}
