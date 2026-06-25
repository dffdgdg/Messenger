using API.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Enum;

namespace API.Infrastructure.Database.Configuration;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(e => e.Id).HasName("users_pkey");
        builder.ToTable("users");

        builder.HasIndex(e => e.DepartmentId, "idx_users_department_id");
        builder.HasIndex(e => e.Username, "users_username_key").IsUnique();

        builder.Property(e => e.Id).ValueGeneratedOnAdd().HasColumnName("id");
        builder.Property(e => e.Avatar).HasColumnName("avatar");
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnType("timestamp without time zone").HasColumnName("created_at");
        builder.Property(e => e.DepartmentId).HasColumnName("department_id");
        builder.Property(e => e.IsBanned).HasDefaultValue(false).HasColumnName("is_banned");
        builder.Property(e => e.LastOnline).HasColumnType("timestamp without time zone").HasColumnName("last_online");
        builder.Property(e => e.Midname).HasMaxLength(50).HasColumnName("midname");
        builder.Property(e => e.Name).HasMaxLength(50).HasColumnName("name");
        builder.Property(e => e.Surname).HasMaxLength(50).HasColumnName("surname");
        builder.Property(e => e.Username).HasMaxLength(32).HasColumnName("username");
        builder.Property(e => e.StatusType).HasColumnName("status_type").HasDefaultValue(UserStatusType.Online);
        builder.Property(e => e.StatusExpiresAt).HasColumnType("timestamp without time zone").HasColumnName("status_expires_at");

        builder.HasOne(d => d.Department).WithMany(p => p.Users).HasForeignKey(d => d.DepartmentId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("Users_DepartmentId_fkey");

        builder.OwnsOne(u => u.Password, pw => pw.Property(p => p.Hash).HasColumnName("password_hash"));
    }
}

internal sealed class UserSettingConfiguration : IEntityTypeConfiguration<UserSetting>
{
    public void Configure(EntityTypeBuilder<UserSetting> builder)
    {
        builder.HasKey(e => e.UserId).HasName("user_settings_pkey");
        builder.ToTable("user_settings");

        builder.Property(e => e.UserId).ValueGeneratedNever().HasColumnName("user_id");
        builder.Property(e => e.Theme).HasColumnName("theme").HasColumnType("theme");
        builder.Property(e => e.NotificationsEnabled).HasDefaultValue(true).HasColumnName("notifications_enabled");

        builder.HasOne(d => d.User).WithOne(p => p.UserSetting).HasForeignKey<UserSetting>(d => d.UserId).HasConstraintName("UserSettings_UserId_fkey");
    }
}