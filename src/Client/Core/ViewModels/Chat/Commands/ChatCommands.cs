using System.Windows.Input;

namespace Core.ViewModels.Chat.Commands;

/// <summary>
/// Mutable-контейнер команд чата. Создаётся пустым, заполняется после
/// инициализации всех суб-viewmodel'ей. Один экземпляр разделяется
/// между всеми MessageViewModel через MessageManager.
/// </summary>
public sealed class ChatCommands
{
    public ICommand? Edit { get; set; }
    public ICommand? Copy { get; set; }
    public ICommand? Delete { get; set; }
    public ICommand? TogglePin { get; set; }
    public ICommand? OpenProfile { get; set; }
    public ICommand? Reply { get; set; }
    public ICommand? ScrollToReply { get; set; }
    public ICommand? Forward { get; set; }
    public ICommand? ShowPollResults { get; set; }
}