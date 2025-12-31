using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO.Chat.Poll;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace MessengerDesktop.ViewModels;

public partial class PollDialogViewModel : DialogBaseViewModel
{
    private const int MinOptions = 2;
    private const int MaxOptions = 10;

    private readonly int _chatId;

    [ObservableProperty]
    private string _question = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OptionItem> _options;

    [ObservableProperty]
    private bool _allowsMultipleAnswers;

    [ObservableProperty]
    private bool _isAnonymous = true;

    public Action<PollDTO>? CreateAction { get; set; }

    public bool CanAddOption => Options.Count < MaxOptions;
    public bool CanRemoveOption => Options.Count > MinOptions;
    public bool CanCreate =>
        !string.IsNullOrWhiteSpace(Question) &&
        Options.Count >= MinOptions &&
        Options.All(o => !string.IsNullOrWhiteSpace(o.Text));

    public PollDialogViewModel(int chatId)
    {
        _chatId = chatId;
        Title = "Создать опрос";
        CanCloseOnBackgroundClick = true;

        _options = [new(), new()];
        SubscribeToOptions(_options);
    }

    partial void OnQuestionChanged(string value)
    {
        if (CanCreate) ErrorMessage = null;
        NotifyStateChanged();
    }

    partial void OnOptionsChanged(ObservableCollection<OptionItem>? oldValue,ObservableCollection<OptionItem> newValue)
    {
        if (oldValue != null)
            UnsubscribeFromOptions(oldValue);

        SubscribeToOptions(newValue);
        NotifyStateChanged();
    }

    private void SubscribeToOptions(ObservableCollection<OptionItem> options)
    {
        options.CollectionChanged += OnOptionsCollectionChanged;
        foreach (var item in options)
            item.PropertyChanged += OnOptionPropertyChanged;
    }

    private void UnsubscribeFromOptions(ObservableCollection<OptionItem> options)
    {
        options.CollectionChanged -= OnOptionsCollectionChanged;
        foreach (var item in options)
            item.PropertyChanged -= OnOptionPropertyChanged;
    }

    private void OnOptionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (OptionItem item in e.OldItems)
                item.PropertyChanged -= OnOptionPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (OptionItem item in e.NewItems)
                item.PropertyChanged += OnOptionPropertyChanged;
        }

        NotifyStateChanged();
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptionItem.Text))
        {
            if (CanCreate) ErrorMessage = null;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanAddOption));
        OnPropertyChanged(nameof(CanRemoveOption));
        CreateCommand.NotifyCanExecuteChanged();
        AddOptionCommand.NotifyCanExecuteChanged();
        RemoveOptionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddOption))]
    private void AddOption() => Options.Add(new OptionItem());

    [RelayCommand(CanExecute = nameof(CanRemoveOption))]
    private void RemoveOption(OptionItem? item)
    {
        if (item != null)
            Options.Remove(item);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        if (!CanCreate)
        {
            ErrorMessage = "Заполните вопрос и минимум 2 варианта ответа";
            return;
        }

        CreateAction?.Invoke(new PollDTO
        {
            ChatId = _chatId,
            Question = Question.Trim(),
            AllowsMultipleAnswers = AllowsMultipleAnswers,
            IsAnonymous = IsAnonymous,
            Options = [.. Options.Select((o, i) => new PollOptionDTO
            {
                OptionText = o.Text.Trim(),
                Position = i
            })]
        });

        RequestClose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            UnsubscribeFromOptions(Options);

        base.Dispose(disposing);
    }

    public partial class OptionItem : ObservableObject
    {
        [ObservableProperty]
        private string _text = string.Empty;
    }
}