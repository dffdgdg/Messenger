using Core.ViewModels.Dialog;
using Shared.Contracts.Poll;
using Shared.Contracts.User;

namespace Core.ViewModels.Chat;

public sealed partial class PollResultsDialogViewModel : DialogBaseViewModel
{
    private readonly IApiClientService _apiClient;
    private readonly ObservableCollection<UserDto> _chatMembers;

    [ObservableProperty] public partial ObservableCollection<PollResultOptionViewModel> Options { get; set; } = [];
    [ObservableProperty] public partial bool IsAnonymous { get; set; }
    [ObservableProperty] public partial int TotalVotes { get; set; }

    public PollResultsDialogViewModel(PollDto poll, ObservableCollection<UserDto> chatMembers, IApiClientService apiClient)
    {
        _apiClient = apiClient;
        _chatMembers = chatMembers;

        Title = "Результаты опроса";
        CanCloseOnBackgroundClick = true;

        IsAnonymous = poll.IsAnonymous;
        TotalVotes = poll.Options.Sum(o => o.VotesCount);

        Options = new ObservableCollection<PollResultOptionViewModel>(
            poll.Options.OrderByDescending(o => o.VotesCount).Select(o => new PollResultOptionViewModel(o, poll.IsAnonymous, TotalVotes)));
    }

    public Task TriggerInitializeAsync()
        => InitializeAsync(LoadVotersAsync);

    private async Task LoadVotersAsync()
    {
        if (IsAnonymous) return;

        var allUserIds = Options.SelectMany(o => o.VoterIds).Distinct().ToList();

        if (allUserIds.Count == 0)
        {
            foreach (var opt in Options)
                opt.ApplyVoters([]);
            return;
        }

        var resolved = await ResolveUsersAsync(allUserIds);

        foreach (var option in Options)
            option.ApplyVoters(resolved);
    }

    private async Task<Dictionary<int, UserDto>> ResolveUsersAsync(List<int> userIds)
    {
        var result = new Dictionary<int, UserDto>();

        var memberLookup = _chatMembers.ToDictionary(m => m.Id);
        var missing = new List<int>();

        foreach (var id in userIds)
        {
            if (memberLookup.TryGetValue(id, out var member))
                result[id] = member;
            else
                missing.Add(id);
        }

        if (missing.Count > 0)
        {
            var tasks = missing.Select(async id =>
            {
                try
                {
                    var response = await _apiClient.GetAsync<UserDto>(
                        ApiEndpoints.Users.ById(id));
                    if (response is { Success: true, Data: not null })
                        return (id, response.Data);
                }
                catch { /* best-effort */ }
                return (id, (UserDto?)null);
            });

            var fetched = await Task.WhenAll(tasks);
            foreach (var (id, user) in fetched)
                if (user != null) result[id] = user;
        }

        return result;
    }
}