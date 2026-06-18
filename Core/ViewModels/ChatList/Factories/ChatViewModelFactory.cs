using Core.ViewModels.Chat;

namespace Core.ViewModels.ChatList.Factories;

public interface IChatViewModelFactory
{
    ChatViewModel Create(ChatDto chat, ChatsViewModel parent, int? targetMessageId = null);
}

public class ChatViewModelFactory(ChatViewModelDependencies dependencies) : IChatViewModelFactory
{
    public ChatViewModel Create(ChatDto chat, ChatsViewModel parent, int? targetMessageId = null)
    {
        var vm = new ChatViewModel(chat,parent,parent.Parent,dependencies, targetMessageId)
        {
            RequestGoBackToList = () => parent.ForceCloseChat()
        };

        return vm;
    }
}