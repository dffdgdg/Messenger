using Avalonia.Media.Imaging;
using Desktop.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop.Infrastructure.Media;

public static class RemoteImage
{
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(RemoteImage));

    private static readonly AttachedProperty<CancellationTokenSource?> CtsProperty =
        AvaloniaProperty.RegisterAttached<Image, CancellationTokenSource?>("LoadCts", typeof(RemoteImage));

    public static readonly AttachedProperty<string?> CurrentUrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("CurrentUrl", typeof(RemoteImage));

    static RemoteImage() => SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);

    public static string? GetSource(Image image) => image.GetValue(SourceProperty);
    public static void SetSource(Image image, string? value) => image.SetValue(SourceProperty, value);

    private static void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var newUrl = e.NewValue as string;
        var currentUrl = image.GetValue(CurrentUrlProperty);

        if (!string.IsNullOrWhiteSpace(newUrl) && newUrl == currentUrl && image.Source != null)
            return;

        CancelCurrent(image);

        if (string.IsNullOrWhiteSpace(newUrl))
        {
            if (image.Source is Bitmap old)
            {
                image.Source = null;
                old.Dispose();
                MemoryDiagnostics.OnBitmapDisposed();
            }
            else
            {
                image.Source = null;
            }
            image.SetValue(CurrentUrlProperty, null);
            return;
        }

        image.Source = null;

        var cts = new CancellationTokenSource();
        image.SetValue(CtsProperty, cts);
        image.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        image.DetachedFromVisualTree += OnDetachedFromVisualTree;

        MemoryDiagnostics.OnRemoteImageStarted();
        _ = LoadAsync(image, newUrl, cts.Token);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Image image) return;
        image.DetachedFromVisualTree -= OnDetachedFromVisualTree;

        if (image.Source is Bitmap old)
        {
            image.Source = null;
            old.Dispose();
            MemoryDiagnostics.OnBitmapDisposed();
        }

        CancelCurrent(image);
    }

    private static async Task LoadAsync(Image image, string url, CancellationToken ct)
    {
        Bitmap? bitmap = null;
        try
        {
            var loader = App.Current.Services.GetRequiredService<AuthenticatedImageLoader>();

            var data = await loader.GetBytesAsync(url, ct);
            if (data == null || ct.IsCancellationRequested)
            {
                MemoryDiagnostics.OnRemoteImageCancelled();
                return;
            }

            bitmap = await Task.Run(() =>
            {
                using var ms = new MemoryStream(data);
                return new Bitmap(ms);
            }, ct);

            MemoryDiagnostics.OnBitmapCreated();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested)
                    return;

                if (image.Source is Bitmap old)
                {
                    image.Source = null;
                    old.Dispose();
                    MemoryDiagnostics.OnBitmapDisposed();
                }

                image.Source = bitmap;
                bitmap = null;
                image.SetValue(CurrentUrlProperty, url);
                MemoryDiagnostics.OnRemoteImageCompleted();
            }, DispatcherPriority.Background, ct);
        }
        catch (OperationCanceledException)
        {
            MemoryDiagnostics.OnRemoteImageCancelled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteImage] Load error for {url}: {ex.Message}");
        }
        finally
        {
            if (bitmap != null)
            {
                bitmap.Dispose();
                MemoryDiagnostics.OnBitmapDisposed();
            }
        }
    }

    private static void CancelCurrent(Image image)
    {
        var cts = image.GetValue(CtsProperty);
        if (cts is null) return;

        cts.Cancel();
        cts.Dispose();
        image.SetValue(CtsProperty, null);
    }
}