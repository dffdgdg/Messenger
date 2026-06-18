using SQLite;

namespace Core.Data.Models.Cache;

[Table("downloaded_files")]
public class CachedDownloadedFile
{
    [PrimaryKey]
    [Column("file_id")]
    public int FileId { get; set; }

    [Indexed]
    [Column("message_id")]
    public int MessageId { get; set; }

    [Column("local_path")]
    public string LocalPath { get; set; } = "";

    [Column("file_name")]
    public string FileName { get; set; } = "";

    [Column("file_size")]
    public long FileSize { get; set; }

    [Column("downloaded_at")]
    public long DownloadedAtTicks { get; set; }

    [Column("content_type")]
    public string ContentType { get; set; } = "";

    [Ignore]
    public DateTime DownloadedAt => new(DownloadedAtTicks, DateTimeKind.Utc);
}