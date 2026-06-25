using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class VoiceMessageConfiguration : IEntityTypeConfiguration<VoiceMessage>
{
    public void Configure(EntityTypeBuilder<VoiceMessage> builder)
    {
        builder.HasKey(e => e.MessageId).HasName("voice_messages_pkey");
        builder.ToTable("voice_messages");

        builder.Property(e => e.MessageId).ValueGeneratedNever().HasColumnName("message_id");
        builder.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(e => e.Waveform).HasColumnName("waveform");
        builder.Property(e => e.FilePath).HasColumnName("file_path");
        builder.Property(e => e.FileSize).HasColumnName("file_size");

        builder.HasOne(d => d.Message).WithOne(p => p.VoiceMessage).HasForeignKey<VoiceMessage>(d => d.MessageId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("VoiceMessages_MessageId_fkey");
    }
}