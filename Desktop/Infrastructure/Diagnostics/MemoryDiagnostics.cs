using System.Diagnostics;
using System.Runtime;
using System.Text;

namespace Desktop.Infrastructure.Diagnostics;

// надеюсь я этой хуетой больше не воспользуюсь нахуй
// блять...
public static class MemoryDiagnostics
{
    #region RemoteImage counters

    private static int _loadStarted;
    private static int _loadCancelled;
    private static int _loadCompleted;
    private static int _bitmapCreated;
    private static int _bitmapDisposed;

    public static void OnRemoteImageStarted() => Interlocked.Increment(ref _loadStarted);
    public static void OnRemoteImageCancelled() => Interlocked.Increment(ref _loadCancelled);
    public static void OnRemoteImageCompleted() => Interlocked.Increment(ref _loadCompleted);
    public static void OnBitmapCreated() => Interlocked.Increment(ref _bitmapCreated);
    public static void OnBitmapDisposed() => Interlocked.Increment(ref _bitmapDisposed);

    public static int AliveBitmaps => _bitmapCreated - _bitmapDisposed;

    #endregion

    #region RichTextBlock counters

    private static int _richTextBlockAlive;
    public static void OnRichTextCreated() => Interlocked.Increment(ref _richTextBlockAlive);
    public static void OnRichTextDestroyed() => Interlocked.Decrement(ref _richTextBlockAlive);

    #endregion

    #region ChatVM / MessageVM counters

    private static int _chatVmAlive;
    private static int _chatVmCreated;
    private static int _chatVmDisposed;
    private static int _messageVmAlive;
    private static int _messageVmFinalized;

    public static int ChatVmAlive => _chatVmAlive;
    public static int ChatVmDisposed => _chatVmDisposed;
    public static int MessageVmAlive => _messageVmAlive;
    public static int MessageVmFinalized => _messageVmFinalized;

    public static void OnChatVmCreated()
    {
        Interlocked.Increment(ref _chatVmCreated);
        Interlocked.Increment(ref _chatVmAlive);
    }

    public static void OnChatVmDisposed()
    {
        Interlocked.Increment(ref _chatVmDisposed);
        Interlocked.Decrement(ref _chatVmAlive);
    }

    public static void OnMessageVmCreated() => Interlocked.Increment(ref _messageVmAlive);
    public static void OnMessageVmFinalized()
    {
        Interlocked.Decrement(ref _messageVmAlive);
        Interlocked.Increment(ref _messageVmFinalized);
    }

    #endregion

    #region ImageLoader counters (заполняется из AuthenticatedImageLoader)

    private static int _imgRamCount;
    private static long _imgRamBytes;
    private static int _imgLargeSkipped;
    private static int _imgDiskHits;
    private static int _imgNetworkFetches;

    public static void OnImageRamCachePut(long bytes)
    {
        Interlocked.Increment(ref _imgRamCount);
        Interlocked.Add(ref _imgRamBytes, bytes);
    }

    public static void OnImageRamCacheEvict(long bytes)
    {
        Interlocked.Decrement(ref _imgRamCount);
        Interlocked.Add(ref _imgRamBytes, -bytes);
    }
    public static void OnMessageVmDisposed()
    {
        Interlocked.Decrement(ref _messageVmAlive);
        Interlocked.Increment(ref _messageVmFinalized);
    }
    public static void OnImageLargeSkipped() => Interlocked.Increment(ref _imgLargeSkipped);
    public static void OnImageDiskHit() => Interlocked.Increment(ref _imgDiskHits);
    public static void OnImageNetworkFetch() => Interlocked.Increment(ref _imgNetworkFetches);

    /// <summary>
    /// Вызывается из AuthenticatedImageLoader.ClearCache() чтобы синхронизировать счётчики.
    /// </summary>
    public static void OnImageCacheCleared(int count, long bytes)
    {
        Interlocked.Add(ref _imgRamCount, -count);
        Interlocked.Add(ref _imgRamBytes, -bytes);
    }

    #endregion

    #region LOH tracking

    private static long _prevLohBytes;

    #endregion

    #region Process handles

    private static int _prevHandleCount;

    #endregion

    #region Experiment: ClearCache and measure LOH

    private static Func<(int count, long bytes)>? _getCacheStats;
    private static Action? _clearImageCache;

    /// <summary>
    /// Регистрируем делегаты из AuthenticatedImageLoader чтобы не создавать зависимость напрямую.
    /// Вызвать один раз при старте приложения.
    /// </summary>
    public static void RegisterImageLoader(Func<(int count, long bytes)> getStats, Action clearCache)
    {
        _getCacheStats = getStats;
        _clearImageCache = clearCache;
    }

