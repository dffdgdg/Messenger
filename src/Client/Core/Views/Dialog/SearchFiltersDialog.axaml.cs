using Avalonia.Interactivity;
using Core.ViewModels.Dialog;
using Core.Views.Controls;

namespace Core.Views.Dialog;

public partial class SearchFiltersDialog : UserControl
{
    public SearchFiltersDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SearchFiltersDialogViewModel vm)
        {
            vm.SearchManager.SenderSuggestionsLoadingChanged += isLoading =>
            {
                if (this.FindControl<FilterAutocomplete>("SenderAutocomplete") is var ctrl && ctrl != null)
                {
                    ctrl.SetLoadingState(isLoading);
                }
            };

            vm.SearchManager.SenderSuggestionsReady += () =>
            {
                if (this.FindControl<FilterAutocomplete>("SenderAutocomplete") is var ctrl && ctrl != null)
                {
                    ctrl.OnSuggestionsReady();
                }
            };

            vm.SearchManager.ChatSuggestionsReady += () =>
            {
                if (this.FindControl<FilterAutocomplete>("ChatAutocomplete") is var ctrl && ctrl != null)
                {
                    ctrl.OnSuggestionsReady();
                }
            };
        }
    }
}