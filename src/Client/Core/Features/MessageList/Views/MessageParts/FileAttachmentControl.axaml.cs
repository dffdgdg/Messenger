using Avalonia.Media.Imaging;
using Core.Features.Chat.ViewModels.Messages;

namespace Core.Features.Chat.Views.MessageParts;

public partial class FileAttachmentControl : UserControl
{
    public FileAttachmentControl()
    {
        InitializeComponent();

        var imageControl = this.FindControl<Image>("AttachmentImage");
        imageControl?.PropertyChanged += OnImagePropertyChanged;
    }

    private void OnImagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Image.SourceProperty && e.NewValue is Bitmap && DataContext is MessageFileViewModel vm && !vm.IsImageLoaded)
        {
            // Только помечаем загрузку — размеры контейнера не меняются
            vm.IsImageLoaded = true;
        }
    }
}