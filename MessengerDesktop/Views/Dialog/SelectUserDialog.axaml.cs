using Avalonia.Controls;
using Avalonia.Input;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.Dto.User;

namespace MessengerDesktop.Views.Dialogs;

public partial class SelectUserDialog : UserControl
{
    public SelectUserDialog() => InitializeComponent();

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: UserDto user } && DataContext is SelectUserDialogViewModel vm)
        {
            vm.SelectUserCommand.Execute(user);
        }
    }
}