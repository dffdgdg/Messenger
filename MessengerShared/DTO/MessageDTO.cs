using System;
using System.Collections.Generic;

namespace MessengerShared.DTO
{
    public class MessageDTO
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public int SenderId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderAvatarUrl { get; set; }
        public string? Content { get; set; }
        public DateTime CreatedAt { get; set; }
        public PollDTO? Poll { get; set; }
        public List<UserMentionDTO> Mentions { get; set; } = [];
        public bool IsOwn { get; set; }
        public bool IsPrevSameSender { get; set; }
        public MessageDTO? PreviousMessage { get; set; }
        public bool IsMentioned { get; set; }
        public bool ShowSenderName 
        { 
            get
            {
                if (PreviousMessage == null)
                    return true;

                if (SenderId != PreviousMessage.SenderId)
                    return true;

                var timeDiff = CreatedAt - PreviousMessage.CreatedAt;
                return timeDiff.TotalMinutes > 5;
            }
            set { } 
        }
    }

    public class UserMentionDTO
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public int Length { get; set; }
    }
}