using MessengerDesktop.Data.Entities;
using SQLite;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Data;

public sealed class LocalDatabase : IAsyncDisposable, IDisposable
{
    private const int SchemaVersion = 1;

    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public LocalDatabase(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentNullException(nameof(dbPath));

        _db = new SQLiteAsyncConnection(dbPath,SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

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

            // PRAGMA через ExecuteScalarAsync — они возвращают значение, не "affected rows"
            await _db.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL");
            await _db.ExecuteScalarAsync<int>("PRAGMA synchronous=NORMAL");
            await _db.ExecuteScalarAsync<int>("PRAGMA cache_size=-8000");
            await _db.ExecuteScalarAsync<int>("PRAGMA temp_store=MEMORY");
            await _db.ExecuteScalarAsync<long>("PRAGMA mmap_size=268435456");

            Debug.WriteLine("[LocalDB] PRAGMAs set");

            // Миграции
            await MigrateAsync();

            // Создание таблиц (IF NOT EXISTS)
            await _db.CreateTableAsync<CachedMessage>();
            await _db.CreateTableAsync<CachedChat>();
            await _db.CreateTableAsync<CachedUser>();
            await _db.CreateTableAsync<CachedReadPointer>();
            await _db.CreateTableAsync<ChatSyncState>();

            Debug.WriteLine("[LocalDB] Tables created");

            // Индексы
            await CreateIndexesAsync();

            // FTS5
            await CreateFtsAsync();

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
            // Таблицы создаются ниже через CreateTableAsync
        }

        // PRAGMA user_version не возвращает значение при SET — используем Execute через connection напрямую
        await _db.RunInTransactionAsync(conn => conn.Execute($"PRAGMA user_version = {SchemaVersion}"));

        Debug.WriteLine($"[LocalDB] Schema updated to v{SchemaVersion}");
    }

    private async Task DropAllTablesAsync()
    {
        await _db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DROP TABLE IF EXISTS messages");
            conn.Execute("DROP TABLE IF EXISTS chats");
            conn.Execute("DROP TABLE IF EXISTS users");
            conn.Execute("DROP TABLE IF EXISTS read_pointers");
            conn.Execute("DROP TABLE IF EXISTS chat_sync_state");
            conn.Execute("DROP TABLE IF EXISTS messages_fts");
        });
    }

    private async Task CreateIndexesAsync()
    {
        // Все CREATE INDEX IF NOT EXISTS — DDL, не возвращает affected rows
        // Используем RunInTransactionAsync с синхронным Execute
        await _db.RunInTransactionAsync(conn =>
        {
            conn.Execute(
                "CREATE INDEX IF NOT EXISTS idx_msg_chat_id ON messages(chat_id, id DESC)");

            conn.Execute(
                "CREATE INDEX IF NOT EXISTS idx_msg_chat_id_asc ON messages(chat_id, id ASC)");

            conn.Execute(
                "CREATE INDEX IF NOT EXISTS idx_chats_last_msg ON chats(last_message_date DESC)");

            conn.Execute(
                "CREATE INDEX IF NOT EXISTS idx_chats_type_date ON chats(type, last_message_date DESC)");
        });

        Debug.WriteLine("[LocalDB] Indexes created");
    }

    private async Task CreateFtsAsync()
    {
        // FTS5 и триггеры — DDL, выполняем через синхронный Execute внутри транзакции
        // НО: CREATE VIRTUAL TABLE нельзя в транзакции в SQLite, выполняем отдельно
        try
        {
            // Сначала FTS таблица (вне транзакции)
            await Task.Run(() =>
            {
                var conn = _db.GetConnection();
                using (conn.Lock())
                {
                    conn.Execute(@"
                        CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts 
                        USING fts5(content, content=messages, content_rowid=id)");
                }
            });

            // Триггеры — тоже DDL, выполняем поштучно
            await Task.Run(() =>
            {
                var conn = _db.GetConnection();
                using (conn.Lock())
                {
                    conn.Execute(@"
                        CREATE TRIGGER IF NOT EXISTS trg_msg_fts_ins 
                        AFTER INSERT ON messages 
                        WHEN new.content IS NOT NULL AND new.is_deleted = 0
                        BEGIN
                            INSERT INTO messages_fts(rowid, content) VALUES (new.id, new.content);
                        END");

                    conn.Execute(@"
                        CREATE TRIGGER IF NOT EXISTS trg_msg_fts_upd 
                        AFTER UPDATE OF content, is_deleted ON messages
                        BEGIN
                            INSERT INTO messages_fts(messages_fts, rowid, content) 
                                VALUES('delete', old.id, COALESCE(old.content, ''));
                            INSERT OR IGNORE INTO messages_fts(rowid, content) 
                                SELECT new.id, new.content 
                                WHERE new.content IS NOT NULL AND new.is_deleted = 0;
                        END");

                    conn.Execute(@"
                        CREATE TRIGGER IF NOT EXISTS trg_msg_fts_del 
                        AFTER DELETE ON messages
                        WHEN old.content IS NOT NULL
                        BEGIN
                            INSERT INTO messages_fts(messages_fts, rowid, content) 
                                VALUES('delete', old.id, old.content);
                        END");
                }
            });

            Debug.WriteLine("[LocalDB] FTS5 table + triggers created");
        }
        catch (Exception ex)
        {
            // FTS — не критично. Если SQLite скомпилирован без FTS5,
            // поиск просто будет fallback на LIKE
            Debug.WriteLine($"[LocalDB] FTS5 setup failed (non-critical): {ex.Message}");
        }
    }

    public async Task ClearAllAsync()
    {
        await _db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM messages");
            conn.Execute("DELETE FROM chats");
            conn.Execute("DELETE FROM users");
            conn.Execute("DELETE FROM read_pointers");
            conn.Execute("DELETE FROM chat_sync_state");
        });
        Debug.WriteLine("[LocalDB] All data cleared");
    }

    public async Task VacuumAsync()
    {
        var sizeBefore = await GetDatabaseSizeBytesAsync();
        // VACUUM нельзя в транзакции
        await Task.Run(() =>
        {
            var conn = _db.GetConnection();
            using (conn.Lock())
            {
                conn.Execute("VACUUM");
            }
        });
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