using System.Text;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class LoggingService : IAsyncDisposable
{
    private string _logDirectory;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    public LoggingService(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public event EventHandler<LogEntry>? EntryAdded;

    public void SetLogDirectory(string logDirectory) => _logDirectory = logDirectory;

    public void Info(LogChannel channel, string message) => Write(new LogEntry(DateTimeOffset.Now, channel, message));

    public void Error(LogChannel channel, string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write(new LogEntry(DateTimeOffset.Now, channel, detail, true));
    }

    private void Write(LogEntry entry)
    {
        EntryAdded?.Invoke(this, entry);
        if (entry.Channel == LogChannel.Manager)
        {
            _ = PersistManagerEntryAsync(entry, _shutdown.Token);
        }
    }

    private async Task PersistManagerEntryAsync(LogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, $"manager-{entry.Timestamp:yyyy-MM-dd}.log");
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] {(entry.IsError ? "ERROR" : "INFO ")} {entry.Message}{Environment.NewLine}";
            await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _fileGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Logging must never crash service management.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        await _fileGate.WaitAsync().ConfigureAwait(false);
        _fileGate.Release();
        _fileGate.Dispose();
        _shutdown.Dispose();
    }
}
