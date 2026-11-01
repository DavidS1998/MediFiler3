using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using MediFiler3.Domain.Files;

namespace MediFiler3.Views;

public sealed class CarouselThumbnailItem : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;
    private bool _isCurrent;

    public FileEntry File { get; }
    public string Name => File.Name;
    public string PlaceholderText => string.IsNullOrWhiteSpace(File.Extension) ? "FILE" : File.Extension.TrimStart('.').ToUpperInvariant();
    public bool IsCurrent
    {
        get => _isCurrent;
        private set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            OnPropertyChanged();
        }
    }
    public bool IsSpacer => false;
    public Task<BitmapSource?>? ThumbnailTask { get; set; }
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => Thumbnail is not null;

    public void SetCurrent(bool isCurrent) => IsCurrent = isCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CarouselThumbnailItem(FileEntry file, bool isCurrent)
    {
        File = file;
        IsCurrent = isCurrent;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
