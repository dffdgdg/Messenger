using SQLite;

namespace MessengerDesktop.Data.Entities;

[Table("users")]
public class CachedUser
{
    [PrimaryKey]
    [Column("id")]
    public int Id { get; set; }

    [Column("username")]
    public string? Username { get; set; }

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("avatar")]
    public string? Avatar { get; set; }

    [Column("cached_at")]
    public long CachedAtTicks { get; set; }
}