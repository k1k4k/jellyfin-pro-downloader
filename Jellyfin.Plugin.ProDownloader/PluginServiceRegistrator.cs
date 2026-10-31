using Jellyfin.Plugin.ProDownloader.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ProDownloader;

/// <summary>
/// Enregistre l'injection du script et les services de téléchargement (10.11 et 12).
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
        serviceCollection.AddSingleton<ItemResolver>();
        serviceCollection.AddSingleton<JobStore>();
        serviceCollection.AddSingleton<TransferLimiter>();
        serviceCollection.AddSingleton<CrcCache>();
    }
}
