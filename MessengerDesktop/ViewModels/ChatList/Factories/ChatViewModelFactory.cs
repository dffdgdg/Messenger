using MessengerDesktop.ViewModels.Chat;

namespace MessengerDesktop.ViewModels.ChatList.Factories;

public interface IChatViewModelFactory
{
    ChatViewModel Create(int chatId, ChatsViewModel parent);
}

public class ChatViewModelFactory(ChatViewModelDependencies dependencies) : IChatViewModelFactory
{
    public ChatViewModel Create(int chatId, ChatsViewModel parent) => new(chatId, parent, parent.Parent, dependencies,
        dependencies.PlatformService.MainWindow?.StorageProvider);
}