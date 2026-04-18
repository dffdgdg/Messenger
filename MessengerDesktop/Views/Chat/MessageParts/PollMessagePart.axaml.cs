using MessengerDesktop.ViewModels.Chat;
using System;

namespace MessengerDesktop.Views.Chat.MessageParts;

public partial class PollMessagePart : UserControl
{
    private PollViewModel? _pollViewModel;
    private double _lastHeight;

    public PollMessagePart()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _pollViewModel = (DataContext as MessageViewModel)?.Poll;
        _lastHeight = Bounds.Height;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        double delta = e.NewSize.Height - e.PreviousSize.Height;
        if (Math.Abs(delta) < 1) return;

        _pollViewModel?.NotifySizeChanged(delta);
    }
}