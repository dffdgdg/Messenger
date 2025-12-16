namespace MessengerShared.DTO
{
    public class UpdateChatDTO
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool? IsGroup { get; set; }
    }
}