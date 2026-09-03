using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Icons;
using SecScoreLauncher.Win32;

namespace SecScoreLauncher.Services;

/// <summary>
///     Hosted service that contributes the "SecScore" button to SecRandom's floating window. Registered
///     as a hosted service (instead of inside <see cref="PluginBase.Initialize"/>) so DI is available to
///     resolve the runtime <see cref="IFloatingWindowButtonRegistry"/> singleton, which only exists after
///     the Host is built.
/// </summary>
public sealed class SecScoreLauncherService(
    IFloatingWindowButtonRegistry buttonRegistry,
    SecScoreLauncherConfig config,
    ILogger<SecScoreLauncherService> logger) : IHostedService
{
    private const string ButtonId = "secscore.launcher";
    private readonly IFloatingWindowButtonRegistry _buttonRegistry = buttonRegistry;
    private readonly SecScoreLauncherConfig _config = config;
    private readonly ILogger<SecScoreLauncherService> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _buttonRegistry.Register(new FloatingWindowButtonDescriptor(
            ButtonId,
            FluentIcons.AppsFilled,
            _config.ButtonLabel,
            OnClick));
        _logger.LogInformation("Registered SecScore floating-window button.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _buttonRegistry.Unregister(ButtonId);
        return Task.CompletedTask;
    }

    private void OnClick()
    {
        _logger.LogInformation("SecScore floating-window button clicked.");
        // The click runs on the UI thread; opening/focusing an external app is offloaded so the
        // floating window stays responsive.
        Task.Run(() => SecScoreWindowFocus.OpenOrFocus(_config, _logger));
    }
}
