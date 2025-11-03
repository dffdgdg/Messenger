using Avalonia.Controls.Notifications;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public class SystemNotificationService : ISystemNotificationService, IDisposable
    {
        private bool _disposedValue;

        public SystemNotificationService()
        {
        }

        public async Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information, int durationMs = 3000)
        {

        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                   
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}