    /// <summary>
    /// Очищает RAM-кэш изображений и замеряет изменение LOH.
    /// Используй только для диагностики — в продакшне не вызывать.
    /// </summary>
    public static void ExperimentClearImageCacheAndMeasureLoh(StringBuilder sb)
    {
        if (_getCacheStats == null || _clearImageCache == null)
        {
            sb.AppendLine("  [LOH experiment] ImageLoader не зарегистрирован");
            return;
        }

        var (countBefore, bytesBefore) = _getCacheStats();

        ForceFullGc();
        var lohBefore = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;

        _clearImageCache();

        ForceFullGc();
        var lohAfter = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;

        var freed = (long)lohBefore - (long)lohAfter;
        sb.AppendLine($"  [LOH experiment] кэш до: {countBefore} items / {bytesBefore / 1024}KB");
        sb.AppendLine($"  [LOH experiment] LOH до:  {lohBefore / 1024}KB  после: {lohAfter / 1024}KB  freed: {freed / 1024}KB");
        sb.AppendLine(freed > 0
            ? "  [LOH experiment] ✅ LOH уменьшился — ImageLoader виновен"
            : "  [LOH experiment] ❌ LOH не изменился — ищем дальше");
    }

    #endregion

    #region Disk cache info

    private static string? _diskCacheDir;

    public static void RegisterDiskCacheDir(string dir) => _diskCacheDir = dir;

