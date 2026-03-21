using MessengerAPI.Services.Infrastructure.Postgres;
using MessengerShared.Enum;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Model;

public partial class MessengerDbContext : DbContext
{
    public MessengerDbContext()
    {
    }

    public MessengerDbContext(DbContextOptions<MessengerDbContext> options) : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }
    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
    public virtual DbSet<ChatMember> ChatMembers { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Message> Messages { get; set; }
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
        modelBuilder
            .HasPostgresEnum<ChatRole>(name: "chat_role", nameTranslator: (Npgsql.INpgsqlNameTranslator?)EnumTypeMappings.ChatRoleNameTranslator)
            .HasPostgresEnum<ChatType>(name: "chat_type", nameTranslator: (Npgsql.INpgsqlNameTranslator?)EnumTypeMappings.ChatTypeNameTranslator)
            .HasPostgresEnum<Theme>("theme")
            .HasPostgresEnum<SystemEventType>("system_event_type")
            .HasPostgresEnum<TranscriptionStatus>(name: "transcription_status", nameTranslator: (Npgsql.INpgsqlNameTranslator?)EnumTypeMappings.TranscriptionStatusNameTranslator);

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("chats_pkey");

            entity.ToTable("chats");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"Chats_Id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.Avatar).HasColumnName("avatar");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id");
            entity.Property(e => e.LastMessageTime)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_message_time");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");

            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasColumnType("chat_type");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.Chats)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("Chats_CreatedById_fkey");
        });

        modelBuilder.Entity<ChatMember>(entity =>
        {
            entity.HasKey(e => new { e.ChatId, e.UserId }).HasName("chat_members_pkey");

            entity.ToTable("chat_members");

            entity.HasIndex(e => new { e.ChatId, e.UserId }, "UQ_Chat_User").IsUnique();

            entity.HasIndex(e => e.LastReadMessageId, "idx_chat_members_last_read_message_id");

            entity.HasIndex(e => e.UserId, "idx_chat_members_user_id");

            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("joined_at");
            entity.Property(e => e.LastReadAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_read_at");
            entity.Property(e => e.LastReadMessageId).HasColumnName("last_read_message_id");
            entity.Property(e => e.NotificationsEnabled)
                .HasDefaultValue(true)
                .HasColumnName("notifications_enabled");

            entity.Property(e => e.Role)
                .HasColumnName("role")
                .HasColumnType("chat_role");

            entity.HasOne(d => d.Chat).WithMany(p => p.ChatMembers)
                .HasForeignKey(d => d.ChatId)
                .HasConstraintName("ChatMembers_ChatId_fkey");

            entity.HasOne(d => d.LastReadMessage).WithMany(p => p.ChatMembers)
                .HasForeignKey(d => d.LastReadMessageId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ChatMembers_LastReadMessageId_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ChatMembers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("ChatMembers_UserId_fkey");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("departments_pkey");

            entity.ToTable("departments");

            entity.HasIndex(e => e.ChatId, "Departments_ChatId_key").IsUnique();

            entity.HasIndex(e => e.HeadId, "idx_departments_head_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"Departments_Id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.HeadId).HasColumnName("head_id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.ParentDepartmentId).HasColumnName("parent_department_id");

            entity.HasOne(d => d.Chat).WithOne(p => p.Department)
                .HasForeignKey<Department>(d => d.ChatId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Departments_ChatId_fkey");

            entity.HasOne(d => d.Head).WithMany(p => p.Departments)
                .HasForeignKey(d => d.HeadId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Departments_Head_fkey");

            entity.HasOne(d => d.ParentDepartment).WithMany(p => p.InverseParentDepartment)
                .HasForeignKey(d => d.ParentDepartmentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Departments_Parent_fkey");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("messages_pkey");

            entity.ToTable("messages");

            entity.HasIndex(e => new { e.ChatId, e.CreatedAt }, "idx_messages_chatid_createdat");
            entity.HasIndex(e => e.ReplyToMessageId, "idx_messages_reply_to_message_id");
            entity.HasIndex(e => e.ForwardedFromMessageId, "idx_messages_forwarded_from_message_id");
            entity.HasIndex(e => e.TargetUserId, "idx_messages_target_user_id");

            entity.Property(e => e.IsSystemMessage).HasColumnName("is_system_message")
            .HasDefaultValue(false);

            entity.Property(e => e.SystemEventType).HasColumnName("system_event_type").HasColumnType("system_event_type");

            entity.Property(e => e.TargetUserId).HasColumnName("target_user_id");

            entity.HasOne(d => d.TargetUser).WithMany().HasForeignKey(d => d.TargetUserId)
            .OnDelete(DeleteBehavior.SetNull).HasConstraintName("Messages_TargetUserId_fkey");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"Messages_Id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.ChatId).HasColumnName("chat_id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EditedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("edited_at");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");
            entity.Property(e => e.ReplyToMessageId).HasColumnName("reply_to_message_id");
            entity.Property(e => e.ForwardedFromMessageId).HasColumnName("forwarded_from_message_id");

            entity.HasOne(d => d.Chat).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ChatId)
                .HasConstraintName("Messages_ChatId_fkey");

            entity.HasOne(d => d.Sender).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SenderId)
                .HasConstraintName("Messages_SenderId_fkey");

            entity.HasOne(d => d.ReplyToMessage).WithMany(p => p.InverseReplyToMessage)
                .HasForeignKey(d => d.ReplyToMessageId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Messages_ReplyToMessageId_fkey");

            entity.HasOne(d => d.ForwardedFromMessage).WithMany(p => p.InverseForwardedFromMessage)
                .HasForeignKey(d => d.ForwardedFromMessageId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Messages_ForwardedFromMessageId_fkey");
        });

        modelBuilder.Entity<VoiceMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("voice_messages_pkey");

            entity.ToTable("voice_messages");

            entity.Property(e => e.MessageId)
                .ValueGeneratedNever()
                .HasColumnName("message_id");

            entity.Property(e => e.DurationSeconds)
                .HasColumnName("duration_seconds");

            entity.Property(e => e.TranscriptionStatus)
                .HasColumnName("transcription_status")
                .HasColumnType("transcription_status")
                .HasDefaultValue(TranscriptionStatus.Pending);

            entity.Property(e => e.TranscriptionText)
                .HasColumnName("transcription_text");

            entity.Property(e => e.FilePath)
                .HasColumnName("file_path");

            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");

            entity.Property(e => e.ContentType)
                .HasMaxLength(100)
                .HasDefaultValue("audio/wav")
                .HasColumnName("content_type");

            entity.Property(e => e.FileSize)
                .HasColumnName("file_size");

            entity.HasOne(d => d.Message)
                .WithOne(p => p.VoiceMessage)
                .HasForeignKey<VoiceMessage>(d => d.MessageId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("VoiceMessages_MessageId_fkey");
        });

        modelBuilder.Entity<MessageFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("message_files_pkey");

            entity.ToTable("message_files");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"MessageFiles_id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.ContentType)
                .HasMaxLength(100)
                .HasColumnName("content_type");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.Path).HasColumnName("path");

            entity.HasOne(d => d.Message).WithMany(p => p.MessageFiles)
                .HasForeignKey(d => d.MessageId)
                .HasConstraintName("MessageFiles_MessageId_fkey");
        });

        modelBuilder.Entity<Poll>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("polls_pkey");

            entity.ToTable("polls");

            entity.HasIndex(e => e.MessageId, "idx_polls_message_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"Polls_Id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.AllowsMultipleAnswers)
                .HasDefaultValue(false)
                .HasColumnName("allows_multiple_answers");
            entity.Property(e => e.ClosesAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("closes_at");
            entity.Property(e => e.IsAnonymous)
                .HasDefaultValue(true)
                .HasColumnName("is_anonymous");
            entity.Property(e => e.MessageId).HasColumnName("message_id");

            entity.HasOne(d => d.Message).WithMany(p => p.Polls)
                .HasForeignKey(d => d.MessageId)
                .HasConstraintName("Polls_MessageId_fkey");
        });

        modelBuilder.Entity<PollOption>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("poll_options_pkey");

            entity.ToTable("poll_options");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"PollOptions_Id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.OptionText)
                .HasMaxLength(50)
                .HasColumnName("option_text");
            entity.Property(e => e.PollId).HasColumnName("poll_id");
            entity.Property(e => e.Position).HasColumnName("position");

            entity.HasOne(d => d.Poll).WithMany(p => p.PollOptions)
                .HasForeignKey(d => d.PollId)
                .HasConstraintName("PollOptions_PollId_fkey");
        });

        modelBuilder.Entity<PollVote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("poll_votes_pkey");

            entity.ToTable("poll_votes");

            entity.HasIndex(e => new { e.PollId, e.UserId, e.OptionId }, "UQ_Poll_User_Option_Vote").IsUnique();

            entity.HasIndex(e => e.UserId, "idx_poll_votes_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"PollVotes_Id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.OptionId).HasColumnName("option_id");
            entity.Property(e => e.PollId).HasColumnName("poll_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.VotedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("voted_at");

            entity.HasOne(d => d.Option).WithMany(p => p.PollVotes)
                .HasForeignKey(d => d.OptionId)
                .HasConstraintName("PollVotes_OptionId_fkey");

            entity.HasOne(d => d.Poll).WithMany(p => p.PollVotes)
                .HasForeignKey(d => d.PollId)
                .HasConstraintName("PollVotes_PollId_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.PollVotes)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("PollVotes_UserId_fkey");
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("system_settings_pkey");

            entity.ToTable("system_settings");

            entity.Property(e => e.Key)
                .HasMaxLength(50)
                .HasColumnName("key");
            entity.Property(e => e.Value).HasColumnName("value");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.DepartmentId, "idx_users_department_id");

            entity.HasIndex(e => e.Username, "users_username_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"Users_Id_seq\"'::regclass)")
                .HasColumnName("id");
            entity.Property(e => e.Avatar).HasColumnName("avatar");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.IsBanned)
                .HasDefaultValue(false)
                .HasColumnName("is_banned");
            entity.Property(e => e.LastOnline)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("last_online");
            entity.Property(e => e.Midname)
                .HasMaxLength(50)
                .HasColumnName("midname");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.Surname)
                .HasMaxLength(50)
                .HasColumnName("surname");
            entity.Property(e => e.Username)
                .HasMaxLength(32)
                .HasColumnName("username");

            entity.HasOne(d => d.Department).WithMany(p => p.Users)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Users_DepartmentId_fkey");
        });

        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("user_settings_pkey");

            entity.ToTable("user_settings");

            entity.Property(e => e.UserId)
                .ValueGeneratedNever()
                .HasColumnName("user_id");

            entity.Property(e => e.Theme)
                .HasColumnName("theme")
                .HasColumnType("theme");

            entity.Property(e => e.NotificationsEnabled)
                .HasDefaultValue(true)
                .HasColumnName("notifications_enabled");

            entity.HasOne(d => d.User).WithOne(p => p.UserSetting)
                .HasForeignKey<UserSetting>(d => d.UserId)
                .HasConstraintName("UserSettings_UserId_fkey");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("refresh_tokens_pkey");

            entity.ToTable("refresh_tokens");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("nextval('\"RefreshTokens_Id_seq\"'::regclass)")
                .HasColumnName("id");

            entity.Property(e => e.UserId)
                .HasColumnName("user_id");

            entity.Property(e => e.TokenHash)
                .HasMaxLength(128)
                .HasColumnName("token_hash");

            entity.Property(e => e.JwtId)
                .HasMaxLength(64)
                .HasColumnName("jwt_id");

            entity.Property(e => e.CreatedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");

            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");

            entity.Property(e => e.UsedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("used_at");

            entity.Property(e => e.RevokedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("revoked_at");

            entity.Property(e => e.ReplacedByTokenId)
                .HasColumnName("replaced_by_token_id");

            entity.Property(e => e.FamilyId)
                .HasMaxLength(64)
                .HasColumnName("family_id");

            // Indexes
            entity.HasIndex(e => e.TokenHash, "idx_refresh_tokens_token_hash");
            entity.HasIndex(e => e.UserId, "idx_refresh_tokens_user_id");
            entity.HasIndex(e => e.FamilyId, "idx_refresh_tokens_family_id");
            entity.HasIndex(e => e.ExpiresAt, "idx_refresh_tokens_expires_at");

            // Relationships
            entity.HasOne(d => d.User)
                .WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("RefreshTokens_UserId_fkey");

            entity.HasOne(d => d.ReplacedByToken)
                .WithMany()
                .HasForeignKey(d => d.ReplacedByTokenId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("RefreshTokens_ReplacedBy_fkey");
        });
    }
}
