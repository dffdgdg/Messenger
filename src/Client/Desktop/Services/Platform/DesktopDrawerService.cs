using Avalonia.Controls.ApplicationLifetimes;
using Core.Services.Platform.Abstractions;
using Desktop.Views;

namespace Desktop.Shared.Services.Platform;

public sealed class DesktopDrawerService : IDrawerService
{
    public void CloseDrawer()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
        {
            window.CloseDrawer();
        }
    }
}