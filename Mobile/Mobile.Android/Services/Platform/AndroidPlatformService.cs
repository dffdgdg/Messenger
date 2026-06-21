using Android.App;
using Android.Content;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Core.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Mobile.Android.Services.Platform;

public class AndroidPlatformService : IPlatformService
{
    private static IStorageProvider? GetAndroidStorageProvider()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is ISingleViewApplicationLifetime { MainView: { } mainView })
        {
            return TopLevel.GetTopLevel(mainView)?.StorageProvider;
        }
        return null;
    }

    public IStorageProvider? GetStorageProvider() => GetAndroidStorageProvider();

    public void Initialize(object? platformContext = null) { }
    public void Cleanup() { }
    public bool IsClipboardAvailable() => true;

    public async Task<bool> CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            var context = global::Android.App.Application.Context;
            var clipboard = (ClipboardManager?)context
                .GetSystemService(Context.ClipboardService);
            if (clipboard is null) return false;
            clipboard.PrimaryClip = ClipData.NewPlainText("text", text);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidPlatform] CopyToClipboard error: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetFromClipboardAsync()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var clipboard = (ClipboardManager?)context
                .GetSystemService(Context.ClipboardService);
            if (clipboard?.PrimaryClip == null
                || clipboard.PrimaryClip.ItemCount == 0)
                return null;
            return clipboard.PrimaryClip
                .GetItemAt(0)
                ?.CoerceToText(context)
                ?.ToString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidPlatform] GetFromClipboard error: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ClearClipboardAsync()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var clipboard = (ClipboardManager?)context
                .GetSystemService(Context.ClipboardService);
            if (clipboard is null) return false;

            if (OperatingSystem.IsAndroidVersionAtLeast(28))
                clipboard.ClearPrimaryClip();
            else
                clipboard.PrimaryClip = ClipData.NewPlainText("", "");

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidPlatform] ClearClipboard error: {ex.Message}");
            return false;
        }
    }

    public Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
    {
        throw new NotImplementedException();
    }
}