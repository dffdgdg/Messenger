using Avalonia.Controls;
using Avalonia.Interactivity;
using MessengerDesktop.ViewModels;

namespace MessengerDesktop.Views;

public partial class PollDialog : UserControl
{
    public PollDialog()
    {
        InitializeComponent();
    }

    private void RemoveOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is object tag && this.DataContext is PollDialogViewModel vm)
        {
            if (tag is PollDialogViewModel.OptionItem option)
            {
                vm.RemoveOptionCommand.Execute(option);
            }
        }
    }
}
