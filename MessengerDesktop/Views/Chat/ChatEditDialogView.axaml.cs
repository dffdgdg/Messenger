using Avalonia.Controls;
using Avalonia.Input;
using MessengerDesktop.ViewModels;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.Views;

public partial class ChatEditDialog : UserControl
{
    public ChatEditDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ChatEditDialogViewModel vm)
                await vm.InitializeCommand.ExecuteAsync(null);
        };
    }

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ChatEditDialogViewModel.SelectableUserItem user)
        {
            user.IsSelected = !user.IsSelected;
            e.Handled = true;
        }
    }
}