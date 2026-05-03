using Avalonia.Input;
using Desktop.ViewModels.Dialog;

namespace Desktop.Views.Dialog;

public partial class UserPickerDialog : UserControl
{
    public UserPickerDialog() => InitializeComponent();

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not UserPickerDialogViewModel vm)
            return;

        if (sender is not Border { DataContext: UserListItemViewModel user })
            return;

        if (vm.IsMultiSelect)
        {
            if (vm.AllowEdit)
            {
                user.IsSelected = !user.IsSelected;
                e.Handled = true;
            }
        }
        else
        {
            vm.SelectSingleUserCommand.Execute(user);
            e.Handled = true;
        }
    }
}