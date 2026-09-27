using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
///     <para>
///         The button is intentionally left registered for the whole process lifetime and is never
///         unregistered: SecRandom's host currently offers no safe unregister point for plugins.
///     </para>
///     <list type="bullet">
///         <item>
///             Unregistering from this hosted service's <c>StopAsync</c> crashes the app on exit. It runs
///             only during host shutdown, after SecRandom has torn down its DI host (<c>IAppHost.Host</c>
///             disposed/null); the registry's <c>Changed</c> event then makes the floating window queue a
///             refresh that throws "Service IFloatingWindowButtonRegistry is null!".
///         </item>
///         <item>
///             Unregistering earlier on <c>IAppLifecycleService.AppStopping</c> avoids that crash but the
///             resulting refresh hits SecRandom's stale-button pruning, which removes our id from
///             <c>settings.json</c> so the button silently disappears after every clean exit.
///         </item>
///     </list>
///     <para>
///         Because the registry is process-scoped and never persisted, leaving the button registered is
///         harmless: it disappears with the process, and disabling or uninstalling this plugin requires an
///         application restart anyway (the registry is rebuilt on each start).
///     </para>
/// </summary>
public sealed class SecScoreLauncherService(
    IFloatingWindowButtonRegistry buttonRegistry,
    SecScoreLauncherConfig config,
    ILogger<SecScoreLauncherService> logger,
    IServiceProvider serviceProvider) : IHostedService
{
    private const string ButtonId = "secscore.launcher";
    private readonly IFloatingWindowButtonRegistry _buttonRegistry = buttonRegistry;
    private readonly SecScoreLauncherConfig _config = config;
    private readonly ILogger<SecScoreLauncherService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly object _registrationGate = new();
    private CancellationTokenSource? _temporaryRegistrationCts;
    private INotifyPropertyChanged? _quickDrawViewModel;
    private PropertyChangedEventHandler? _quickDrawPropertyChangedHandler;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _buttonRegistry.Register(new FloatingWindowButtonDescriptor(
            ButtonId,
            FluentIcons.AppsFilled,
            _config.ButtonLabel,
            OnClick));
        _logger.LogInformation("Registered SecScore floating-window button.");
        AttachQuickDrawListener();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Intentionally no Unregister call - see the class summary: the host has no safe unregister
        // point, and the process is about to exit so the process-scoped registry dies with it.
        lock (_registrationGate)
        {
            _temporaryRegistrationCts?.Cancel();
            _temporaryRegistrationCts?.Dispose();
            _temporaryRegistrationCts = null;
        }
        if (_quickDrawViewModel is not null && _quickDrawPropertyChangedHandler is not null)
            _quickDrawViewModel.PropertyChanged -= _quickDrawPropertyChangedHandler;
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    private void AttachQuickDrawListener()
    {
        var viewModelType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("SecRandom.ViewModels.MainPages.QuickDrawPageViewModel"))
            .FirstOrDefault(type => type is not null);
        if (viewModelType is null)
        {
            _logger.LogWarning("SecRandom QuickDraw view model type was not found; temporary registration is disabled.");
            return;
        }

        var viewModel = _serviceProvider.GetService(viewModelType) as INotifyPropertyChanged;
        if (viewModel is null)
        {
            _logger.LogWarning("SecRandom QuickDraw view model could not be resolved; temporary registration is disabled.");
            return;
        }

        _quickDrawViewModel = viewModel;
        _quickDrawPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName != "LastDrawnStudent")
                return;
            var student = viewModelType.GetProperty("LastDrawnStudent")?.GetValue(viewModel);
            var name = student?.GetType().GetProperty("Name")?.GetValue(student) as string;
            if (!string.IsNullOrWhiteSpace(name))
                _ = RegisterTemporaryStudentAsync(name.Trim());
        };
        viewModel.PropertyChanged += _quickDrawPropertyChangedHandler;
        _logger.LogInformation("Attached SecScore temporary registration to SecRandom QuickDraw.");
    }

    private async Task RegisterTemporaryStudentAsync(string studentName)
    {
        CancellationTokenSource registrationCts;
        lock (_registrationGate)
        {
            _temporaryRegistrationCts?.Cancel();
            _temporaryRegistrationCts?.Dispose();
            registrationCts = new CancellationTokenSource();
            _temporaryRegistrationCts = registrationCts;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildApiUri("/api/v1/quick-students"));
            AddApiToken(request);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { student_name = studentName, replace = true }),
                Encoding.UTF8,
                "application/json");
            using var response = await _httpClient.SendAsync(request, registrationCts.Token).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(registrationCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SecScore temporary student registration failed: HTTP {StatusCode} {Body}", response.StatusCode, responseBody);
                return;
            }

            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("student_id", out var studentIdElement) ||
                !studentIdElement.TryGetInt32(out var studentId))
            {
                _logger.LogWarning("SecScore registration response did not contain a student id.");
                return;
            }

            _logger.LogInformation("Registered temporary SecScore student {StudentName} for 3 minutes.", studentName);
            await Task.Delay(TimeSpan.FromMinutes(3), registrationCts.Token).ConfigureAwait(false);
            using var deleteRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                BuildApiUri($"/api/v1/quick-students/{studentId}"));
            AddApiToken(deleteRequest);
            using var deleteResponse = await _httpClient.SendAsync(deleteRequest, registrationCts.Token).ConfigureAwait(false);
            if (!deleteResponse.IsSuccessStatusCode && deleteResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
                _logger.LogWarning("SecScore temporary student removal failed: HTTP {StatusCode}", deleteResponse.StatusCode);
        }
        catch (OperationCanceledException) when (registrationCts.IsCancellationRequested)
        {
            // A newer flash draw superseded this temporary registration.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "SecScore temporary student registration failed.");
        }
    }

    private Uri BuildApiUri(string path)
    {
        var baseUrl = _config.SecScoreApiUrl.TrimEnd('/');
        return new Uri($"{baseUrl}{path}", UriKind.Absolute);
    }

    private void AddApiToken(HttpRequestMessage request)
    {
        var token = _config.SecScoreApiToken.Trim();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void OnClick()
    {
        _logger.LogInformation("SecScore floating-window button clicked.");
        // The click runs on the UI thread; opening/focusing an external app is offloaded so the
        // floating window stays responsive.
        Task.Run(() => SecScoreWindowFocus.OpenOrFocus(_config, _logger));
    }
}
