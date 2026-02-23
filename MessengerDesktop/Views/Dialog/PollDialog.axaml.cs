using Avalonia.Controls;
using Avalonia.Interactivity;
using MessengerDesktop.ViewModels;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.Views;

public partial class PollDialog : UserControl
{
    public PollDialog() => InitializeComponent();

    private void RemoveOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PollDialogViewModel.OptionItem option && DataContext is PollDialogViewModel vm)
        {
            vm.RemoveOptionCommand.Execute(option);
        }
    }
}