using Core.Services.Abstractions;

namespace Android.Services.Platform.OS;

public sealed class AndroidDrawerService : IDrawerService
{
    // На Android drawer — это нативный NavigationDrawer или BottomSheet,
    // управляется через MainView напрямую, не через этот сервис
    public void CloseDrawer() { }
}