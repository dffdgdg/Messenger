using Avalonia.Input;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.Views.Dialog;

public partial class UserListDialog : UserControl
{
    public UserListDialog() => InitializeComponent();

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not UserListDialogViewModel vm || !vm.IsEditMode || !vm.AllowEdit)
            return;

        if (sender is not Border { DataContext: UserListItemViewModel user })
            return;

        user.IsSelected = !user.IsSelected;
        e.Handled = true;
    }
}
