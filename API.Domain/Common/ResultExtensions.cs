using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace API.Domain.Common;

public static class ResultExtensions
{
    public static Result<T> As<T>(this Result result) => Result<T>.FromFailure(result);

    public static T? UnwrapOrDefault<T>(this Result<T> result, ILogger logger, [CallerMemberName] string caller = "") where T : class
    {
        if (result.IsSuccess) return result.Value;
        logger.LogWarning("{Method} failed: {Error}", caller, result.Error);
        return default;
    }

    public static T UnwrapOrFallback<T>(this Result<T> result, T fallback, ILogger logger, [CallerMemberName] string caller = "")
    {
        if (result.IsSuccess) return result.Value!;
        logger.LogWarning("{Method} failed: {Error}", caller, result.Error);
        return fallback;
    }

    public static bool TryUnwrap<T>(this Result<T> result, [NotNullWhen(true)] out T? value, ILogger? logger = null, [CallerMemberName] string caller = "")
    {
        if (result.IsSuccess && result.Value is not null)
        {
            value = result.Value;
            return true;
        }
        logger?.LogWarning("{Method} failed: {Error}", caller, result.Error);
        value = default;
        return false;
    }
}