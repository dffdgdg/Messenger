using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace API.Services.Features.Call;

internal static class ChannelExtensions
{
    /// <summary>
    /// Возвращает true если в канале есть хотя бы один элемент.
    /// BoundedChannel в .NET 8+ предоставляет Count без блокировки.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool HasItems<T>(this ChannelReader<T> reader)
        => reader.Count > 0;
}