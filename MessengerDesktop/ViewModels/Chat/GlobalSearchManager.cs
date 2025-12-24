using CommunityToolkit.Mvvm.ComponentModel;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class GlobalSearchManager(int userId, IApiClientService apiClient, int debounceMs = 300) : ObservableObject
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private CancellationTokenSource? _searchCts;
    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private bool isSearchMode;

    [ObservableProperty]    
    private int totalMessagesCount;

    [ObservableProperty]
    private bool hasMoreMessages;

    [ObservableProperty]
    private string? errorMessage;

    public ObservableCollection<ChatDTO> ChatResults { get; } = [];
    public ObservableCollection<GlobalSearchMessageDTO> MessageResults { get; } = [];

    public bool HasResults => ChatResults.Count > 0 || MessageResults.Count > 0;
    public bool HasChatResults => ChatResults.Count > 0;
    public bool HasMessageResults => MessageResults.Count > 0;

    partial void OnSearchQueryChanged(string value)
    {
        IsSearchMode = !string.IsNullOrWhiteSpace(value);

        _searchCts?.Cancel();
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
        catch (OperationCanceledException)
        {

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

            var url = $"api/messages/user/{userId}/search" +
                      $"?query={Uri.EscapeDataString(query)}&page=1&pageSize=20";

            var result = await _apiClient.GetAsync<GlobalSearchResponseDTO>(url, ct);

            if (ct.IsCancellationRequested) return;

            if (result.Success && result.Data != null)
            {
                ChatResults.Clear();
                MessageResults.Clear();

                foreach (var chat in result.Data.Chats)
                {
                    ChatResults.Add(chat);
                }

                foreach (var msg in result.Data.Messages)
                {
                    MessageResults.Add(msg);
                }

                TotalMessagesCount = result.Data.TotalMessagesCount;
                HasMoreMessages = result.Data.HasMoreMessages;

                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(HasChatResults));
                OnPropertyChanged(nameof(HasMessageResults));
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
            var nextPage = (MessageResults.Count / 20) + 1;

            var url = $"api/messages/user/{userId}/search" +
                      $"?query={Uri.EscapeDataString(SearchQuery)}&page={nextPage}&pageSize=20";

            var result = await _apiClient.GetAsync<GlobalSearchResponseDTO>(url);

            if (result.Success && result.Data != null)
            {
                foreach (var msg in result.Data.Messages)
                {
                    MessageResults.Add(msg);
                }

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
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasChatResults));
        OnPropertyChanged(nameof(HasMessageResults));
    }

    public void ExitSearch()
    {
        SearchQuery = string.Empty;
        IsSearchMode = false;
        Clear();
    }
}