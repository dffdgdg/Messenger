using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> builder)
    {
        builder.HasKey(e => new { e.ChatId, e.UserId }).HasName("chat_members_pkey");
        builder.ToTable("chat_members");

        builder.HasIndex(e => e.LastReadMessageId, "idx_chat_members_last_read_message_id");
        builder.HasIndex(e => e.UserId, "idx_chat_members_user_id");

        builder.Property(e => e.ChatId).HasColumnName("chat_id");
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.JoinedAt).HasDefaultValueSql("now()").HasColumnType("timestamp without time zone").HasColumnName("joined_at");
        builder.Property(e => e.LastReadAt).HasColumnType("timestamp without time zone").HasColumnName("last_read_at");
        builder.Property(e => e.LastReadMessageId).HasColumnName("last_read_message_id");
        builder.Property(e => e.NotificationsEnabled).HasDefaultValue(true).HasColumnName("notifications_enabled");
        builder.Property(e => e.Role).HasColumnName("role").HasColumnType("chat_role");

        builder.HasOne(d => d.Chat).WithMany(p => p.ChatMembers).HasForeignKey(d => d.ChatId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("ChatMembers_ChatId_fkey");
        builder.HasOne(d => d.LastReadMessage).WithMany(p => p.ChatMembers).HasForeignKey(d => d.LastReadMessageId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("ChatMembers_LastReadMessageId_fkey");
        builder.HasOne(d => d.User).WithMany(p => p.ChatMembers).HasForeignKey(d => d.UserId).HasConstraintName("ChatMembers_UserId_fkey");
    }
}