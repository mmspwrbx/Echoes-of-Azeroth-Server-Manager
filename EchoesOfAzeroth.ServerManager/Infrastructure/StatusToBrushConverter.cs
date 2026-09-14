using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Infrastructure;

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromRgb(143, 147, 164));
    private static readonly Brush PendingBrush = new SolidColorBrush(Color.FromRgb(255, 196, 92));
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(78, 214, 160));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 107, 122));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ComponentStatus.Running => RunningBrush,
        ComponentStatus.Starting or ComponentStatus.Stopping => PendingBrush,
        ComponentStatus.Error => ErrorBrush,
        _ => StoppedBrush
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
