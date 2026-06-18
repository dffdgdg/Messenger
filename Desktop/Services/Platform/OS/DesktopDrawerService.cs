using Avalonia.Controls.ApplicationLifetimes;
using Desktop.Views;

namespace Desktop.Services.Platform.OS;

public sealed class DesktopDrawerService : IDrawerService
{
    public void CloseDrawer()
    {
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
        {
            window.CloseDrawer();
        }
    }
}