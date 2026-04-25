using Avalonia.Media.Imaging;
using MessengerDesktop.ViewModels.Chat;

namespace MessengerDesktop.Views.Chat;

public partial class FileAttachmentControl : UserControl
{
    public FileAttachmentControl()
    {
        InitializeComponent();

        var imageControl = this.FindControl<Image>("AttachmentImage");
        imageControl?.PropertyChanged += OnImagePropertyChanged;
    }

    private void OnImagePropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Image.SourceProperty && e.NewValue is Bitmap && DataContext is MessageFileViewModel vm && !vm.IsImageLoaded)
        {
            // Только помечаем загрузку — размеры контейнера не меняются
            vm.IsImageLoaded = true;
        }
    }
}