using Microsoft.EntityFrameworkCore;
using Shared.Enum;

namespace API.Infrastructure.Database.Configuration;

internal static class PostgresEnumExtensions
{
    internal static ModelBuilder RegisterPostgresEnums(this ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum<ChatRole>(name: "chat_role", nameTranslator: EnumTypeMappings.ChatRoleNameTranslator)
            .HasPostgresEnum<ChatType>(name: "chat_type", nameTranslator: EnumTypeMappings.ChatTypeNameTranslator)
            .HasPostgresEnum<Theme>(name: "theme", nameTranslator: EnumTypeMappings.ThemeNameTranslator)
            .HasPostgresEnum<SystemEventType>(name: "system_event_type", nameTranslator: EnumTypeMappings.SystemEventTypeNameTranslator)
            .HasPostgresEnum<UserStatusType>(name: "user_status_type", nameTranslator: EnumTypeMappings.UserStatusTypeNameTranslator);

        return modelBuilder;
    }
}