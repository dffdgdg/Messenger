using Core.Dialog.Shared;
using Core.Features.ChatList.ViewModels.Search;

namespace Core.Dialog.Search;

public sealed partial class SearchFiltersDialogViewModel : DialogBaseViewModel
{
    private readonly GlobalSearchManager _searchManager;
    private readonly Func<Task> _applyAction;
    private readonly Func<Task> _clearAction;

    private readonly string _mentionSnapshot;
    private readonly SearchContentFilter _contentSnapshot;
    private readonly SearchAuthorFilter _authorSnapshot;
    private readonly DateTimeOffset? _dateFromSnapshot;
    private readonly DateTimeOffset? _dateToSnapshot;
    private readonly SearchScopeMode _scopeSnapshot;
    private readonly SearchFilterItem? _selectedSenderSnapshot;
    private readonly SearchFilterItem? _selectedChatFilterSnapshot;
    private readonly string _senderSearchTextSnapshot;
    private readonly string _chatSearchTextSnapshot;

    public SearchFiltersDialogViewModel(GlobalSearchManager searchManager, Func<Task> applyAction, Func<Task> clearAction)
    {
        _searchManager = searchManager ?? throw new ArgumentNullException(nameof(searchManager));
        _applyAction = applyAction ?? throw new ArgumentNullException(nameof(applyAction));
        _clearAction = clearAction ?? throw new ArgumentNullException(nameof(clearAction));

        Title = "Фильтры";
        CanCloseOnBackgroundClick = true;

        _mentionSnapshot = _searchManager.MentionFilter;
        _contentSnapshot = _searchManager.ContentFilter;
        _authorSnapshot = _searchManager.AuthorFilter;
        _dateFromSnapshot = _searchManager.DateFromFilter;
        _dateToSnapshot = _searchManager.DateToFilter;
        _scopeSnapshot = _searchManager.SelectedScope;
        _selectedSenderSnapshot = _searchManager.SelectedSender;
        _selectedChatFilterSnapshot = _searchManager.SelectedChatFilter;
        _senderSearchTextSnapshot = _searchManager.SenderSearchText;
        _chatSearchTextSnapshot = _searchManager.ChatSearchText;
    }

    public GlobalSearchManager SearchManager => _searchManager;

    [RelayCommand]
    private void SetSearchContentFilter(SearchContentFilter filter) => SearchManager.ContentFilter = filter;

    [RelayCommand]
    private void SetSearchScope(SearchScopeMode scope) => SearchManager.UseScope(scope);

    [RelayCommand]
    private void SetSearchAuthorFilter(SearchAuthorFilter filter) => SearchManager.AuthorFilter = filter;

    [RelayCommand]
    private async Task Apply()
    {
        await _applyAction();
        await RequestCloseAsync();
    }

    [RelayCommand]
    private async Task ClearFilters()
    {
        SearchManager.ResetFilters();
        await _clearAction();
    }

    protected override Task Cancel()
    {
        RestoreSnapshot();
        return base.Cancel();
    }

    protected override Task CloseOnBackgroundClick()
    {
        RestoreSnapshot();
        return base.CloseOnBackgroundClick();
    }

    private void RestoreSnapshot()
    {
        SearchManager.SelectedSender = _selectedSenderSnapshot;
        SearchManager.SelectedChatFilter = _selectedChatFilterSnapshot;
        SearchManager.SenderSearchText = _senderSearchTextSnapshot;
        SearchManager.ChatSearchText = _chatSearchTextSnapshot;
        SearchManager.MentionFilter = _mentionSnapshot;
        SearchManager.ContentFilter = _contentSnapshot;
        SearchManager.AuthorFilter = _authorSnapshot;
        SearchManager.DateFromFilter = _dateFromSnapshot;
        SearchManager.DateToFilter = _dateToSnapshot;
        SearchManager.SelectedScope = _scopeSnapshot;
    }
}