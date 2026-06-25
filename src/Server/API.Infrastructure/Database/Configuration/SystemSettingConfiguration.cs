using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace API.Infrastructure.Database.Configuration;

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(e => e.Key).HasName("system_settings_pkey");
        builder.ToTable("system_settings");

        builder.Property(e => e.Key).HasMaxLength(50).HasColumnName("key");
        builder.Property(e => e.Value).HasColumnName("value");
    }
}