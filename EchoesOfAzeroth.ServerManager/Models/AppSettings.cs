namespace EchoesOfAzeroth.ServerManager.Models;

public sealed class AppSettings
{
    public string ServerRootPath { get; set; } = @"C:\Projects\Development\Echoes of Azeroth\server";
    public string MySqlServiceName { get; set; } = "MySQL84";
    public int MySqlPort { get; set; } = 3306;
    public string AuthServerExecutable { get; set; } = "authserver.exe";
    public string AuthServerArguments { get; set; } = @"-c configs\authserver.conf";
    public int AuthServerPort { get; set; } = 3724;
    public string WorldServerExecutable { get; set; } = "worldserver.exe";
    public string WorldServerArguments { get; set; } = @"-c configs\worldserver.conf";
    public int WorldServerPort { get; set; } = 8085;
    public int StartupTimeoutSeconds { get; set; } = 120;
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;
    public string Language { get; set; } = "en-US";
    public string LogsDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EchoesOfAzeroth",
        "ServerManager",
        "logs");

    public string GetExecutablePath(string executable) =>
        Path.IsPathRooted(executable) ? executable : Path.Combine(ServerRootPath, executable);
}

