using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Core.Services.Call.Abstractions;
using Core.Services.Media.Abstractions;
using Core.Services.Platform.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Mobile.Android.Services;
using Mobile.Android.Services.Platform;

namespace Mobile.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CreateAppBuilder()
    {
        // Регистрируем Android-специфичные сервисы
        App.RegisterPlatformServices = services =>
        {
            services.AddSingleton<IPlatformService, AndroidPlatformService>();
            services.AddSingleton<ISecureStorageService, AndroidSecureStorageService>();
            services.AddSingleton<IAudioPlaybackDevice, AndroidPlaybackDevice>();
            services.AddSingleton<IAudioRecorderService, AndroidAudioRecorderService>();
            services.AddSingleton<ICallAudioService, AndroidCallAudioService>();
            
        };

        return AppBuilder.Configure<App>().UseAndroid().WithInterFont();
    }
}