using MessengerDesktop.ViewModels.ChatList.Search;
using MessengerDesktop.ViewModels.Chats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public enum SearchScopeMode { All = 0, Chats = 1, Contacts = 2, CurrentChatMessages = 3 }
public enum SearchSortOrder { Newest = 0, Oldest = 1 }
public enum SearchContentFilter { Any = 0, OnlyText = 1, WithFiles = 2, WithVoice = 3, WithPolls = 4 }
public enum SearchAuthorFilter { Any = 0, Me = 1, Others = 2 }

public sealed record SearchFilterItem(int Id, string DisplayName, string? Avatar);

public sealed partial class GlobalSearchManager(int userId, bool startWithChatsScope, IApiClientService apiClient, Func<Task<List<SearchFilterItem>>>? getUsersFunc = null,
    Func<Task<List<SearchFilterItem>>>? getChatsFunc = null, int debounceMs = AppConstants.DefaultDebounceMs) : ObservableObject, IDisposable
{
    private List<SearchFilterItem>? _cachedUsers;
    private List<SearchFilterItem>? _cachedChats;
    private CancellationTokenSource? _searchCts;
    private List<SearchFilterItem>? _cachedChatMembers;

    private bool _disposed;
    public bool IsChatFilterVisible => SelectedScope != SearchScopeMode.Contacts && SelectedScope != SearchScopeMode.CurrentChatMessages;

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

    [ObservableProperty] public partial SearchScopeMode SelectedScope { get; set; } = startWithChatsScope ? SearchScopeMode.Chats : SearchScopeMode.All;
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

    public bool HasActiveFilters => SelectedSender != null || SelectedChatFilter != null || !string.IsNullOrWhiteSpace(MentionFilter) || ContentFilter != SearchContentFilter.Any ||
        AuthorFilter != SearchAuthorFilter.Any || DateFromFilter.HasValue || DateToFilter.HasValue;

    private bool? ServerHasFiles => ContentFilter == SearchContentFilter.WithFiles ? true : null;
    private bool? ServerHasVoice => ContentFilter == SearchContentFilter.WithVoice ? true : null;
    private bool? ServerHasPoll => ContentFilter == SearchContentFilter.WithPolls ? true : null;
    private bool? ServerOnlyText => ContentFilter == SearchContentFilter.OnlyText ? true : null;
    private bool ServerOldestFirst => SortOrder == SearchSortOrder.Oldest;

    // senderId: Me → свой userId, выбранный отправитель → его Id, иначе null
    // AuthorFilter.Others не выразить через senderId — фильтруем на клиенте
    private int? ServerSenderId => AuthorFilter == SearchAuthorFilter.Me ? userId : SelectedSender?.Id;

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
        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            RestartSearch();
    }

    partial void OnSelectedChatFilterChanged(SearchFilterItem? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        IsChatDropdownOpen = false;

        _cachedChatMembers = null;

        if (value != null && SelectedSender != null)
        {
            SelectedSender = null;
            SenderSearchText = string.Empty;
        }

        if (value != null)
            _ = LoadChatMembersAsync(value.Id);

        if (!string.IsNullOrWhiteSpace(SearchQuery) || HasActiveFilters)
            RestartSearch();
    }

    private async Task LoadChatMembersAsync(int chatId)
    {
        try
        {
            var result = await apiClient.GetAsync<List<ChatMemberDto>>(ApiEndpoints.Chats.MembersDetailed(chatId));

            if (result.Success && result.Data != null)
            {
                _cachedChatMembers = result.Data.ConvertAll(m => new SearchFilterItem(m.UserId, m.DisplayName ?? m.Username ?? string.Empty, m.Avatar));
            }
        }
        catch
        {
            _cachedChatMembers = null;
        }
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested) {/* Игнорируем отмену, вызванную новым поиском */ }
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
        System.Diagnostics.Debug.WriteLine($"ExecuteGlobal: ContentFilter={ContentFilter}, ServerHasVoice={ServerHasVoice}, dto.HasVoice будет={ContentFilter == SearchContentFilter.WithVoice}");
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

            foreach (var msg in result.Data.Messages.Where(m => IsChatAllowedForScope(m.ChatType) && IsClientOnlyFilter(m)))
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
        if (!HasMoreMessages || IsSearching || (string.IsNullOrWhiteSpace(SearchQuery) && !HasActiveFilters))
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

    /// <summary>
    /// Фильтры, которые нельзя передать на сервер одним параметром.
    /// AuthorFilter.Others — исключить свои сообщения (нет серверного excludeSenderId).
    /// MentionFilter — поиск по упоминанию внутри content, сервер ищет по query.
    /// </summary>
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
        SearchScopeMode.Chats => type is ChatType.Chat or ChatType.Department,
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
        System.Diagnostics.Debug.WriteLine($"ApplyFilters: ContentFilter={ContentFilter}, ServerHasVoice={ServerHasVoice}, HasActiveFilters={HasActiveFilters}");

        EnterSearchMode();

        if (string.IsNullOrWhiteSpace(SearchQuery) && !HasActiveFilters)
        {
            Clear();
            return;
        }

        await ExecuteSearchAsync(SearchQuery, CancellationToken.None);
    }

    public void ToggleSortOrder() => SortOrder = SortOrder == SearchSortOrder.Newest ? SearchSortOrder.Oldest : SearchSortOrder.Newest;

    public void ResetFilters()
    {
        SelectedSender = null;
        SenderSearchText = string.Empty;
        SelectedChatFilter = null;
        ChatSearchText = string.Empty;
        MentionFilter = string.Empty;
        ContentFilter = SearchContentFilter.Any;
        AuthorFilter = SearchAuthorFilter.Any;
        DateFromFilter = null;
        DateToFilter = null;
    }

    #endregion

    #region Autocomplete

    private async Task UpdateSenderSuggestionsAsync(string query)
    {
        List<SearchFilterItem> source;

        if (SelectedChatFilter != null)
        {
            if (_cachedChatMembers == null)
                await LoadChatMembersAsync(SelectedChatFilter.Id);

            source = _cachedChatMembers ?? [];
        }
        else
        {
            _cachedUsers ??= getUsersFunc != null ? await getUsersFunc() : [];
            source = _cachedUsers;
        }

        var filtered = string.IsNullOrWhiteSpace(query) ? source.Take(8) : source.Where(u => u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8);

        SenderSuggestions = new ObservableCollection<SearchFilterItem>(filtered);
        IsSenderDropdownOpen = SenderSuggestions.Count > 0;
    }

    private async Task UpdateChatSuggestionsAsync(string query)
    {
        _cachedChats ??= getChatsFunc != null ? await getChatsFunc() : [];

        var filtered = string.IsNullOrWhiteSpace(query) ? _cachedChats.Take(8) : _cachedChats.Where(c => c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8);

        ChatSuggestions = new ObservableCollection<SearchFilterItem>(filtered);
        IsChatDropdownOpen = ChatSuggestions.Count > 0;
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