namespace EchoesOfAzeroth.ServerManager.Models;

public sealed record LogEntry(DateTimeOffset Timestamp, LogChannel Channel, string Message, bool IsError = false)
{
    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Message}";
}

