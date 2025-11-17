using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace MessengerDesktop.ViewModels
{
    /// <summary>
    /// ViewModel диалога создани€ опроса.
    /// </summary>
    public partial class PollDialogViewModel : DialogBaseViewModel
    {
        private readonly int _chatId;

        public PollDialogViewModel(int chatId)
        {
            _chatId = chatId;
            Title = "—оздать опрос";
            CanCloseOnBackgroundClick = true;
            SubscribeOptionItems(Options);
        }

        [ObservableProperty]
        private string _question = string.Empty;

        [ObservableProperty]
        private ObservableCollection<OptionItem> _options = [new OptionItem(), new OptionItem()];

        [ObservableProperty]
        private bool _allowsMultipleAnswers = false;

        [ObservableProperty]
        private bool _isAnonymous = true;

        /// <summary>Callback дл€ создани€ опроса</summary>
        public Action<PollDTO>? CreateAction { get; set; }

        public bool CanCreate => !string.IsNullOrWhiteSpace(Question) && Options.Count >= 2 && Options.All(o => !string.IsNullOrWhiteSpace(o.Text));

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
                    foreach (OptionItem item in e.NewItems)
                        item.PropertyChanged += OptionItem_PropertyChanged;
                if (e.OldItems != null)
                    foreach (OptionItem item in e.OldItems) 
                        item.PropertyChanged -= OptionItem_PropertyChanged;
            };

            foreach (var item in optionItems)
            {
                item.PropertyChanged += OptionItem_PropertyChanged;
            }
        }

        private void OptionItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OptionItem.Text))
                OnPropertyChanged(nameof(CanCreate));
        }

        [RelayCommand]
        private void AddOption() => Options.Add(new OptionItem());

        [RelayCommand]
        private void RemoveOption(OptionItem? item)
        {
            if (item != null && Options.Count > 2)
                Options.Remove(item);
        }

        [RelayCommand]
        private void Create()
        {
            if (!CanCreate)
            {
                ErrorMessage = "«аполните вопрос и минимум 2 варианта ответа";
                return;
            }

            var poll = new PollDTO
            {
                ChatId = _chatId,
                Question = Question,
                AllowsMultipleAnswers = AllowsMultipleAnswers,
                IsAnonymous = IsAnonymous,
                Options = [.. Options.Select((o, i) => new PollOptionDTO
                {
                    OptionText = o.Text,
                    Position = i
                })]
            };

            CreateAction?.Invoke(poll);
            RequestClose(); 
        }

        public partial class OptionItem : ObservableObject
        {
            [ObservableProperty]
            private string _text = string.Empty;
        }
    }
}