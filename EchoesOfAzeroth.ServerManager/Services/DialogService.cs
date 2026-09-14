using System.Windows;

namespace EchoesOfAzeroth.ServerManager.Services;

public interface IDialogService
{
    bool Confirm(string message, string title);
    void ShowError(string message, string title);
    void ShowInformation(string message, string title);
}

public sealed class DialogService : IDialogService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}

