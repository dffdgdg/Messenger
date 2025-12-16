// ViewModels/Chat/MessageViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MessengerDesktop.ViewModels.Chat
{
    public partial class MessageViewModel : ObservableObject
    {
        public MessageDTO Message { get; }

        [ObservableProperty]
        private string? senderName;

        [ObservableProperty]
        private string? senderAvatarUrl;

        [ObservableProperty]
        private ObservableCollection<MessageFileViewModel> fileViewModels = [];

        public int Id => Message.Id;
        public int ChatId => Message.ChatId;
        public int SenderId => Message.SenderId;
        public string? Content => Message.Content;
        public DateTime CreatedAt => Message.CreatedAt;
        public DateTime? EditedAt => Message.EditedAt;
        public bool IsEdited => Message.IsEdited;
        public bool IsDeleted => Message.IsDeleted;
        public bool IsOwn => Message.IsOwn;
        public bool ShowSenderName => Message.ShowSenderName;
        public PollDTO? Poll => Message.Poll;

        public bool HasFiles => FileViewModels.Count > 0;
        public bool HasImages => FileViewModels.Any(f => f.IsImage);
        public bool HasNonImageFiles => FileViewModels.Any(f => !f.IsImage);

        public MessageViewModel(MessageDTO message)
        {
            Message = message;
            SenderName = message.SenderName;
            SenderAvatarUrl = message.SenderAvatarUrl;

            if (message.Files?.Count > 0)
            {
                FileViewModels = new ObservableCollection<MessageFileViewModel>(
                    message.Files.Select(f => new MessageFileViewModel(f))
                );
            }
        }
    }
}