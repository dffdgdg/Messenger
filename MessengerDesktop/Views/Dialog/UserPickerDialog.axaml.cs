using Avalonia.Input;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.Views.Dialog;

public partial class UserPickerDialog : UserControl
{
    public UserPickerDialog() => InitializeComponent();

    private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not UserPickerDialogViewModel vm)
            return;

        if (sender is not Border { DataContext: ChatEditDialogViewModel.SelectableUserItem user })
            return;

        if (vm.IsMultiSelect)
        {
            // Multi-select: toggle чекбокса
            if (vm.AllowEdit)
            {
                user.IsSelected = !user.IsSelected;
                e.Handled = true;
            }
        }
        else
        {
            // Single-select: выбрать и закрыть
            vm.SelectSingleUserCommand.Execute(user);
            e.Handled = true;
        }
    }
}