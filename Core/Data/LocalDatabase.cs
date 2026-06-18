using Core.Data.Models.Cache;
using Core.Data.Models.Sync;
using SQLite;
using System;
using System.Diagnostics;
using System.Threading;

namespace Core.Data;

public sealed class LocalDatabase : IAsyncDisposable, IDisposable
{
    private const int SchemaVersion = 2;

    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public LocalDatabase(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentNullException(nameof(dbPath));

        _db = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

        Debug.WriteLine($"[LocalDB] Path: {dbPath}");
    }

    public SQLiteAsyncConnection Connection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(LocalDatabase));
            return _db;
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var sw = Stopwatch.StartNew();

            await _db.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL");
            await _db.ExecuteScalarAsync<int>("PRAGMA synchronous=NORMAL");
            await _db.ExecuteScalarAsync<int>("PRAGMA cache_size=-4000");
            await _db.ExecuteScalarAsync<int>("PRAGMA temp_store=MEMORY");
            await _db.ExecuteScalarAsync<long>("PRAGMA mmap_size=33554432");

            Debug.WriteLine("[LocalDB] PRAGMAs set");

            await MigrateAsync();

            await _db.CreateTableAsync<CachedMessage>();
            await _db.CreateTableAsync<CachedChat>();
            await _db.CreateTableAsync<CachedUser>();
            await _db.CreateTableAsync<CachedReadPointer>();
            await _db.CreateTableAsync<ChatSyncState>();
            await _db.CreateTableAsync<CachedDownloadedFile>();

            Debug.WriteLine("[LocalDB] Tables created");

            await CreateIndexesAsync();

            _initialized = true;
            sw.Stop();
            Debug.WriteLine($"[LocalDB] Initialized in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalDB] Initialization FAILED: {ex}");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task MigrateAsync()
    {
        var currentVersion = await _db.ExecuteScalarAsync<int>("PRAGMA user_version");
        Debug.WriteLine($"[LocalDB] Schema version: {currentVersion}, expected: {SchemaVersion}");

        if (currentVersion == SchemaVersion)
            return;

        if (currentVersion > SchemaVersion)
        {
            Debug.WriteLine("[LocalDB] Schema downgrade detected, clearing all data");
            await DropAllTablesAsync();
        }

        if (currentVersion < 1)
        {
            Debug.WriteLine("[LocalDB] Migrating to schema v1 (initial)");
        }

        if (currentVersion is >= 1 and < 4)
        {
            Debug.WriteLine("[LocalDB] Migrating to schema v4: adding is_pinned to messages");
            try
            {
                await _db.ExecuteAsync(
                    "ALTER TABLE messages ADD COLUMN is_pinned INTEGER NOT NULL DEFAULT 0");
                Debug.WriteLine("[LocalDB] Column is_pinned added");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalDB] ALTER TABLE messages (non-critical): {ex.Message}");
            }
        }

        if (currentVersion < 2)
        {
            Debug.WriteLine("[LocalDB] Migrating to schema v2: adding downloaded_files table");
            try
            {
                await _db.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS downloaded_files (
                    file_id       INTEGER PRIMARY KEY,
                    message_id    INTEGER NOT NULL,
                    local_path    TEXT    NOT NULL,
                    file_name     TEXT    NOT NULL,
                    file_size     INTEGER NOT NULL DEFAULT 0,
                    downloaded_at INTEGER NOT NULL,
                    content_type  TEXT    NOT NULL DEFAULT ''
                )
                """);

                await _db.ExecuteAsync(
                    "CREATE INDEX IF NOT EXISTS idx_downloaded_files_message ON downloaded_files(message_id)");

                Debug.WriteLine("[LocalDB] downloaded_files table created");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalDB] Migration v2 failed (non-critical): {ex.Message}");
            }
        }

        await _db.ExecuteAsync($"PRAGMA user_version = {SchemaVersion}");
        Debug.WriteLine($"[LocalDB] Schema updated to v{SchemaVersion}");
    }

    private async Task DropAllTablesAsync() => await _db.RunInTransactionAsync(conn =>
    {
        conn.Execute("DROP TABLE IF EXISTS messages");
        conn.Execute("DROP TABLE IF EXISTS chats");
        conn.Execute("DROP TABLE IF EXISTS users");
        conn.Execute("DROP TABLE IF EXISTS read_pointers");
        conn.Execute("DROP TABLE IF EXISTS chat_sync_state");
        conn.Execute("DROP TABLE IF EXISTS messages_fts");
        conn.Execute("DROP TABLE IF EXISTS downloaded_files");
    });

    private async Task CreateIndexesAsync() => await _db.RunInTransactionAsync(conn =>
    {
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_msg_chat_id ON messages(chat_id, id DESC)");
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_msg_chat_id_asc ON messages(chat_id, id ASC)");
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_chats_last_msg ON chats(last_message_date DESC)");
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_chats_type_date ON chats(type, last_message_date DESC)");
        conn.Execute("CREATE INDEX IF NOT EXISTS idx_messages_chat_id ON messages (chat_id, id DESC)");
    });

    public async Task ClearAllAsync()
    {
        await _db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM messages");
            conn.Execute("DELETE FROM chats");
            conn.Execute("DELETE FROM users");
            conn.Execute("DELETE FROM read_pointers");
            conn.Execute("DELETE FROM chat_sync_state");
            conn.Execute("DELETE FROM downloaded_files");
        });
        Debug.WriteLine("[LocalDB] All data cleared");
    }

    public async Task VacuumAsync()
    {
        var sizeBefore = await GetDatabaseSizeBytesAsync();
        await _db.ExecuteAsync("VACUUM");
        var sizeAfter = await GetDatabaseSizeBytesAsync();
        Debug.WriteLine($"[LocalDB] VACUUM: {sizeBefore / 1024}KB → {sizeAfter / 1024}KB");
    }

    public async Task<long> GetDatabaseSizeBytesAsync()
    {
        var pageCount = await _db.ExecuteScalarAsync<long>("PRAGMA page_count");
        var pageSize = await _db.ExecuteScalarAsync<long>("PRAGMA page_size");
        return pageCount * pageSize;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _db.GetConnection().Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalDB] Dispose error: {ex.Message}");
        }
        _initLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _db.CloseAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalDB] DisposeAsync error: {ex.Message}");
        }
        _initLock.Dispose();
    }
}