    private static (int files, long bytes) GetDiskCacheInfo()
    {
        if (_diskCacheDir == null || !Directory.Exists(_diskCacheDir))
            return (0, 0);
        try
        {
            var files = Directory.GetFiles(_diskCacheDir);
            var total = files.Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0L; }
            });
            return (files.Length, total);
        }
        catch { return (0, 0); }
    }

    #endregion

    #region Core GC helpers

    private static void ForceFullGc()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    #endregion

    #region Dump

    public static void Dump(string label)
    {
        ForceFullGc();

        var proc = Process.GetCurrentProcess();
        proc.Refresh();

        var info = GC.GetGCMemoryInfo();
        var totalMemory = GC.GetTotalMemory(false);
        var totalAllocated = GC.GetTotalAllocatedBytes(false);
        var lohBytes = info.GenerationInfo[3].SizeAfterBytes;
        var lohDelta = lohBytes - _prevLohBytes;
        var handleCount = proc.HandleCount;
        var handleDelta = handleCount - _prevHandleCount;

        _prevLohBytes = lohBytes;
        _prevHandleCount = handleCount;

        var (diskFiles, diskBytes) = GetDiskCacheInfo();

        // TCP connections
        int tcpCount = 0;
        try
        {
            tcpCount = System.Net.NetworkInformation.IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Length;
        }
        catch { /* не критично */ }

        var sb = new StringBuilder();
        sb.AppendLine($"\n{'=',80}");
        sb.AppendLine($"[MEMORY] {label}");

        // Process
        sb.AppendLine($"  WorkingSet:        {proc.WorkingSet64 / 1024 / 1024} MB");
        sb.AppendLine($"  PrivateMemory:     {proc.PrivateMemorySize64 / 1024 / 1024} MB");
        sb.AppendLine($"  Handles:           {handleCount} (delta: {handleDelta:+#;-#;0})");
        sb.AppendLine($"  TCP connections:   {tcpCount}");

        // GC managed
        sb.AppendLine($"  GC TotalMemory:    {totalMemory / 1024 / 1024} MB");
        sb.AppendLine($"  GC TotalAllocated: {totalAllocated / 1024 / 1024} MB");
        sb.AppendLine($"  Gen0 collections:  {GC.CollectionCount(0)}");
        sb.AppendLine($"  Gen1 collections:  {GC.CollectionCount(1)}");
        sb.AppendLine($"  Gen2 collections:  {GC.CollectionCount(2)}");

        // Heap breakdown
        sb.AppendLine($"  HeapSize:          {info.HeapSizeBytes / 1024 / 1024} MB");
        sb.AppendLine($"  FragmentedBytes:   {info.FragmentedBytes / 1024 / 1024} MB");
        sb.AppendLine($"  Gen0Size:          {info.GenerationInfo[0].SizeAfterBytes / 1024 / 1024} MB");
        sb.AppendLine($"  Gen1Size:          {info.GenerationInfo[1].SizeAfterBytes / 1024 / 1024} MB");
        sb.AppendLine($"  Gen2Size:          {info.GenerationInfo[2].SizeAfterBytes / 1024 / 1024} MB");

        // LOH с дельтой
        var lohDeltaStr = lohDelta == 0 ? "±0" : $"{lohDelta / 1024:+#;-#;0} KB";
        sb.AppendLine($"  LOHSize:           {lohBytes / 1024 / 1024} MB  ({lohDeltaStr})");
        if (lohDelta > 1024 * 1024)
            sb.AppendLine($"  ⚠️ LOH вырос на {lohDelta / 1024}KB!");

        sb.AppendLine($"  POHSize:           {info.GenerationInfo[4].SizeAfterBytes / 1024 / 1024} MB");
        sb.AppendLine($"  PinnedObjects:     {info.PinnedObjectsCount}");
        sb.AppendLine($"  FinalizationPending:{info.FinalizationPendingCount}");

        // Нативная дельта
        var nativeDelta = proc.WorkingSet64 - totalMemory;
        sb.AppendLine($"  Нативная (delta):  {nativeDelta / 1024 / 1024} MB");

        // ImageLoader
        sb.AppendLine($"  [ImageRAMCache]    items={_imgRamCount} size={_imgRamBytes / 1024}KB " +
                      $"largeSkipped={_imgLargeSkipped} diskHits={_imgDiskHits} netFetches={_imgNetworkFetches}");
        sb.AppendLine($"  [ImageDiskCache]   files={diskFiles} size={diskBytes / 1024 / 1024}MB");

        // Bitmap
        sb.AppendLine($"  [Bitmap]           created={_bitmapCreated} disposed={_bitmapDisposed} alive={AliveBitmaps}");

        // RemoteImage
        sb.AppendLine($"  [RemoteImage]      started={_loadStarted} cancelled={_loadCancelled} " +
                      $"completed={_loadCompleted} inflight={_loadStarted - _loadCancelled - _loadCompleted}");

        // ViewModels
        sb.AppendLine($"  [ChatVM]           alive={_chatVmAlive} created={_chatVmCreated} disposed={_chatVmDisposed}");
        sb.AppendLine($"  [MessageVM]        alive={_messageVmAlive} finalized={_messageVmFinalized}");
        sb.AppendLine($"  [RichTextBlock]    alive={_richTextBlockAlive}");

        Debug.WriteLine(sb.ToString());
    }

    public static void DumpDetailed(string label, bool runLohExperiment = false)
    {
        ForceFullGc();

        var proc = Process.GetCurrentProcess();
        proc.Refresh();

        var info = GC.GetGCMemoryInfo();
        var managed = GC.GetTotalMemory(false);
        var working = proc.WorkingSet64;

        var sb = new StringBuilder();
        sb.AppendLine($"\n=== {label} ===");

        sb.AppendLine($"  Gen0:    {info.GenerationInfo[0].SizeAfterBytes / 1024 / 1024} MB");
        sb.AppendLine($"  Gen1:    {info.GenerationInfo[1].SizeAfterBytes / 1024 / 1024} MB");
        sb.AppendLine($"  Gen2:    {info.GenerationInfo[2].SizeAfterBytes / 1024 / 1024} MB");
        sb.AppendLine($"  LOH:     {info.GenerationInfo[3].SizeAfterBytes / 1024}KB");
        sb.AppendLine($"  POH:     {info.GenerationInfo[4].SizeAfterBytes / 1024 / 1024} MB");
        sb.AppendLine($"  GC managed total: {managed / 1024 / 1024} MB");
        sb.AppendLine($"  WorkingSet:       {working / 1024 / 1024} MB");
        sb.AppendLine($"  Нативная (delta): {(working - managed) / 1024 / 1024} MB");
        sb.AppendLine($"  Handles:          {proc.HandleCount}");
        sb.AppendLine($"  Pinned objects:   {info.PinnedObjectsCount}");
        sb.AppendLine($"  Finalization q:   {info.FinalizationPendingCount}");

        sb.AppendLine($"  [ChatVM]          alive={_chatVmAlive}");
        sb.AppendLine($"  [MessageVM]       alive={_messageVmAlive}");
        sb.AppendLine($"  [Bitmap]          alive={AliveBitmaps} (created={_bitmapCreated} disposed={_bitmapDisposed})");
        sb.AppendLine($"  [RemoteImg]       started={_loadStarted} cancelled={_loadCancelled} " +
                      $"completed={_loadCompleted} inflight={_loadStarted - _loadCancelled - _loadCompleted}");
        sb.AppendLine($"  [ImageRAMCache]   items={_imgRamCount} size={_imgRamBytes / 1024}KB largeSkipped={_imgLargeSkipped}");

        if (runLohExperiment)
            ExperimentClearImageCacheAndMeasureLoh(sb);

        Debug.WriteLine(sb.ToString());
    }

    #endregion

    #region Reset

    public static void ResetCounters()
    {
        _chatVmAlive = 0;
        _chatVmCreated = 0;
        _chatVmDisposed = 0;
        _messageVmAlive = 0;
        _messageVmFinalized = 0;
        _bitmapCreated = 0;
        _bitmapDisposed = 0;
        _loadStarted = 0;
        _loadCancelled = 0;
        _loadCompleted = 0;
        _imgRamCount = 0;
        _imgRamBytes = 0;
        _imgLargeSkipped = 0;
        _imgDiskHits = 0;
        _imgNetworkFetches = 0;
        _prevLohBytes = 0;
        _prevHandleCount = 0;
    }

    #endregion
}