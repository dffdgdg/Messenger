using Core.Features.Chat.ViewModels;

namespace Core.Features.ChatList.ViewModels.Factories;

public interface IChatViewModelFactory
{
    ChatViewModel Create(ChatDto chat, ChatListViewModel parent, int? targetMessageId = null);
}

public sealed class ChatViewModelFactory(
    ChatCoreServices core,
    MediaServices media,
    CallServices calls,
    CacheServices cache) : IChatViewModelFactory
{
    public ChatViewModel Create(ChatDto chat, ChatListViewModel parent, int? targetMessageId = null)
    {
        var vm = new ChatViewModel(chat, parent, parent.Parent, core, media, calls, cache, targetMessageId)
        {
            RequestGoBackToList = () => parent.ForceCloseChat()
        };

        return vm;
    }
}