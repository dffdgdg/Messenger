using Core.Services.Platform.Abstractions;

namespace Mobile.Services.Platform;

public sealed class MobileDrawerService : IDrawerService
{
    // На Android drawer — это нативный NavigationDrawer или BottomSheet,
    // управляется через MainView напрямую, не через этот сервис
    public void CloseDrawer() { }
}