using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ProDownloader.Configuration;

/// <summary>Configuration du plugin (Tableau de bord → Plugins → Jellyfin Pro Downloader).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Injecter le script dans le client web. Décocher rend au client son téléchargement d'origine
    /// (prise en compte au prochain rechargement de la page web, sans redémarrage).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Mode Dossier (navigateurs Chromium sur ordinateur, en HTTPS ou localhost).</summary>
    public bool AllowFolderMode { get; set; } = true;

    /// <summary>Mode File d'attente (tous les navigateurs).</summary>
    public bool AllowQueueMode { get; set; } = true;

    /// <summary>Mode ZIP (tous les navigateurs).</summary>
    public bool AllowZipMode { get; set; } = true;

    /// <summary>Fichiers envoyés en même temps à un même utilisateur (1 à 10).</summary>
    public int MaxConcurrentPerUser { get; set; } = 2;

    /// <summary>Fichiers envoyés en même temps, tous utilisateurs confondus (0 = illimité).</summary>
    public int MaxConcurrentTotal { get; set; }

    /// <summary>Débit maximum par utilisateur, en Mbit/s, partagé entre ses téléchargements (0 = illimité).</summary>
    public int MaxSpeedPerUserMbps { get; set; }

    /// <summary>Une tâche sans activité depuis ce nombre d'heures est supprimée (ses liens ne marchent plus).</summary>
    public int JobExpirationHours { get; set; } = 72;
}
