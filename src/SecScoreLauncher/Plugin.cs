using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Extensions.Registry;
using SecRandom.PluginSdk;
using SecScoreLauncher.Services;
using SecScoreLauncher.Views.SettingsPages;

namespace SecScoreLauncher;

/// <summary>
///     SecRandom in-process plugin entrance. Registers the floating-window button (via a hosted service,
///     resolved against the Host's service provider, since <see cref="PluginBase.Initialize"/> runs before
///     the Host is built) and a settings page to configure the SecScore executable path.
/// </summary>
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var config = new SecScoreLauncherConfig(PluginConfigFolder);
        services.AddSingleton(config);
        services.AddHostedService<SecScoreLauncherService>();
        services.AddSettingsPage<SecScoreLauncherSettingsPage>("SecScore");
    }
}
