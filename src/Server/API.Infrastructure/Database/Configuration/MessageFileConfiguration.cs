using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class MessageFileConfiguration : IEntityTypeConfiguration<MessageFile>
{
    public void Configure(EntityTypeBuilder<MessageFile> builder)
    {
        builder.HasKey(e => e.Id).HasName("message_files_pkey");
        builder.ToTable("message_files");

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.ContentType).HasMaxLength(100).HasColumnName("content_type");
        builder.Property(e => e.FileName).HasMaxLength(255).HasColumnName("file_name");
        builder.Property(e => e.MessageId).HasColumnName("message_id");
        builder.Property(e => e.Path).HasColumnName("path");
    }
}