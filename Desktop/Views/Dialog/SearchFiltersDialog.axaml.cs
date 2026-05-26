using Avalonia.Interactivity;
using Desktop.ViewModels.Dialog;
using Desktop.Views.Controls;

namespace Desktop.Views.Dialog;

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