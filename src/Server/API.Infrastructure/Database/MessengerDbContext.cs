using API.Infrastructure.Database.Configuration;
using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace API.Infrastructure.Database;

public partial class MessengerDbContext(DbContextOptions<MessengerDbContext> options)
    : DbContext(options)
{
    public virtual DbSet<Message> Messages { get; set; }
    public virtual DbSet<UserMessage> UserMessages { get; set; }
    public virtual DbSet<SystemMessage> SystemMessages { get; set; }
    public virtual DbSet<Chat> Chats { get; set; }
    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
    public virtual DbSet<ChatMember> ChatMembers { get; set; }
    public virtual DbSet<Department> Departments { get; set; }
    public virtual DbSet<VoiceMessage> VoiceMessages { get; set; }
    public virtual DbSet<MessageFile> MessageFiles { get; set; }
    public virtual DbSet<Poll> Polls { get; set; }
    public virtual DbSet<PollOption> PollOptions { get; set; }
    public virtual DbSet<PollVote> PollVotes { get; set; }
    public virtual DbSet<SystemSetting> SystemSettings { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<UserSetting> UserSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.RegisterPostgresEnums();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessengerDbContext).Assembly);
    }
}