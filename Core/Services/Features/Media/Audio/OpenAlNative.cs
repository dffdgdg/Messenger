using System.Runtime.InteropServices;

namespace Core.Services.Features.Media.Audio;

public static class OpenAlNative
{
    private static readonly nint _lib = LoadLib();

    private static nint LoadLib()
    {
        // Silk.NET.OpenAL.Soft.Native копирует нативную либу рядом с exe.
        // Имена файлов зависят от платформы:
        var candidates = GetCandidates();

        foreach (var name in candidates)
        {
            try
            {
                var handle = NativeLibrary.Load(name);
                if (handle != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenAlNative] Loaded: {name}");
                    return handle;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenAlNative] Failed {name}: {ex.Message}");
            }
        }

        throw new DllNotFoundException(
            $"OpenAL не найден. Попробованные имена: {string.Join(", ", candidates)}");
    }

    private static string[] GetCandidates()
    {
        if (OperatingSystem.IsWindows())
            return ["soft_oal.dll", "OpenAL32.dll", "OpenAL64.dll"];

        if (OperatingSystem.IsMacOS())
            return [
                "libopenal.dylib",
                "/System/Library/Frameworks/OpenAL.framework/OpenAL",
                "soft_oal.dylib"
            ];

        // Linux
        return ["libopenal.so.1", "libopenal.so", "soft_oal.so"];
    }

    private static T GetExport<T>(string name) where T : Delegate
    {
        var ptr = NativeLibrary.GetExport(_lib, name);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    // ── Делегаты ─────────────────────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nint alcCaptureOpenDeviceDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string? deviceName,
        uint frequency, int format, int buffersize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate bool alcCaptureCloseDeviceDelegate(nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void alcCaptureStartDelegate(nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void alcCaptureStopDelegate(nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void alcGetIntegervDelegate(
        nint device, int param, int size, out int data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void alcCaptureSamplesDelegate(
        nint device, nint buffer, int samples);

    // ── Экспорты ─────────────────────────────────────────────────────────

    public static readonly alcCaptureOpenDeviceDelegate CaptureOpenDevice =
        GetExport<alcCaptureOpenDeviceDelegate>("alcCaptureOpenDevice");

    public static readonly alcCaptureCloseDeviceDelegate CaptureCloseDevice =
        GetExport<alcCaptureCloseDeviceDelegate>("alcCaptureCloseDevice");

    public static readonly alcCaptureStartDelegate CaptureStart =
        GetExport<alcCaptureStartDelegate>("alcCaptureStart");

    public static readonly alcCaptureStopDelegate CaptureStop =
        GetExport<alcCaptureStopDelegate>("alcCaptureStop");

    public static readonly alcGetIntegervDelegate GetIntegerv =
        GetExport<alcGetIntegervDelegate>("alcGetIntegerv");

    public static readonly alcCaptureSamplesDelegate CaptureSamples =
        GetExport<alcCaptureSamplesDelegate>("alcCaptureSamples");
}