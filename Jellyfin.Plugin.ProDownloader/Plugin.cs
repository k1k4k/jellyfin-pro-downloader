using System.Globalization;
using System.Reflection;
using Jellyfin.Plugin.ProDownloader.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ProDownloader;

/// <summary>
/// Jellyfin Pro Downloader : remplace le « Tout télécharger » du client web (un téléchargement
/// navigateur par enfant, tous en même temps) par une fenêtre de sélection et un gestionnaire de
/// téléchargements (dossier, file d'attente ou ZIP), avec des limites appliquées par le serveur.
/// Le script client (jellyfin-pro-downloader.js) est embarqué dans la DLL, servi par
/// <see cref="Controllers.ProDownloaderController"/> et injecté dans index.html à la volée par
/// <see cref="Services.ScriptInjectionStartupFilter"/>.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Nom du fichier script tel qu'exposé par l'API du plugin.</summary>
    public const string ScriptFileName = "jellyfin-pro-downloader.js";

    /// <summary>Segment de route de l'API du plugin.</summary>
    public const string RoutePrefix = "ProDownloader";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Jellyfin Pro Downloader";

    public override Guid Id => Guid.Parse("114272b4-a63b-4036-84be-843fc6552d61");

    public override string Description =>
        "Télécharger une série, un album ou une collection sans lancer des centaines de téléchargements d'un coup : fenêtre de sélection, gestionnaire de téléchargements, dossier, file d'attente ou ZIP. "
        + "Download a series, album or collection without firing hundreds of downloads at once: picker, download manager, folder, queue or ZIP.";

    /// <summary>Version du plugin, utilisée pour le cache-busting de l'URL du script.</summary>
    public static string ScriptVersion { get; } =
        (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)).ToString(3);

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
