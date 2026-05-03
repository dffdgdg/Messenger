using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Desktop.ViewModels.Chat;

namespace Desktop.Views.Chat;

public class MessagePartSelector : IDataTemplate
{
    public IDataTemplate? DeletedTemplate { get; set; }
    public IDataTemplate? VoiceTemplate { get; set; }
    public IDataTemplate? TextTemplate { get; set; }

    public Control? Build(object? param)
    {
        if (param is not MessageViewModel vm) return null;

        return (vm.IsDeleted, vm.ShowVoiceMessage) switch
        {
            (true, _) => DeletedTemplate?.Build(param),
            (_, true) => VoiceTemplate?.Build(param),
            _ => TextTemplate?.Build(param)
        };
    }

    public bool Match(object? data) => data is MessageViewModel;
}