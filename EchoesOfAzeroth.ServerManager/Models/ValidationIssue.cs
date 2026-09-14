namespace EchoesOfAzeroth.ServerManager.Models;

public sealed record ValidationIssue(string ResourceKey, string? Detail = null);

