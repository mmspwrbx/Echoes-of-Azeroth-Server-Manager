using System.Security.Principal;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class ValidationService
{
    private readonly AppSettings _settings;
    private readonly MySqlServiceManager _database;

    public ValidationService(AppSettings settings, MySqlServiceManager database)
    {
        _settings = settings;
        _database = database;
    }

    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        if (!Directory.Exists(_settings.ServerRootPath))
        {
            issues.Add(new ValidationIssue("ValidationServerRootMissing", _settings.ServerRootPath));
            return issues;
        }

        AddMissingFile(issues, _settings.GetExecutablePath(_settings.AuthServerExecutable), "ValidationAuthMissing");
        AddMissingFile(issues, _settings.GetExecutablePath(_settings.WorldServerExecutable), "ValidationWorldMissing");

        var configsPath = Path.Combine(_settings.ServerRootPath, "configs");
        if (!Directory.Exists(configsPath))
        {
            issues.Add(new ValidationIssue("ValidationConfigsMissing", configsPath));
        }
        else
        {
            AddMissingFile(issues, Path.Combine(configsPath, "authserver.conf"), "ValidationAuthConfigMissing");
            AddMissingFile(issues, Path.Combine(configsPath, "worldserver.conf"), "ValidationWorldConfigMissing");
        }

        if (!await _database.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            issues.Add(new ValidationIssue("ValidationMySqlMissing", _settings.MySqlServiceName));
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            issues.Add(new ValidationIssue("ValidationAdminRequired"));
        }

        return issues;
    }

    private static void AddMissingFile(List<ValidationIssue> issues, string path, string key)
    {
        if (!File.Exists(path))
        {
            issues.Add(new ValidationIssue(key, path));
        }
    }
}

