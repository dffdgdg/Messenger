using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Core.Services.Abstractions;
using Core.Services.Features.Call;
using Core.Services.Features.Media.Abstractions;
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