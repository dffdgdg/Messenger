using MessengerDesktop.Helpers;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public class ChatSearchManager(int chatId,int userId,IApiClientService apiClient,Func<IReadOnlyCollection<UserDTO>> getMembersFunc)
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly Func<IReadOnlyCollection<UserDTO>> _getMembersFunc = getMembersFunc ?? throw new ArgumentNullException(nameof(getMembersFunc));

    public ObservableCollection<MessageViewModel> Results { get; } = [];
    public int TotalCount { get; private set; }
    public bool IsSearching { get; private set; }

    public async Task<(bool Success, string? Error)> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Clear();
            return (true, null);
        }

        try
        {
            IsSearching = true;

            var url = $"api/messages/chat/{chatId}/search" +
                      $"?query={Uri.EscapeDataString(query)}&userId={userId}&page=1&pageSize=20";

            var result = await _apiClient.GetAsync<SearchMessagesResponseDTO>(url, ct);

            if (!result.Success)
            {
                return (false, result.Error);
            }

            if (result.Data is not null)
            {
                TotalCount = result.Data.TotalCount;

                var viewModels = result.Data.Messages.ConvertAll(msg => new MessageViewModel(msg));

                ProcessSearchResults(result.Data.Messages, viewModels);

                Results.Clear();
                foreach (var vm in viewModels)
                {
                    Results.Add(vm);
                }
            }

            return (true, null);
        }
        finally
        {
            IsSearching = false;
        }
    }

    public void Clear()
    {
        Results.Clear();
        TotalCount = 0;
    }

    private void ProcessSearchResults(List<MessageDTO> messages, List<MessageViewModel> viewModels)
    {
        var members = _getMembersFunc();
        var userDict = members.ToDictionary(u => u.Id);

        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            var vm = viewModels[i];

            if (userDict.TryGetValue(msg.SenderId, out var user))
            {
                vm.SenderName = user.DisplayName ?? user.Username;
                if (!string.IsNullOrEmpty(user.Avatar))
                {
                    vm.SenderAvatar = AvatarHelper.GetSafeUrl(user.Avatar);
                }
            }
        }
    }
}