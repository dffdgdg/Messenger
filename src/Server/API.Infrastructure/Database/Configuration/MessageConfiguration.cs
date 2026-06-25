using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(e => e.Id).HasName("messages_pkey");
        builder.ToTable("messages");

        builder.HasDiscriminator<bool>("message_type").HasValue<UserMessage>(false).HasValue<SystemMessage>(true);
        builder.Property<bool>("message_type").HasColumnName("message_type");

        builder.HasIndex(e => new { e.ChatId, e.CreatedAt }, "idx_messages_chatid_createdat");
        builder.HasIndex(e => new { e.ChatId, e.PinnedAt }, "idx_messages_chatid_pinnedat").HasFilter("pinned_at IS NOT NULL");

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.ChatId).HasColumnName("chat_id");
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnType("timestamp without time zone").HasColumnName("created_at");
        builder.Property(e => e.IsDeleted).HasDefaultValue(false).HasColumnName("is_deleted");
        builder.Property(e => e.PinnedAt).HasColumnType("timestamp without time zone").HasColumnName("pinned_at");
        builder.Property(e => e.PinnedByUserId).HasColumnName("pinned_by_user_id");

        builder.HasOne(d => d.Chat).WithMany(p => p.Messages).HasForeignKey(d => d.ChatId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("Messages_ChatId_fkey");
        builder.HasOne(d => d.PinnedByUser).WithMany().HasForeignKey(d => d.PinnedByUserId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Messages_PinnedByUserId_fkey");
    }
}

internal sealed class UserMessageConfiguration : IEntityTypeConfiguration<UserMessage>
{
    public void Configure(EntityTypeBuilder<UserMessage> builder)
    {
        builder.HasIndex(e => e.ReplyToMessageId, "idx_messages_reply_to_message_id");
        builder.HasIndex(e => e.ForwardedFromMessageId, "idx_messages_forwarded_from_message_id");

        builder.Property(e => e.SenderId).HasColumnName("sender_id");
        builder.Property(e => e.Content).HasColumnName("content");
        builder.Property(e => e.EditedAt).HasColumnType("timestamp without time zone").HasColumnName("edited_at");
        builder.Property(e => e.ReplyToMessageId).HasColumnName("reply_to_message_id");
        builder.Property(e => e.ForwardedFromMessageId).HasColumnName("forwarded_from_message_id");

        builder.HasOne(d => d.Sender).WithMany(p => p.SentMessages).HasForeignKey(d => d.SenderId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Messages_SenderId_fkey");
        builder.HasOne(d => d.ReplyToMessage).WithMany(p => p.InverseReplyToMessage).HasForeignKey(d => d.ReplyToMessageId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Messages_ReplyToMessageId_fkey");
        builder.HasOne(d => d.ForwardedFromMessage).WithMany(p => p.InverseForwardedFromMessage).HasForeignKey(d => d.ForwardedFromMessageId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Messages_ForwardedFromMessageId_fkey");
        builder.HasOne(d => d.Poll).WithOne(p => p.Message).HasForeignKey<Poll>(p => p.MessageId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("Polls_MessageId_fkey");
        builder.HasMany(d => d.MessageFiles).WithOne(p => p.Message).HasForeignKey(p => p.MessageId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("MessageFiles_MessageId_fkey");
    }
}

internal sealed class SystemMessageConfiguration : IEntityTypeConfiguration<SystemMessage>
{
    public void Configure(EntityTypeBuilder<SystemMessage> builder)
    {
        builder.HasIndex(e => e.TargetUserId, "idx_messages_target_user_id");

        builder.Property(e => e.InitiatorId).HasColumnName("sender_id");
        builder.Property(e => e.TargetUserId).HasColumnName("target_user_id");
        builder.Property(e => e.SystemEventType).HasColumnName("system_event_type").HasColumnType("system_event_type");
        builder.Property(e => e.Content).HasColumnName("content");

        builder.HasOne(d => d.Initiator).WithMany().HasForeignKey(d => d.InitiatorId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Messages_SenderId_fkey");
        builder.HasOne(d => d.TargetUser).WithMany().HasForeignKey(d => d.TargetUserId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Messages_TargetUserId_fkey");
    }
}