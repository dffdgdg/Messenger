using MessengerDesktop.ViewModels.Chats;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class GlobalSearchManager(int userId, IApiClientService apiClient, int debounceMs = AppConstants.DefaultDebounceMs)
    : ObservableObject, IDisposable
{
    private CancellationTokenSource? _searchCts;
    private bool _disposed;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private int _totalMessagesCount;
    [ObservableProperty] private bool _hasMoreMessages;
    [ObservableProperty] private int? _chatLocalSearchChatId;
    [ObservableProperty] private ChatType? _chatLocalSearchChatType;
    [ObservableProperty] private string? _chatLocalSearchChatName;
    [ObservableProperty] private string? _chatLocalSearchChatAvatar;
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<ChatListItemViewModel> ChatResults { get; } = [];
    public ObservableCollection<GlobalSearchMessageDto> MessageResults { get; } = [];

    public bool HasResults => ChatResults.Count > 0 || MessageResults.Count > 0;
    public bool HasChatResults => ChatResults.Count > 0;
    public bool HasMessageResults => MessageResults.Count > 0;
    public bool IsChatLocalMode => ChatLocalSearchChatId.HasValue;

    partial void OnChatLocalSearchChatIdChanged(int? value)
    {
        OnPropertyChanged(nameof(IsChatLocalMode));

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            _ = SearchWithDelayAsync(SearchQuery, _searchCts.Token);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        IsSearchMode = !string.IsNullOrWhiteSpace(value);

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        if (string.IsNullOrWhiteSpace(value))
        {
            Clear();
            return;
        }

        _ = SearchWithDelayAsync(value, _searchCts.Token);
    }

    private async Task SearchWithDelayAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(debounceMs, ct);
            if (ct.IsCancellationRequested) return;
            await ExecuteSearchAsync(query, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected debounce cancellation
        }
    }

    public async Task ExecuteSearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Clear();
            return;
        }

        try
        {
            IsSearching = true;
            ErrorMessage = null;

            if (IsChatLocalMode)
            {
                await ExecuteChatLocalSearchAsync(query, 1, ct);
            }
            else
            {
                await ExecuteGlobalSearchAsync(query, 1, ct);
            }
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
        var url = ApiEndpoints.Message.Search(userId, query, page, AppConstants.SearchPageSize);
        var result = await apiClient.GetAsync<GlobalSearchResponseDto>(url, ct);

        if (ct.IsCancellationRequested) return;

        if (result.Success && result.Data != null)
        {
            if (page == 1)
            {
                ChatResults.Clear();
                MessageResults.Clear();

                foreach (var chat in result.Data.Chats)
                    ChatResults.Add(new ChatListItemViewModel(chat));
            }

            foreach (var msg in result.Data.Messages)
                MessageResults.Add(msg);

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
        var url = ApiEndpoints.Message.ChatSearch(chatId, query, page, AppConstants.SearchPageSize);
        var result = await apiClient.GetAsync<SearchMessagesResponseDto>(url, ct);

        if (ct.IsCancellationRequested) return;

        if (result.Success && result.Data != null)
        {
            if (page == 1)
            {
                ChatResults.Clear();
                MessageResults.Clear();
            }

            foreach (var msg in result.Data.Messages)
            {
                MessageResults.Add(new GlobalSearchMessageDto
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
                    HasFiles = msg.Files.Count > 0
                });
            }

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
        if (!HasMoreMessages || IsSearching || string.IsNullOrWhiteSpace(SearchQuery))
            return;

        try
        {
            IsSearching = true;
            var nextPage = (MessageResults.Count / AppConstants.SearchPageSize) + 1;

            if (IsChatLocalMode)
            {
                await ExecuteChatLocalSearchAsync(SearchQuery, nextPage, CancellationToken.None);
            }
            else
            {
                await ExecuteGlobalSearchAsync(SearchQuery, nextPage, CancellationToken.None);
            }
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

    public void EnterSearchMode()
    {
        if (IsSearchMode) return;
        IsSearchMode = true;
        ErrorMessage = null;
        NotifyResultsChanged();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        GC.SuppressFinalize(this);
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasChatResults));
        OnPropertyChanged(nameof(HasMessageResults));
        OnPropertyChanged(nameof(IsChatLocalMode));
    }
}