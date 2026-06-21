using Npgsql;
using Npgsql.NameTranslation;

namespace API.Infrastructure.Database;

public class EnumNameTranslator(IReadOnlyDictionary<string, string> memberNames) : INpgsqlNameTranslator
{
    private static readonly NpgsqlSnakeCaseNameTranslator Fallback = new();

    public string TranslateTypeName(string clrName)
        => Fallback.TranslateTypeName(clrName);

    public string TranslateMemberName(string clrName)
        => memberNames.TryGetValue(clrName, out var mappedName) ? mappedName : Fallback.TranslateMemberName(clrName);
}