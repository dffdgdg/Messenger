namespace API.Services.Infrastructure.Database;

public static class EnumTypeMappings
{
    public static EnumNameTranslator ChatRoleNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    { [nameof(ChatRole.Member)] = "member", [nameof(ChatRole.Admin)] = "admin", [nameof(ChatRole.Owner)] = "owner" });

    public static EnumNameTranslator ChatTypeNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    { [nameof(ChatType.Chat)] = "chat", [nameof(ChatType.Department)] = "department", [nameof(ChatType.Contact)] = "contact", [nameof(ChatType.DepartmentHeads)] = "department_heads" });
    public static EnumNameTranslator UserStatusTypeNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(UserStatusType.Online)] = "online",
        [nameof(UserStatusType.Away)] = "away",
        [nameof(UserStatusType.DoNotDisturb)] = "do_not_disturb",
        [nameof(UserStatusType.Busy)] = "busy"
    });

}