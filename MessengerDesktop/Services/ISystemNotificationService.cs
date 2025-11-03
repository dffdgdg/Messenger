using Avalonia.Controls.Notifications;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
 public interface ISystemNotificationService
 {
 Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information, int durationMs =3000);
 }
}
