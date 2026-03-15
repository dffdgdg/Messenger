using Avalonia.Input;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.Views.Dialog;

public partial class ChatPickerDialog : UserControl
{
    public ChatPickerDialog() => InitializeComponent();

    private void OnChatItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ChatPickerDialogViewModel vm)
            return;

        if (sender is not Border { DataContext: ChatDto chat })
            return;

        vm.SelectChatCommand.Execute(chat);
        e.Handled = true;
    }
}