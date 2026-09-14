using System.Text.Json;

namespace EchoesOfAzeroth.ServerManager.Services;

public static class SelfTestRunner
{
    public static async Task<int> RunAsync(
        AppRuntime runtime,
        string reportPath,
        bool includeUiSmokeTest,
        CancellationToken cancellationToken)
    {
        var originalLanguage = runtime.Settings.Language;
        var results = new Dictionary<string, object?>();
        try
        {
            await runtime.SettingsService.SaveAsync(runtime.Settings, cancellationToken).ConfigureAwait(false);
            var reloaded = await runtime.SettingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            results["settingsRoundTrip"] = reloaded.ServerRootPath == runtime.Settings.ServerRootPath &&
                                           reloaded.Language == runtime.Settings.Language;

            runtime.Localization.SetCulture("en-US");
            var english = runtime.Localization["ServerManager"];
            runtime.Localization.SetCulture("ru-RU");
            var russian = runtime.Localization["ServerManager"];
            results["englishLocalization"] = english == "Server Manager";
            results["russianLocalization"] = russian == "Менеджер сервера";

            await runtime.Monitoring.RefreshNowAsync(cancellationToken).ConfigureAwait(false);
            var issues = await runtime.Validation.ValidateAsync(cancellationToken).ConfigureAwait(false);
            results["mysqlServiceDetected"] = await runtime.Database.ExistsAsync(cancellationToken).ConfigureAwait(false);
            results["mysqlStatus"] = runtime.Database.Status.ToString();
            results["mysqlPortOpen"] = runtime.Database.PortOpen;
            results["authProcessStatus"] = runtime.AuthServer.Status.ToString();
            results["authProcessId"] = runtime.AuthServer.ProcessId;
            results["authPortOpen"] = runtime.AuthServer.PortOpen;
            results["worldProcessStatus"] = runtime.WorldServer.Status.ToString();
            results["worldProcessId"] = runtime.WorldServer.ProcessId;
            results["worldPortOpen"] = runtime.WorldServer.PortOpen;
            results["validationIssues"] = issues.Select(x => new { x.ResourceKey, x.Detail }).ToArray();
            if (includeUiSmokeTest)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var window = new MainWindow();
                    window.Measure(new System.Windows.Size(1180, 850));
                    window.Arrange(new System.Windows.Rect(0, 0, 1180, 850));
                    window.UpdateLayout();
                    window.Close();
                });
                results["uiWindowConstructed"] = true;
            }

            results["success"] = (bool)results["settingsRoundTrip"]! &&
                                 (bool)results["englishLocalization"]! &&
                                 (bool)results["russianLocalization"]! &&
                                 (bool)results["mysqlServiceDetected"]! &&
                                 (!includeUiSmokeTest || results.TryGetValue("uiWindowConstructed", out var uiConstructed) && uiConstructed is true) &&
                                 issues.All(x => x.ResourceKey == "ValidationAdminRequired");
        }
        catch (Exception exception)
        {
            results["success"] = false;
            results["exception"] = exception.ToString();
        }
        finally
        {
            runtime.Settings.Language = originalLanguage;
            runtime.Localization.SetCulture(originalLanguage);
        }

        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        await File.WriteAllTextAsync(
            fullReportPath,
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
        return results.TryGetValue("success", out var success) && success is true ? 0 : 1;
    }
}
