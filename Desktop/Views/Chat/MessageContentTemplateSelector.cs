using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Desktop.ViewModels.Chat;

namespace Desktop.Views.Chat;

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