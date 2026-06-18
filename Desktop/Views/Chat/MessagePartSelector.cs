using Avalonia.Controls.Templates;
using Core.ViewModels.Chat;

namespace Desktop.Views.Chat;

public class MessagePartSelector : IDataTemplate
{
    public required IDataTemplate VoiceTemplate { get; set; }
    public required IDataTemplate TextTemplate { get; set; }
    public required IDataTemplate PollTemplate { get; set; }

    public Control? Build(object? param)
    {
        if (param is not MessageViewModel vm) return null;
        if (vm.OriginalHasPoll) return PollTemplate.Build(param);
        if (vm.OriginalIsVoiceMessage) return VoiceTemplate.Build(param);
        return TextTemplate.Build(param);
    }

    public bool Match(object? data) => data is MessageViewModel;
}