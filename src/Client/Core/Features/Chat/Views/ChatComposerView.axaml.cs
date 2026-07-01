using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Core.Features.Chat.ViewModels;

namespace Core.Features.Chat.Views;

public partial class ChatComposerView : UserControl
{
    private ChatViewModel? _ViewModel;

    public ChatComposerView() => InitializeComponent();
    private void ComposerTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if ((DataContext as ChatViewModel)?.HandleMentionNavigationKey(e.Key) == true)
            e.Handled = true;
    }

    private void ComposerTextBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_ViewModel is not null && sender is TextBox tb)
            _ViewModel.OnComposerSelectionChanged(tb.CaretIndex);
    }

    private void ComposerTextBox_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_ViewModel is not null && sender is TextBox tb)
            _ViewModel.OnComposerSelectionChanged(tb.CaretIndex);
    }

}