using Avalonia.Media.Imaging;
using Desktop.ViewModels.Chat;

namespace Desktop.Views.Chat;

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