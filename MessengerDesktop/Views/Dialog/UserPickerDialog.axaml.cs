using Avalonia.Input;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.Views.Dialog;

public partial class UserPickerDialog : UserControl
{
    public UserPickerDialog() => InitializeComponent();

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ChatUserPickerDialogViewModel vm || !vm.AllowEdit)
            return;

        if (sender is Border border && border.DataContext is ChatEditDialogViewModel.SelectableUserItem user)
        {
            user.IsSelected = !user.IsSelected;
            e.Handled = true;
        }
    }
}