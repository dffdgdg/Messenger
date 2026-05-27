using Desktop.ViewModels.Chat;

namespace Desktop.ViewModels.ChatList.Factories;

public interface IChatViewModelFactory
{
    ChatViewModel Create(ChatDto chat, ChatsViewModel parent, int? targetMessageId = null);
}

public class ChatViewModelFactory(ChatViewModelDependencies dependencies) : IChatViewModelFactory
{
    public ChatViewModel Create(ChatDto chat, ChatsViewModel parent, int? targetMessageId = null)
        => new(chat, parent, parent.Parent, dependencies,
            dependencies.PlatformService.MainWindow?.StorageProvider,
            targetMessageId);
}