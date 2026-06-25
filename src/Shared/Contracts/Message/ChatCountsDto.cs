namespace Shared.Contracts.Message;

public class ChatCountsDto
{
    public int MediaCount { get; set; }
    public int FilesCount { get; set; }
    public int PollsCount { get; set; }
    public int PinnedCount { get; set; }
}