using Desktop.ViewModels.ChatList.Search;
using Desktop.ViewModels.Chats;

namespace Desktop.ViewModels.Chat;

public enum SearchScopeMode { All = 0, Chats = 1, Contacts = 2, CurrentChatMessages = 3 }
public enum SearchSortOrder { Newest = 0, Oldest = 1 }
public enum SearchContentFilter { Any = 0, OnlyText = 1, WithFiles = 2, WithVoice = 3, WithPolls = 4 }
public enum SearchAuthorFilter { Any = 0, Me = 1, Others = 2 }

public sealed record SearchFilterItem(int Id, string DisplayName, string? Avatar);

public sealed partial class GlobalSearchManager(int userId, bool startWithChatsScope, IApiClientService apiClient,
    Func<Task<List<SearchFilterItem>>>? getUsersFunc = null,
    Func<Task<List<SearchFilterItem>>>? getChatsFunc = null,
    int debounceMs = AppConstants.DefaultDebounceMs) : ObservableObject, IDisposable
{
    private List<SearchFilterItem>? _cachedUsers;
    private List<SearchFilterItem>? _cachedChats;
    private List<SearchFilterItem>? _cachedChatMembers;
    private List<SearchFilterItem>? _cachedChatsForSender;

    private Task? _loadChatMembersTask;
    private Task? _loadChatsForSenderTask;

    private CancellationTokenSource? _searchCts;
    private bool _disposed;

    public bool IsChatFilterVisible =>
        SelectedScope != SearchScopeMode.Contacts &&
        SelectedScope != SearchScopeMode.CurrentChatMessages;

    [ObservableProperty] public partial ObservableCollection<SearchFilterItem> SenderSuggestions { get; set; } = [];
    [ObservableProperty] public partial SearchFilterItem? SelectedSender { get; set; }
    [ObservableProperty] public partial string SenderSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSenderDropdownOpen { get; set; }

    [ObservableProperty] public partial ObservableCollection<SearchFilterItem> ChatSuggestions { get; set; } = [];
    [ObservableProperty] public partial SearchFilterItem? SelectedChatFilter { get; set; }
    [ObservableProperty] public partial string ChatSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsChatDropdownOpen { get; set; }

    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSearching { get; set; }
    [ObservableProperty] public partial bool IsSearchMode { get; set; }
    [ObservableProperty] public partial int TotalMessagesCount { get; set; }
    [ObservableProperty] public partial bool HasMoreMessages { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial SearchScopeMode SelectedScope { get; set; } =
        startWithChatsScope ? SearchScopeMode.Chats : SearchScopeMode.All;

    [ObservableProperty] public partial int? ChatLocalSearchChatId { get; set; }
    [ObservableProperty] public partial ChatType? ChatLocalSearchChatType { get; set; }
    [ObservableProperty] public partial string? ChatLocalSearchChatName { get; set; }
    [ObservableProperty] public partial string? ChatLocalSearchChatAvatar { get; set; }

    [ObservableProperty] public partial SearchSortOrder SortOrder { get; set; } = SearchSortOrder.Newest;
    [ObservableProperty] public partial string MentionFilter { get; set; } = string.Empty;
    [ObservableProperty] public partial SearchContentFilter ContentFilter { get; set; } = SearchContentFilter.Any;
    [ObservableProperty] public partial SearchAuthorFilter AuthorFilter { get; set; } = SearchAuthorFilter.Any;
    [ObservableProperty] public partial DateTimeOffset? DateFromFilter { get; set; }
    [ObservableProperty] public partial DateTimeOffset? DateToFilter { get; set; }

    public ObservableCollection<ChatListItemViewModel> ChatResults { get; } = [];
    public ObservableCollection<SearchMessageResultViewModel> MessageResults { get; } = [];

    public bool HasResults => ChatResults.Count > 0 || MessageResults.Count > 0;
    public bool HasChatResults => ChatResults.Count > 0;
    public bool HasMessageResults => MessageResults.Count > 0;
    public bool CanSearchInCurrentChat => ChatLocalSearchChatId.HasValue;
    public bool IsAllScope => SelectedScope == SearchScopeMode.All;
    public bool IsChatsScope => SelectedScope == SearchScopeMode.Chats;
    public bool IsContactsScope => SelectedScope == SearchScopeMode.Contacts;
    public bool IsChatLocalMode => SelectedScope == SearchScopeMode.CurrentChatMessages && CanSearchInCurrentChat;
    public bool IsEmpty => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery) && !HasResults;
    public bool IsSortNewest => SortOrder == SearchSortOrder.Newest;

    public bool HasActiveFilters =>
        SelectedSender != null ||
        SelectedChatFilter != null ||
        !string.IsNullOrWhiteSpace(MentionFilter) ||
        ContentFilter != SearchContentFilter.Any ||
        AuthorFilter != SearchAuthorFilter.Any ||
        DateFromFilter.HasValue ||
        DateToFilter.HasValue;

    private bool? ServerHasFiles => ContentFilter == SearchContentFilter.WithFiles ? true : null;
    private bool? ServerHasVoice => ContentFilter == SearchContentFilter.WithVoice ? true : null;
    private bool? ServerHasPoll => ContentFilter == SearchContentFilter.WithPolls ? true : null;
    private bool? ServerOnlyText => ContentFilter == SearchContentFilter.OnlyText ? true : null;
    private bool ServerOldestFirst => SortOrder == SearchSortOrder.Oldest;

    private int? ServerSenderId =>
        AuthorFilter == SearchAuthorFilter.Me ? userId : SelectedSender?.Id;

    #region Property change handlers

    partial void OnSelectedScopeChanged(SearchScopeMode value)
    {
        if (value == SearchScopeMode.CurrentChatMessages && !CanSearchInCurrentChat)
        {
            SelectedScope = SearchScopeMode.All;
            return;
        }

        OnPropertyChanged(nameof(IsChatLocalMode));
        OnPropertyChanged(nameof(IsAllScope));
        OnPropertyChanged(nameof(IsChatsScope));
        OnPropertyChanged(nameof(IsContactsScope));
        OnPropertyChanged(nameof(IsChatFilterVisible));

        if (!IsChatFilterVisible)
        {
            SelectedChatFilter = null;
            ChatSearchText = string.Empty;
            _cachedChatMembers = null;
            _loadChatMembersTask = null;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            RestartSearch();
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnMentionFilterChanged(string value) => OnPropertyChanged(nameof(HasActiveFilters));
    partial void OnContentFilterChanged(SearchContentFilter value) => OnPropertyChanged(nameof(HasActiveFilters));
    partial void OnAuthorFilterChanged(SearchAuthorFilter value) => OnPropertyChanged(nameof(HasActiveFilters));
    partial void OnDateFromFilterChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasActiveFilters));
    partial void OnDateToFilterChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(HasActiveFilters));

    partial void OnChatLocalSearchChatIdChanged(int? value)
    {
        OnPropertyChanged(nameof(CanSearchInCurrentChat));
        OnPropertyChanged(nameof(IsChatLocalMode));

        if (!CanSearchInCurrentChat && SelectedScope == SearchScopeMode.CurrentChatMessages)
        {
            SelectedScope = SearchScopeMode.All;
            return;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            RestartSearch();
    }

    partial void OnSearchQueryChanged(string value)
    {
        RestartSearch();

        if (string.IsNullOrWhiteSpace(value))
        {
            if (HasActiveFilters && IsSearchMode)
            {
                _ = SearchWithDelayAsync(string.Empty, _searchCts!.Token);
                return;
            }
            Clear();
        }
    }

    partial void OnSortOrderChanged(SearchSortOrder value)
    {
        OnPropertyChanged(nameof(IsSortNewest));
        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            RestartSearch();
    }

    partial void OnSelectedSenderChanged(SearchFilterItem? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        IsSenderDropdownOpen = false;

        if (value == null)
        {
            _cachedChatsForSender = null;
            _loadChatsForSenderTask = null;
        }
        else
        {
            _cachedChatsForSender = null;
            _loadChatsForSenderTask = LoadChatsForSenderAsync(value.Id);
        }

        // Предзагружаем подсказки чатов, но НЕ открываем дропдаун
        _ = UpdateChatSuggestionsAsync(ChatSearchText);

        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            RestartSearch();
    }

    partial void OnSelectedChatFilterChanged(SearchFilterItem? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        IsChatDropdownOpen = false;

        if (value == null)
        {
            _cachedChatMembers = null;
            _loadChatMembersTask = null;
        }
        else
        {
            _cachedChatMembers = null;
            _loadChatMembersTask = LoadChatMembersAsync(value.Id);
        }

        // Предзагружаем подсказки пользователей, но НЕ открываем дропдаун
        _ = UpdateSenderSuggestionsAsync(SenderSearchText);

        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            RestartSearch();
    }

    partial void OnSenderSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        _ = UpdateSenderSuggestionsAsync(value);
    }

    partial void OnChatSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        _ = UpdateChatSuggestionsAsync(value);
    }

    #endregion

    #region Autocomplete data loading

    private async Task LoadChatMembersAsync(int chatId)
    {
        try
        {
            var membersResult = await apiClient.GetAsync<List<ChatMemberDto>>(
                ApiEndpoints.Chats.MembersDetailed(chatId));

            if (!membersResult.Success || membersResult.Data == null || membersResult.Data.Count == 0)
            {
                _cachedChatMembers = [];
                return;
            }

            var memberIds = membersResult.Data.Select(m => m.UserId).ToHashSet();

            if (_cachedUsers == null && getUsersFunc != null)
            {
                _cachedUsers = await getUsersFunc();
            }

            _cachedChatMembers = [.. (_cachedUsers ?? [])
                .Where(u => memberIds.Contains(u.Id))
                .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)];

            System.Diagnostics.Debug.WriteLine($"[GlobalSearchManager] LoadChatMembersAsync: {_cachedChatMembers.Count} members from user cache");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlobalSearchManager] LoadChatMembersAsync error: {ex.Message}");
            _cachedChatMembers = [];
        }
    }

    private async Task UpdateSenderSuggestionsAsync(string query)
    {
        SenderSuggestionsLoadingChanged?.Invoke(true);
        try
        {
            List<SearchFilterItem> source;

            if (SelectedChatFilter != null)
            {
                // Выбран чат — показываем только его участников
                if (_loadChatMembersTask != null)
                    await _loadChatMembersTask;
                else if (_cachedChatMembers == null)
                {
                    _loadChatMembersTask = LoadChatMembersAsync(SelectedChatFilter.Id);
                    await _loadChatMembersTask;
                }

                source = _cachedChatMembers ?? [];
            }
            else
            {
                // Чат не выбран — показываем всех пользователей
                if (_cachedUsers == null && getUsersFunc != null)
                {
                    _cachedUsers = await getUsersFunc();
                }
                source = _cachedUsers ?? [];
            }

            var filtered = string.IsNullOrWhiteSpace(query)
                ? source.Take(8)
                : source.Where(u => u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8);

            SenderSuggestions = new ObservableCollection<SearchFilterItem>(filtered);
        }
        finally
        {
            SenderSuggestionsLoadingChanged?.Invoke(false);
            SenderSuggestionsReady?.Invoke();
        }
    }

    private async Task LoadChatsForSenderAsync(int senderId)
    {
        try
        {
            var result = await apiClient.PostAsync<GlobalSearchQueryDto, GlobalSearchResponseDto>(
                ApiEndpoints.Messages.Search(userId),
                new GlobalSearchQueryDto
                {
                    Query = string.Empty,
                    Page = 1,
                    PageSize = 100,
                    SenderId = senderId
                });

            if (!result.Success || result.Data == null)
            {
                _cachedChatsForSender = [];
                return;
            }

            var chatIds = result.Data.Messages
                .Select(m => m.ChatId)
                .Distinct()
                .ToHashSet();

            _cachedChats ??= getChatsFunc != null ? await getChatsFunc() : [];

            _cachedChatsForSender = [.. _cachedChats
                .Where(c => chatIds.Contains(c.Id))
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)];
        }
        catch
        {
            _cachedChatsForSender = [];
        }
    }
    public event Action<bool>? SenderSuggestionsLoadingChanged;
    public event Action? SenderSuggestionsReady;
    public event Action? ChatSuggestionsReady;

    private async Task UpdateChatSuggestionsAsync(string query)
    {
        List<SearchFilterItem> source;

        if (SelectedSender != null)
        {
            if (_loadChatsForSenderTask != null)
                await _loadChatsForSenderTask;
            else if (_cachedChatsForSender == null)
                await LoadChatsForSenderAsync(SelectedSender.Id);

            source = _cachedChatsForSender ?? [];
        }
        else
        {
            _cachedChats ??= getChatsFunc != null ? await getChatsFunc() : [];
            source = _cachedChats;
        }

        var filtered = string.IsNullOrWhiteSpace(query)
            ? source.Take(8)
            : source.Where(c => c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8);

        ChatSuggestions = new ObservableCollection<SearchFilterItem>(filtered);
        ChatSuggestionsReady?.Invoke();
    }

    /// <summary>
    /// Вызывать при фокусе на поле "От кого" чтобы триггернуть загрузку подсказок.
    /// </summary>
    public async Task RequestSenderSuggestionsAsync()
        => await UpdateSenderSuggestionsAsync(SenderSearchText);

    /// <summary>
    /// Вызывать при фокусе на поле "В чате" чтобы триггернуть загрузку подсказок.
    /// </summary>
    public async Task RequestChatSuggestionsAsync()
        => await UpdateChatSuggestionsAsync(ChatSearchText);

    #endregion

    #region Search execution

    private void RestartSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            _ = SearchWithDelayAsync(SearchQuery, _searchCts.Token);
    }

    private async Task SearchWithDelayAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(debounceMs, ct);
            if (ct.IsCancellationRequested) return;
            await ExecuteSearchAsync(query, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task ExecuteSearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) && !HasActiveFilters)
        {
            Clear();
            return;
        }

        try
        {
            IsSearching = true;
            ErrorMessage = null;

            if (IsChatLocalMode)
                await ExecuteChatLocalSearchAsync(query, 1, ct);
            else
                await ExecuteGlobalSearchAsync(query, 1, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task ExecuteGlobalSearchAsync(string query, int page, CancellationToken ct)
    {
        var dto = new GlobalSearchQueryDto
        {
            Query = query,
            Page = page,
            PageSize = AppConstants.SearchPageSize,
            SenderId = ServerSenderId,
            FilterChatId = SelectedChatFilter?.Id,
            HasFiles = ServerHasFiles,
            HasVoice = ServerHasVoice,
            HasPoll = ServerHasPoll,
            OnlyText = ServerOnlyText,
            DateFrom = DateFromFilter?.DateTime,
            DateTo = DateToFilter?.DateTime,
            OldestFirst = ServerOldestFirst
        };

        var url = ApiEndpoints.Messages.Search(userId);
        var result = await apiClient.PostAsync<GlobalSearchQueryDto, GlobalSearchResponseDto>(url, dto, ct);

        if (ct.IsCancellationRequested) return;

        if (result.Success && result.Data != null)
        {
            if (page == 1)
            {
                ChatResults.Clear();
                MessageResults.Clear();

                foreach (var chat in result.Data.Chats.Where(c => IsChatAllowedForScope(c.Type)))
                    ChatResults.Add(new ChatListItemViewModel(chat));
            }

            foreach (var msg in result.Data.Messages
                .Where(m => IsChatAllowedForScope(m.ChatType) && IsClientOnlyFilter(m)))
            {
                MessageResults.Add(new SearchMessageResultViewModel(msg, userId));
            }

            TotalMessagesCount = result.Data.TotalMessagesCount;
            HasMoreMessages = result.Data.HasMoreMessages;

            NotifyResultsChanged();
        }
        else
        {
            ErrorMessage = result.Error ?? "Ошибка поиска";
        }
    }

    private async Task ExecuteChatLocalSearchAsync(string query, int page, CancellationToken ct)
    {
        if (!ChatLocalSearchChatId.HasValue)
        {
            Clear();
            return;
        }

        var chatId = ChatLocalSearchChatId.Value;

        var dto = new SearchMessagesQueryDto
        {
            Query = query,
            Page = page,
            PageSize = AppConstants.SearchPageSize,
            SenderId = ServerSenderId,
            HasFiles = ServerHasFiles,
            HasVoice = ServerHasVoice,
            HasPoll = ServerHasPoll,
            OnlyText = ServerOnlyText,
            DateFrom = DateFromFilter?.DateTime,
            DateTo = DateToFilter?.DateTime,
            OldestFirst = ServerOldestFirst
        };

        var url = ApiEndpoints.Messages.ChatSearch(chatId);
        var result = await apiClient.PostAsync<SearchMessagesQueryDto, SearchMessagesResponseDto>(url, dto, ct);

        if (ct.IsCancellationRequested) return;

        if (result.Success && result.Data != null)
        {
            if (page == 1)
            {
                ChatResults.Clear();
                MessageResults.Clear();
            }

            var mapped = result.Data.Messages.Select(msg => new GlobalSearchMessageDto
            {
                Id = msg.Id,
                ChatId = chatId,
                ChatName = ChatLocalSearchChatName,
                ChatAvatar = ChatLocalSearchChatAvatar,
                ChatType = ChatLocalSearchChatType ?? ChatType.Contact,
                SenderId = msg.SenderId,
                SenderName = msg.SenderName,
                Content = msg.Content,
                CreatedAt = msg.CreatedAt,
                HighlightedContent = msg.Content,
                HasFiles = msg.Files.Count > 0,
                HasVoice = msg.IsVoiceMessage,
                HasPoll = msg.Poll != null
            }).Where(IsClientOnlyFilter);

            foreach (var msg in mapped)
                MessageResults.Add(new SearchMessageResultViewModel(msg, userId));

            TotalMessagesCount = result.Data.TotalCount;
            HasMoreMessages = result.Data.HasMoreMessages;

            NotifyResultsChanged();
        }
        else
        {
            ErrorMessage = result.Error ?? "Ошибка поиска";
        }
    }

    public async Task LoadMoreMessagesAsync()
    {
        if (!HasMoreMessages || IsSearching)
            return;
        try
        {
            IsSearching = true;
            var nextPage = (MessageResults.Count / AppConstants.SearchPageSize) + 1;

            if (IsChatLocalMode)
                await ExecuteChatLocalSearchAsync(SearchQuery, nextPage, CancellationToken.None);
            else
                await ExecuteGlobalSearchAsync(SearchQuery, nextPage, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    #endregion

    #region Filters

    private bool IsClientOnlyFilter(GlobalSearchMessageDto message)
    {
        if (AuthorFilter == SearchAuthorFilter.Others && message.SenderId == userId)
            return false;

        if (!string.IsNullOrWhiteSpace(MentionFilter) && !(message.Content ?? string.Empty).Contains(MentionFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private bool IsChatAllowedForScope(ChatType type) => SelectedScope switch
    {
        SearchScopeMode.Chats => type is not ChatType.Contact,
        SearchScopeMode.Contacts => type == ChatType.Contact,
        _ => true
    };

    #endregion

    #region Public API

    public void EnterSearchMode()
    {
        IsSearchMode = true;
        ErrorMessage = null;
        NotifyResultsChanged();
    }

    public void UseScope(SearchScopeMode scope)
    {
        if (scope == SearchScopeMode.CurrentChatMessages && !CanSearchInCurrentChat)
            return;

        SelectedScope = scope;
        EnterSearchMode();
    }

    public void Clear()
    {
        ChatResults.Clear();
        MessageResults.Clear();
        TotalMessagesCount = 0;
        HasMoreMessages = false;
        ErrorMessage = null;
        NotifyResultsChanged();
    }

    public void ExitSearch()
    {
        SearchQuery = string.Empty;
        IsSearchMode = false;
        Clear();
    }

    public async Task ApplyFiltersAsync()
    {
        EnterSearchMode();

        if (string.IsNullOrWhiteSpace(SearchQuery) && !HasActiveFilters)
        {
            Clear();
            return;
        }

        await ExecuteSearchAsync(SearchQuery, CancellationToken.None);
    }

    public void ToggleSortOrder() => SortOrder = SortOrder == SearchSortOrder.Newest
        ? SearchSortOrder.Oldest : SearchSortOrder.Newest;

    public void ResetFilters()
    {
        SelectedSender = null;
        SenderSearchText = string.Empty;
        SelectedChatFilter = null;
        ChatSearchText = string.Empty;

        _cachedChatMembers = null;
        _cachedChatsForSender = null;
        _loadChatMembersTask = null;
        _loadChatsForSenderTask = null;

        MentionFilter = string.Empty;
        ContentFilter = SearchContentFilter.Any;
        AuthorFilter = SearchAuthorFilter.Any;
        DateFromFilter = null;
        DateToFilter = null;
    }

    #endregion

    #region Display

    public string TotalMessagesText => TotalMessagesCount switch
    {
        0 => "Нет результатов",
        1 => "1 результат",
        var n when n % 100 is >= 11 and <= 14 => $"{n} результатов",
        var n when n % 10 == 1 => $"{n} результат",
        var n when n % 10 is 2 or 3 or 4 => $"{n} результата",
        var n => $"{n} результатов"
    };

    partial void OnTotalMessagesCountChanged(int value) =>
        OnPropertyChanged(nameof(TotalMessagesText));

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasChatResults));
        OnPropertyChanged(nameof(HasMessageResults));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsChatLocalMode));
        OnPropertyChanged(nameof(CanSearchInCurrentChat));
        OnPropertyChanged(nameof(IsAllScope));
        OnPropertyChanged(nameof(IsChatsScope));
        OnPropertyChanged(nameof(IsContactsScope));
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }
}