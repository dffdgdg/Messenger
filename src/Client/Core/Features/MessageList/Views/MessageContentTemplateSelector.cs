using Avalonia.Controls.Templates;
using Core.Features.Chat.ViewModels.Messages;

namespace Core.Features.MessageList.Views;

public class MessageContentTemplateSelector : IDataTemplate
{
    public IDataTemplate? VoiceTemplate { get; set; }
    public IDataTemplate? PollTemplate { get; set; }
    public IDataTemplate? TextTemplate { get; set; }

    public Control? Build(object? param)
    {
        if (param is not MessageViewModel vm) return null;

        var template = vm.OriginalIsVoiceMessage ? VoiceTemplate
            : vm.OriginalHasPoll ? PollTemplate
            : TextTemplate;

        return template?.Build(param);
    }

    public bool Match(object? data) => data is MessageViewModel;
}