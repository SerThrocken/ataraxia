using Hermes.Client.Core;
using Hermes.Client.Infrastructure;
using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Hermes.Client.WinUI3;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();

        // Tailscale-first: the gateway binds 0.0.0.0:8642 on the Acer;
        // loopback is the fallback when running on the Acer itself.
        services.AddSingleton(new GatewayOptions { BaseUrl = new Uri("http://100.101.250.118:8642") });
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IConnectionState, ConnectionStateService>();
        services.AddSingleton<ConnectionTester>();

        // Shared gateway client (chat worker): concrete GatewayClient over the
        // shared GatewayClientBase — no HTTP/auth duplication. The connection
        // flow owns the key/URL UX; the client is pointed at the saved values
        // once they load (InitializeGatewayClientAsync below).
        services.AddSingleton<IGatewayClient>(sp =>
            new GatewayClient(new HttpClient(), sp.GetRequiredService<GatewayOptions>()));

        services.AddTransient<ChatViewModel>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<ModelsViewModel>();
        services.AddTransient<SkillsViewModel>();

        // Singleton: shared by the first-run ConnectPage and the Settings page.
        services.AddSingleton<SettingsViewModel>();

        Services = services.BuildServiceProvider();

        _window = new MainWindow();
        _window.Activate();

        _ = InitializeGatewayClientAsync(Services);
    }

    private static async Task InitializeGatewayClientAsync(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingsService>();
        var secrets = services.GetRequiredService<ISecretStore>();
        var client = (GatewayClient)services.GetRequiredService<IGatewayClient>();

        var saved = await settings.LoadAsync<ConnectionSettings>(SettingsViewModel.SettingsName);
        if (saved is not null && ConnectionValidation.IsValidBaseUrl(saved.BaseUrl))
        {
            client.SetBaseUrl(new Uri(saved.BaseUrl.Trim()));
        }

        client.SetApiKey(await secrets.GetAsync(SettingsViewModel.ApiKeySecretName));
    }
}
