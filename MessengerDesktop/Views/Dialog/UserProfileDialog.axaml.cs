using Avalonia.Controls;
using MessengerDesktop.ViewModels;

namespace MessengerDesktop.Views;

public partial class UserProfileDialog : UserControl
{
    public UserProfileDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is UserProfileDialogViewModel vm)
            {
                await vm.InitializeCommand.ExecuteAsync(null);
            }
        };
    }
}