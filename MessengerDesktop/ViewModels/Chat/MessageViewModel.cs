using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;

namespace MessengerDesktop.ViewModels.Chat
{
    public partial class MessageViewModel(MessageDTO message) : ObservableObject
    {
        public int Id => Message.Id;
        public int SenderId => Message.SenderId;
        public MessageDTO Message { get; } = message;

        [ObservableProperty]
        private Bitmap? avatarBitmap;

        [ObservableProperty]
        private string? senderName = message.SenderName;

        [ObservableProperty]
        private string? senderAvatarUrl = message.SenderAvatarUrl;

        public object? Poll => Message.Poll;
        public string Content => Message.Content;

        public bool IsOwn => Message.IsOwn;

        public bool ShowSenderName => Message.ShowSenderName;
        public DateTime CreatedAt => Message.CreatedAt;

        // Expose attachments for the view
        public ObservableCollection<MessengerShared.DTO.MessageFileDTO> Files { get; } = new ObservableCollection<MessengerShared.DTO.MessageFileDTO>(message.Files ?? []);
    }

}