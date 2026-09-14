using System.Text;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class LoggingService : IAsyncDisposable
{
    private string _logDirectory;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private readonly HashSet<Task> _pendingWrites = [];
    private Task? _disposeTask;

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
            lock (_lifecycleSync)
            {
                if (_disposeTask is not null)
                {
                    return;
                }

                var writeTask = PersistManagerEntryAsync(entry);
                _pendingWrites.Add(writeTask);
                _ = writeTask.ContinueWith(completed =>
                {
                    lock (_lifecycleSync)
                    {
                        _pendingWrites.Remove(completed);
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private async Task PersistManagerEntryAsync(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, $"manager-{entry.Timestamp:yyyy-MM-dd}.log");
            var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] {(entry.IsError ? "ERROR" : "INFO ")} {entry.Message}{Environment.NewLine}";
            await _fileGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(path, line, Encoding.UTF8).ConfigureAwait(false);
            }
            finally
            {
                _fileGate.Release();
            }
        }
        catch
        {
            // Logging must never crash service management.
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleSync)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync(_pendingWrites.ToArray()));
        }
    }

    private async Task DisposeCoreAsync(Task[] pendingWrites)
    {
        await Task.WhenAll(pendingWrites).ConfigureAwait(false);
        _fileGate.Dispose();
    }
}
