using MessengerDesktop.Services.Api;
using MessengerShared.DTO;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat.Managers;

public class ChatMemberLoader(int chatId, int currentUserId, IApiClientService apiClient)
{
    public async Task<ObservableCollection<UserDTO>> LoadMembersAsync(ChatDTO? chat, CancellationToken ct = default)
    {
        var result = await apiClient.GetAsync<List<UserDTO>>($"api/chats/{chatId}/members", ct);

        if (result.Success && result.Data is { Count: > 0 })
        {
            return new ObservableCollection<UserDTO>(result.Data);
        }

        if (chat?.Type == ChatType.Contact)
        {
            return await LoadContactMembersAsync(chat, ct);
        }

        return [];
    }

    private async Task<ObservableCollection<UserDTO>> LoadContactMembersAsync(ChatDTO chat, CancellationToken ct)
    {
        if (!int.TryParse(chat.Name, out var otherUserId))
        {
            return [];
        }

        var members = new ObservableCollection<UserDTO>();

        var meResult = await apiClient.GetAsync<UserDTO>($"api/user/{currentUserId}", ct);
        if (meResult is { Success: true, Data: not null })
        {
            members.Add(meResult.Data);
        }

        if (otherUserId != currentUserId)
        {
            var otherResult = await apiClient.GetAsync<UserDTO>($"api/user/{otherUserId}", ct);
            if (otherResult is { Success: true, Data: not null })
            {
                members.Add(otherResult.Data);
            }
        }

        return members;
    }
}