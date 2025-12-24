using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using MessengerDesktop.ViewModels;
using MessengerShared.DTO;

namespace MessengerDesktop.Views.Dialogs;

public partial class SelectUserDialog : UserControl
{
    public SelectUserDialog()
    {
        InitializeComponent();
    }

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: UserDTO user } &&
            DataContext is SelectUserDialogViewModel vm)
        {
            vm.SelectUserCommand.Execute(user);
        }
    }
}