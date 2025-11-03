namespace MessengerShared.DTO
{
    public class ChatDTO
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsGroup { get; set; }
        public int CreatedById { get; set; }
        public DateTime? LastMessageDate { get; set; }
        public string? Avatar { get; set; }
    }
}