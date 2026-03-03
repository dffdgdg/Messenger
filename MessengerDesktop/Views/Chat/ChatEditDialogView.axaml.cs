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
}