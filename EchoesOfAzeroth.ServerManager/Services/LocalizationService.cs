using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly ResourceManager _resourceManager = new(
        "EchoesOfAzeroth.ServerManager.Resources.Resources",
        typeof(LocalizationService).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _resourceManager.GetString(key, _culture) ?? $"[{key}]";

    public CultureInfo Culture => _culture;

    public void SetCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName is "ru-RU" ? "ru-RU" : "en-US");
        if (Equals(_culture, culture))
        {
            return;
        }

        _culture = culture;
        CultureInfo.CurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
    }
}

