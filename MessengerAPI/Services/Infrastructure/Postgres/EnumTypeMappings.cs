namespace MessengerAPI.Services.Infrastructure.Postgres;

public static class EnumTypeMappings
{
    public static EnumNameTranslator ChatRoleNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    { [nameof(ChatRole.Member)] = "member", [nameof(ChatRole.Admin)] = "admin", [nameof(ChatRole.Owner)] = "owner" });

    public static EnumNameTranslator ChatTypeNameTranslator { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal)
    { [nameof(ChatType.Chat)] = "Chat", [nameof(ChatType.Department)] = "Department", [nameof(ChatType.Contact)] = "Contact", [nameof(ChatType.DepartmentHeads)] = "department_heads" });
}