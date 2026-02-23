using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Api;
using MessengerDesktop.ViewModels.Chats;
using MessengerShared.DTO.Search;
using System;
using System.Collections.ObjectModel;
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
    [ObservableProperty] private string? _errorMessage;

    public ObservableCollection<ChatListItemViewModel> ChatResults { get; } = [];
    public ObservableCollection<GlobalSearchMessageDTO> MessageResults { get; } = [];

    public bool HasResults => ChatResults.Count > 0 || MessageResults.Count > 0;
    public bool HasChatResults => ChatResults.Count > 0;
    public bool HasMessageResults => MessageResults.Count > 0;

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
            // Expected: previous search was cancelled by a newer keystroke (debounce).
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

            var url = ApiEndpoints.Message.Search(userId, query, 1, AppConstants.SearchPageSize);
            var result = await apiClient.GetAsync<GlobalSearchResponseDTO>(url, ct);

            if (ct.IsCancellationRequested) return;

            if (result.Success && result.Data != null)
            {
                ChatResults.Clear();
                MessageResults.Clear();

                foreach (var chat in result.Data.Chats)
                    ChatResults.Add(new ChatListItemViewModel(chat));

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

    public async Task LoadMoreMessagesAsync()
    {
        if (!HasMoreMessages || IsSearching || string.IsNullOrWhiteSpace(SearchQuery))
            return;

        try
        {
            IsSearching = true;
            var nextPage = (MessageResults.Count / AppConstants.SearchPageSize) + 1;

            var url = ApiEndpoints.Message.Search(userId, SearchQuery, nextPage, AppConstants.SearchPageSize);
            var result = await apiClient.GetAsync<GlobalSearchResponseDTO>(url);

            if (result.Success && result.Data != null)
            {
                foreach (var msg in result.Data.Messages)
                    MessageResults.Add(msg);

                HasMoreMessages = result.Data.HasMoreMessages;
                OnPropertyChanged(nameof(HasMessageResults));
            }
        }
        finally
        {
            IsSearching = false;
        }
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
    }
}