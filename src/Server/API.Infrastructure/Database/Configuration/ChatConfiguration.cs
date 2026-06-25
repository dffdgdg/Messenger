using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.HasKey(e => e.Id).HasName("chats_pkey");
        builder.ToTable("chats");

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.Avatar).HasColumnName("avatar");
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnType("timestamp without time zone").HasColumnName("created_at");
        builder.Property(e => e.CreatedById).HasColumnName("created_by_id");
        builder.Property(e => e.LastMessageTime).HasColumnType("timestamp without time zone").HasColumnName("last_message_time");
        builder.Property(e => e.Name).HasMaxLength(100).HasColumnName("name");
        builder.Property(e => e.Type).HasColumnName("type").HasColumnType("chat_type");
        builder.Property(e => e.ShowHistoryForNewMembers).HasColumnName("show_history_for_new_members").HasDefaultValue(true);

        builder.HasOne(d => d.CreatedBy).WithMany(p => p.CreatedChats).HasForeignKey(d => d.CreatedById).OnDelete(DeleteBehavior.Cascade).HasConstraintName("Chats_CreatedById_fkey");
    }
}