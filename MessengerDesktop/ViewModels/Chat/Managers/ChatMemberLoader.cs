using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Api;
using MessengerShared.Dto.Chat;
using MessengerShared.Dto.User;
using MessengerShared.Enum;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public class ChatMemberLoader(int chatId, int currentUserId, IApiClientService apiClient)
{
    public async Task<ObservableCollection<UserDto>> LoadMembersAsync(ChatDto? chat, CancellationToken ct = default)
    {
        var result = await apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Chat.Members(chatId), ct);

        if (result.Success && result.Data is { Count: > 0 })
            return new ObservableCollection<UserDto>(result.Data);

        if (chat?.Type == ChatType.Contact)
            return await LoadContactMembersAsync(chat, ct);

        return [];
    }

    private async Task<ObservableCollection<UserDto>> LoadContactMembersAsync(ChatDto chat, CancellationToken ct)
    {
        if (!int.TryParse(chat.Name, out var otherUserId))
        {
            return [];
        }

        var members = new ObservableCollection<UserDto>();
        var meResult = await apiClient.GetAsync<UserDto>(ApiEndpoints.User.ById(currentUserId), ct);
        if (meResult is { Success: true, Data: not null })
        {
            members.Add(meResult.Data);
        }

        if (otherUserId != currentUserId)
        {
            var otherResult = await apiClient.GetAsync<UserDto>(ApiEndpoints.User.ById(otherUserId), ct);
            if (otherResult is { Success: true, Data: not null })
            {
                members.Add(otherResult.Data);
            }
        }

        return members;
    }
}