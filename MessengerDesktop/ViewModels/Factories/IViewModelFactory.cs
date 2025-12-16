namespace MessengerDesktop.ViewModels.Factories;

public interface IChatViewModelFactory
{
    ChatViewModel Create(int chatId, ChatsViewModel parent);
}

public interface IChatsViewModelFactory
{
    ChatsViewModel Create(MainMenuViewModel parent, bool isGroupMode);
}