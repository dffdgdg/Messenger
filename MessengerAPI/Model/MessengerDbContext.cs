using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Model;

public partial class MessengerDbContext : DbContext
{
    public MessengerDbContext()
    {
    }

    public MessengerDbContext(DbContextOptions<MessengerDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }

    public virtual DbSet<ChatMember> ChatMembers { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<MessageFile> MessageFiles { get; set; }

    public virtual DbSet<Poll> Polls { get; set; }

    public virtual DbSet<PollOption> PollOptions { get; set; }

    public virtual DbSet<PollVote> PollVotes { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserSetting> UserSettings { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=MessengerDB;Username=postgres;Password=123");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("chat_role", new[] { "member", "admin", "owner" })
            .HasPostgresEnum("chat_type", new[] { "Chat", "Department", "Contact", "contact" })
            .HasPostgresEnum("theme", new[] { "light", "dark", "system" });

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Chats_pkey");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.LastMessageTime).HasColumnType("timestamp without time zone");
            entity.Property(e => e.Name).HasMaxLength(100);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.Chats)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("Chats_CreatedById_fkey");
        });

        modelBuilder.Entity<ChatMember>(entity =>
        {
            entity.HasKey(e => new { e.ChatId, e.UserId }).HasName("ChatMembers_pkey");

            entity.HasIndex(e => new { e.ChatId, e.UserId }, "UQ_Chat_User").IsUnique();

            entity.HasIndex(e => e.LastReadMessageId, "idx_chatmembers_lastreadmessageid");

            entity.HasIndex(e => e.UserId, "idx_chatmembers_userid");

            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.LastReadAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.NotificationsEnabled).HasDefaultValue(true);

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
            entity.HasKey(e => e.Id).HasName("Departments_pkey");

            entity.HasIndex(e => e.ChatId, "Departments_ChatId_key").IsUnique();

            entity.HasIndex(e => e.Head, "idx_departments_head");

            entity.Property(e => e.Name).HasMaxLength(100);

            entity.HasOne(d => d.Chat).WithOne(p => p.Department)
                .HasForeignKey<Department>(d => d.ChatId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Departments_ChatId_fkey");

            entity.HasOne(d => d.HeadNavigation).WithMany(p => p.Departments)
                .HasForeignKey(d => d.Head)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Departments_Head_fkey");

            entity.HasOne(d => d.ParentDepartment).WithMany(p => p.InverseParentDepartment)
                .HasForeignKey(d => d.ParentDepartmentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Departments_Parent_fkey");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Messages_pkey");

            entity.HasIndex(e => new { e.ChatId, e.CreatedAt }, "idx_messages_chatid_createdat");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.EditedAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Chat).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ChatId)
                .HasConstraintName("Messages_ChatId_fkey");

            entity.HasOne(d => d.Sender).WithMany(p => p.Messages)
                .HasForeignKey(d => d.SenderId)
                .HasConstraintName("Messages_SenderId_fkey");
        });

        modelBuilder.Entity<MessageFile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("MessageFiles_pkey");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.FileName).HasMaxLength(255);

            entity.HasOne(d => d.Message).WithMany(p => p.MessageFiles)
                .HasForeignKey(d => d.MessageId)
                .HasConstraintName("MessageFiles_MessageId_fkey");
        });

        modelBuilder.Entity<Poll>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Polls_pkey");

            entity.HasIndex(e => e.MessageId, "idx_polls_chatid");

            entity.Property(e => e.AllowsMultipleAnswers).HasDefaultValue(false);
            entity.Property(e => e.ClosesAt).HasColumnType("timestamp without time zone");
            entity.Property(e => e.IsAnonymous).HasDefaultValue(true);
            entity.Property(e => e.Question).HasMaxLength(75);

            entity.HasOne(d => d.Message).WithMany(p => p.Polls)
                .HasForeignKey(d => d.MessageId)
                .HasConstraintName("Polls_MessageId_fkey");
        });

        modelBuilder.Entity<PollOption>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PollOptions_pkey");

            entity.Property(e => e.OptionText).HasMaxLength(50);

            entity.HasOne(d => d.Poll).WithMany(p => p.PollOptions)
                .HasForeignKey(d => d.PollId)
                .HasConstraintName("PollOptions_PollId_fkey");
        });

        modelBuilder.Entity<PollVote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PollVotes_pkey");

            entity.HasIndex(e => new { e.PollId, e.UserId, e.OptionId }, "UQ_Poll_User_Option_Vote").IsUnique();

            entity.HasIndex(e => e.UserId, "idx_pollvotes_userid");

            entity.Property(e => e.VotedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");

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

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Users_pkey");

            entity.HasIndex(e => e.Username, "Users_Username_key").IsUnique();

            entity.HasIndex(e => e.Department, "idx_users_department");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.IsBanned).HasDefaultValue(false);
            entity.Property(e => e.LastOnline).HasColumnType("timestamp without time zone");
            entity.Property(e => e.Midname).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.Surname).HasMaxLength(50);
            entity.Property(e => e.Username).HasMaxLength(32);

            entity.HasOne(d => d.DepartmentNavigation).WithMany(p => p.Users)
                .HasForeignKey(d => d.Department)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("Users_DepartmentId_fkey");
        });

        modelBuilder.Entity<UserSetting>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("UserSettings_pkey");

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.NotificationsEnabled).HasDefaultValue(true);

            entity.HasOne(d => d.User).WithOne(p => p.UserSetting)
                .HasForeignKey<UserSetting>(d => d.UserId)
                .HasConstraintName("UserSettings_UserId_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
