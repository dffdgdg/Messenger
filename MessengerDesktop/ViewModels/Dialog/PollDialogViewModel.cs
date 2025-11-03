using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerShared.DTO;
using System.Collections.ObjectModel;
using System;
using System.Linq;
using MessengerDesktop.ViewModels.Dialog;

namespace MessengerDesktop.ViewModels;

public partial class PollDialogViewModel : DialogViewModelBase
{
    private readonly int chatId;

    public PollDialogViewModel(int chatId)
    {
        this.chatId = chatId;
        SubscribeOptionItems(options);
    }

    [ObservableProperty]
    private string question = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OptionItem> options = [new OptionItem(), new OptionItem()];

    [ObservableProperty]
    private bool allowsMultipleAnswers = false;

    [ObservableProperty]
    private bool isAnonymous = true;

    public Action? CloseAction { get; set; }
    public Action<PollDTO>? CreateAction { get; set; }

    public bool CanCreate => !string.IsNullOrWhiteSpace(this.Question) && this.Options.Count >= 2 && this.Options.All(o => !string.IsNullOrWhiteSpace(o.Text));

    partial void OnQuestionChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    partial void OnOptionsChanged(ObservableCollection<OptionItem> value)
    {
        OnPropertyChanged(nameof(CanCreate));
        SubscribeOptionItems(value);
    }

    private void SubscribeOptionItems(ObservableCollection<OptionItem> optionItems)
    {
        optionItems.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(CanCreate));
            if (e.NewItems != null)
            {
                foreach (OptionItem item in e.NewItems)
                {
                    item.PropertyChanged += OptionItem_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (OptionItem item in e.OldItems)
                {
                    item.PropertyChanged -= OptionItem_PropertyChanged;
                }
            }
        };
        foreach (var item in optionItems)
        {
            item.PropertyChanged += OptionItem_PropertyChanged;
        }
    }

    private void OptionItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptionItem.Text))
            OnPropertyChanged(nameof(CanCreate));
    }

    [RelayCommand]
    private void AddOption() => this.Options.Add(new OptionItem());

    [RelayCommand]
    private void RemoveOption(OptionItem? item)
    {
        if (item != null && this.Options.Count > 2)
            this.Options.Remove(item);
    }

    [RelayCommand]
    private void Create()
    {
        if (!CanCreate) return;
        var poll = new PollDTO
        {
            ChatId = this.chatId,
            Question = this.Question,
            AllowsMultipleAnswers = this.AllowsMultipleAnswers,
            IsAnonymous = this.IsAnonymous,
            Options = [.. this.Options.Select((o, i) => new PollOptionDTO { OptionText = o.Text, Position = i })]
        };
        CreateAction?.Invoke(poll);
        CloseAction?.Invoke();
    }

    public partial class OptionItem : ObservableObject
    {
        [ObservableProperty]
        private string text = string.Empty;
    }
}
