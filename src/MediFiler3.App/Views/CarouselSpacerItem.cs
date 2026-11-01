using System.ComponentModel;

namespace MediFiler3.Views;

public sealed class CarouselSpacerItem : INotifyPropertyChanged
{
    private double _width;

    public double Width
    {
        get => _width;
        set
        {
            if (Math.Abs(_width - value) < 0.1)
            {
                return;
            }

            _width = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
        }
    }

    public bool IsSpacer => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CarouselSpacerItem(double width)
    {
        _width = width;
    }
}
