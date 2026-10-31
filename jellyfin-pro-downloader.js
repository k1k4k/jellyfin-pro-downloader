/*!
 * Jellyfin Pro Downloader — v0.1.0
 * https://github.com/k1k4k/jellyfin-pro-downloader
 *
 * « Tout télécharger » sur une série, un album, une collection… ne lance plus des centaines de
 * téléchargements d'un coup : une fenêtre permet de choisir quoi télécharger et comment
 * (dans un dossier, en file d'attente ou en ZIP), et un gestionnaire suit la progression.
 *
 * "Download all" on a series, album, collection… no longer fires hundreds of downloads at once:
 * a picker lets you choose what to download and how (into a folder, as a queue or as ZIP files),
 * and a download manager tracks progress.
 *
 * Cible / target : jellyfin-web 10.11+ (développé sur 12.1). Le plugin serveur fournit l'API
 * /ProDownloader. Aucune modification du bundle : les modules internes (actionSheet, shell,
 * playbackManager) sont retrouvés via le runtime webpack, par leur contenu.
 *
 * Licence MIT.
 */
(function () {
    'use strict';

    if (window.__jfProDownloader) return;
    var VERSION = '0.1.0';
    var TAG = '[ProDownloader]';
    window.__jfProDownloader = { version: VERSION };

    /* ------------------------------------------------------------------ */
    /*  0. Textes FR / EN                                                  */
    /* ------------------------------------------------------------------ */

    var STRINGS = {
        fr: {
            download: 'Télécharger', downloadMany: 'Télécharger…', downloads: 'Téléchargements',
            loading: 'Préparation…', nothing: 'Rien à télécharger ici.', error: 'Erreur',
            selected: '{n} fichier(s) · {size}', selectAll: 'Tout cocher', selectNone: 'Tout décocher',
            unavailable: 'indisponible', extras: 'Inclure les fichiers annexes (sous-titres, .nfo, images, paroles)',
            mode: 'Comment télécharger ?', cancel: 'Annuler', start: 'Télécharger',
            modeFolder: 'Dans un dossier', modeFolderDesc: 'Les fichiers sont rangés dans un dossier de votre choix (Saison 01, Album…). Les fichiers déjà présents sont ignorés.',
            modeFolderNo: 'Demande Chrome, Edge ou Opera sur ordinateur, et une adresse en HTTPS.',
            modeQueue: 'File d\'attente', modeQueueDesc: 'Le navigateur télécharge les fichiers un par un, dans votre dossier Téléchargements.',
            modeZip: 'ZIP', modeZipDesc: 'Une archive avec les dossiers déjà rangés. Il faudra la décompresser (prévoir deux fois la place).',
            zipPerGroup: 'Un ZIP par saison / album', zipSingle: 'Un seul ZIP',
            groupFiles: '{n} · {size}',
            resumeTitle: 'Téléchargements non terminés',
            resumeText: 'Reprendre maintenant ?', resumeYes: 'Reprendre', resumeLater: 'Plus tard', resumeDrop: 'Abandonner',
            remaining: '{n} fichier(s) restant(s)',
            pause: 'Pause', resume: 'Reprendre', retry: 'Réessayer', cancelJob: 'Annuler', remove: 'Retirer',
            clearHistory: 'Vider l\'historique', empty: 'Aucun téléchargement.',
            stPending: 'En attente', stRunning: 'En cours', stPaused: 'En pause', stDone: 'Terminé', stFailed: 'Échec', stCanceled: 'Annulé',
            files: '{done}/{total} fichiers', speed: '{speed}/s',
            pausedNative: 'Les fichiers déjà lancés dans le navigateur se terminent.',
            browserBlocked: 'Le navigateur ne démarre pas le téléchargement : autorisez les téléchargements multiples pour ce site (icône dans la barre d\'adresse), puis « Réessayer ».',
            folderPermission: 'Accès au dossier refusé.',
            failedFiles: '{n} fichier(s) en échec',
            close: 'Fermer'
        },
        en: {
            download: 'Download', downloadMany: 'Download…', downloads: 'Downloads',
            loading: 'Preparing…', nothing: 'Nothing to download here.', error: 'Error',
            selected: '{n} file(s) · {size}', selectAll: 'Select all', selectNone: 'Select none',
            unavailable: 'unavailable', extras: 'Include extra files (subtitles, .nfo, images, lyrics)',
            mode: 'How to download?', cancel: 'Cancel', start: 'Download',
            modeFolder: 'Into a folder', modeFolderDesc: 'Files are organized in a folder of your choice (Season 01, Album…). Files already there are skipped.',
            modeFolderNo: 'Needs Chrome, Edge or Opera on a computer, and an HTTPS address.',
            modeQueue: 'Queue', modeQueueDesc: 'The browser downloads the files one by one into your Downloads folder.',
            modeZip: 'ZIP', modeZipDesc: 'An archive with folders already organized. It must be extracted (needs twice the space).',
            zipPerGroup: 'One ZIP per season / album', zipSingle: 'A single ZIP',
            groupFiles: '{n} · {size}',
            resumeTitle: 'Unfinished downloads',
            resumeText: 'Resume now?', resumeYes: 'Resume', resumeLater: 'Later', resumeDrop: 'Discard',
            remaining: '{n} file(s) left',
            pause: 'Pause', resume: 'Resume', retry: 'Retry', cancelJob: 'Cancel', remove: 'Remove',
            clearHistory: 'Clear history', empty: 'No downloads.',
            stPending: 'Waiting', stRunning: 'Downloading', stPaused: 'Paused', stDone: 'Done', stFailed: 'Failed', stCanceled: 'Canceled',
            files: '{done}/{total} files', speed: '{speed}/s',
            pausedNative: 'Files already started in the browser will finish.',
            browserBlocked: 'The browser does not start the download: allow multiple downloads for this site (icon in the address bar), then "Retry".',
            folderPermission: 'Folder access denied.',
            failedFiles: '{n} file(s) failed',
            close: 'Close'
        }
    };

    function getLang() {
        var l = (document.documentElement.lang || navigator.language || 'en').toLowerCase();
        return l.indexOf('fr') === 0 ? 'fr' : 'en';
    }

    function t(key, args) {
        var lang = getLang();
        var s = (STRINGS[lang] && STRINGS[lang][key]) || STRINGS.en[key] || key;
        if (args) {
            Object.keys(args).forEach(function (k) { s = s.split('{' + k + '}').join(args[k]); });
        }
        return s;
    }

    function formatSize(bytes) {
        var fr = getLang() === 'fr';
        var units = fr ? ['o', 'Ko', 'Mo', 'Go', 'To'] : ['B', 'KB', 'MB', 'GB', 'TB'];
        var v = bytes || 0;
        var i = 0;
        while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
        var n = i === 0 ? String(Math.round(v)) : v.toFixed(v < 10 ? 1 : 0);
        return (fr ? n.replace('.', ',') : n) + ' ' + units[i];
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    function sleep(ms) {
        return new Promise(function (r) { setTimeout(r, ms); });
    }

    /* ------------------------------------------------------------------ */
    /*  1. Modules internes via le runtime webpack                         */
    /* ------------------------------------------------------------------ */

    var pm = null;          // playbackManager
    var shellObj = null;    // export par défaut de scripts/shell (objet modifiable)
    var actionSheetObj = null; // export par défaut de components/actionSheet/actionSheet

    function getWebpackRequire() {
        return new Promise(function (resolve) {
            var chunks = (self.webpackChunk = self.webpackChunk || []);
            chunks.push([['jfProDownloader_' + Date.now()], {}, function (req) { resolve(req); }]);
        });
    }

    var scanned = {};

    function scanModules(req) {
        var modules = req.m || {};
        var ids = Object.keys(modules);
        for (var i = 0; i < ids.length; i++) {
            if (pm && shellObj && actionSheetObj) return;
            var id = ids[i];
            if (scanned[id]) continue;
            scanned[id] = true;
            var src;
            try { src = Function.prototype.toString.call(modules[id]); } catch (e) { continue; }
            var maybePm = !pm && /setCurrentPlaylistItem\s*[=(]/.test(src) && /getCurrentPlaylistItemId\s*[=(]/.test(src) && src.indexOf('_currentPlayer') !== -1;
            var maybeShell = !shellObj && src.indexOf('downloadFiles') !== -1 && src.indexOf('openDownloadManager') !== -1;
            var maybeSheet = !actionSheetObj && src.indexOf('actionSheetMenuItem') !== -1 && src.indexOf('actionSheetScroller') !== -1;
            if (!maybePm && !maybeShell && !maybeSheet) continue;
            var exp;
            // Dépendances dans un chunk pas encore arrivé : on réessaiera au prochain chunk.
            try { exp = req(id); } catch (e) { delete scanned[id]; continue; }
            if (!exp) continue;
            var keys = Object.keys(exp);
            for (var k = 0; k < keys.length; k++) {
                var v = exp[keys[k]];
                if (!v) continue;
                if (maybePm && typeof v.canPlay === 'function' && typeof v.getCurrentPlayer === 'function' && typeof v.setCurrentPlaylistItem === 'function') pm = v;
                if (maybeShell && typeof v.downloadFiles === 'function' && typeof v.openDownloadManager === 'function') shellObj = v;
                if (maybeSheet && typeof v.show === 'function' && keys[k] === 'default') actionSheetObj = v;
            }
        }
    }

    /* ------------------------------------------------------------------ */
    /*  2. API du plugin                                                   */
    /* ------------------------------------------------------------------ */

    var info = null; // GET /ProDownloader/Info pour l'utilisateur courant
    var infoUser = null;

    function api() {
        return window.ApiClient || null;
    }

    function currentUserId() {
        var c = api();
        try { return c && c.getCurrentUserId ? c.getCurrentUserId() : null; } catch (e) { return null; }
    }

    function serverId() {
        var c = api();
        try { return (c && c.serverId && c.serverId()) || (c && c.serverInfo && c.serverInfo() && c.serverInfo().Id) || 'local'; } catch (e) { return 'local'; }
    }

    function apiUrl(path) {
        return api().getUrl(path);
    }

    function apiJson(method, path, body) {
        var c = api();
        var opts = { type: method, url: c.getUrl(path), dataType: 'json' };
        if (body !== undefined) {
            opts.data = JSON.stringify(body);
            opts.contentType = 'application/json';
        }
        return c.fetch(opts);
    }

    function apiDelete(path) {
        var c = api();
        return c.fetch({ type: 'DELETE', url: c.getUrl(path) }).catch(function () { /* déjà supprimée */ });
    }

    function loadInfo() {
        var uid = currentUserId();
        if (!uid) return Promise.resolve(null);
        if (info && infoUser === uid) return Promise.resolve(info);
        return apiJson('GET', 'ProDownloader/Info').then(function (i) {
            info = i;
            infoUser = uid;
            return i;
        }, function () { return null; });
    }

    function folderModeSupported() {
        return typeof window.showDirectoryPicker === 'function' && window.isSecureContext;
    }

    /* ------------------------------------------------------------------ */
    /*  3. Interception du menu et des téléchargements natifs              */
    /* ------------------------------------------------------------------ */

    var FOLDER_TYPES = ['Series', 'Season', 'BoxSet', 'MusicAlbum', 'MusicArtist', 'Playlist', 'Folder', 'PhotoAlbum'];
    var ITEM_MENU_IDS = ['download', 'downloadall', 'copy-stream', 'addtoplaylist', 'addtocollection', 'edit', 'editimages',
        'editsubtitles', 'refresh', 'identify', 'delete', 'share', 'moremediainfo', 'instantmix', 'shuffle', 'resume', 'play', 'queue'];

    // Éléments passés à playbackManager.canPlay : la construction du menu d'un élément commence par là.
    var recentItems = [];

    function rememberItem(item) {
        if (!item || !item.Id || !item.Type) return;
        recentItems.push({ item: item, at: Date.now() });
        if (recentItems.length > 30) recentItems.splice(0, recentItems.length - 30);
    }

    function menuItemFor(options) {
        var now = Date.now();
        var candidates = recentItems.filter(function (r) { return now - r.at < 4000; });
        if (!candidates.length) return null;
        // Carte de bibliothèque : l'élément porte son id dans le DOM.
        var anchor = options && options.positionTo;
        var card = anchor && anchor.closest ? anchor.closest('[data-id]') : null;
        var domId = card ? card.getAttribute('data-id') : null;
        for (var i = candidates.length - 1; i >= 0; i--) {
            if (!domId || candidates[i].item.Id === domId) return candidates[i].item;
        }
        return null;
    }

    function canHandle(item) {
        if (!info || !info.CanDownload || !item) return false;
        if (FOLDER_TYPES.indexOf(item.Type) !== -1) return true;
        return !item.IsFolder && item.CanDownload === true;
    }

    function hookPlaybackManager() {
        if (!pm || pm.__jfpdHooked) return;
        pm.__jfpdHooked = true;
        var orig = pm.canPlay;
        pm.canPlay = function (item) {
            try { rememberItem(item); } catch (e) { /* ignore */ }
            return orig.apply(this, arguments);
        };
    }

    function hookActionSheet() {
        if (!actionSheetObj || actionSheetObj.__jfpdHooked) return;
        actionSheetObj.__jfpdHooked = true;
        var orig = actionSheetObj.show;
        actionSheetObj.show = function (options) {
            var items = options && options.items;
            if (window.NativeShell || !Array.isArray(items) || !info || !info.CanDownload) return orig.apply(this, arguments);
            var ids = items.map(function (i) { return i && i.id; });
            var isItemMenu = ids.some(function (id) { return ITEM_MENU_IDS.indexOf(id) !== -1; });
            var item = isItemMenu ? menuItemFor(options) : null;
            if (!item || !canHandle(item)) return orig.apply(this, arguments);
            // Vérification croisée avec ce que jellyfin-web propose pour cet élément.
            if (ids.indexOf('download') !== -1 && !item.CanDownload) return orig.apply(this, arguments);

            var isFolder = FOLDER_TYPES.indexOf(item.Type) !== -1;
            var insertAt = -1;
            var newItems = [];
            items.forEach(function (i) {
                if (i && (i.id === 'download' || i.id === 'downloadall')) {
                    if (insertAt === -1) insertAt = newItems.length;
                    return;
                }
                newItems.push(i);
            });
            if (insertAt === -1) {
                var copyAt = ids.indexOf('copy-stream');
                insertAt = copyAt !== -1 ? copyAt : newItems.length;
                // Avant « Supprimer » plutôt qu'en dernier.
                var del = newItems.map(function (i) { return i && i.id; }).indexOf('delete');
                if (copyAt === -1 && del !== -1) insertAt = del;
            }
            newItems.splice(insertAt, 0, { name: isFolder ? t('downloadMany') : t('download'), id: 'jfpd-download', icon: 'file_download' });

            var opts = Object.assign({}, options, { items: newItems });
            return orig.call(this, opts).then(function (id) {
                if (id === 'jfpd-download') {
                    startFor(item);
                    // Pour itemContextMenu, c'est comme une fermeture sans choix.
                    throw new Error('ActionSheet closed without resolving');
                }
                return id;
            });
        };
    }

    function hookShell() {
        if (!shellObj || shellObj.__jfpdHooked) return;
        shellObj.__jfpdHooked = true;
        var orig = shellObj.downloadFiles;
        shellObj.downloadFiles = function (items) {
            // Applis natives : leur propre gestionnaire. Sans droits ou plugin désactivé : comportement d'origine.
            if (window.NativeShell || !info || !info.CanDownload || !Array.isArray(items) || !items.length) {
                return orig.apply(this, arguments);
            }
            var ids = items.map(function (i) { return i.itemId || (i.item && i.item.Id); }).filter(Boolean);
            if (!ids.length) return orig.apply(this, arguments);
            if (ids.length === 1 && items[0].item && !items[0].item.IsFolder) {
                downloadDirect(ids[0]);
            } else {
                openPicker(ids);
            }
            return true;
        };
    }

    function startFor(item) {
        if (FOLDER_TYPES.indexOf(item.Type) !== -1) {
            openPicker([item.Id]);
        } else {
            downloadDirect(item.Id);
        }
    }

    /* ------------------------------------------------------------------ */
    /*  4. Styles                                                          */
    /* ------------------------------------------------------------------ */

    function injectStyles() {
        if (document.getElementById('jfpdStyles')) return;
        var css = [
            '.jfpd-overlay{position:fixed;inset:0;z-index:2000;background:rgba(0,0,0,.6);display:flex;align-items:center;justify-content:center;padding:16px;box-sizing:border-box}',
            '.jfpd-dialog{background:#202020;color:#eee;border-radius:10px;box-shadow:0 10px 40px rgba(0,0,0,.6);width:min(720px,100%);max-height:calc(100vh - 32px);display:flex;flex-direction:column;overflow:hidden;font-size:15px}',
            '.jfpd-dialog.jfpd-small{width:min(460px,100%)}',
            '.jfpd-head{display:flex;align-items:center;gap:8px;padding:14px 16px;border-bottom:1px solid rgba(255,255,255,.08)}',
            '.jfpd-head h2{margin:0;font-size:1.15em;font-weight:600;flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
            '.jfpd-x{background:none;border:0;color:inherit;cursor:pointer;padding:4px;border-radius:50%;line-height:0}',
            '.jfpd-x:hover{background:rgba(255,255,255,.1)}',
            '.jfpd-body{overflow-x:hidden;overflow-y:auto;padding:8px 16px;flex:1 1 auto;min-height:0}',
            '.jfpd-foot{display:flex;flex-wrap:wrap;align-items:center;gap:8px;padding:12px 16px;border-top:1px solid rgba(255,255,255,.08)}',
            '.jfpd-foot .jfpd-sum{flex:1;min-width:0;opacity:.85}',
            '.jfpd-btn{background:rgba(255,255,255,.1);color:inherit;border:0;border-radius:6px;padding:8px 16px;font:inherit;cursor:pointer}',
            '.jfpd-btn:hover{background:rgba(255,255,255,.18)}',
            '.jfpd-btn:disabled{opacity:.4;cursor:default}',
            '.jfpd-btn.jfpd-primary{background:#00a4dc;color:#fff}',
            '.jfpd-btn.jfpd-primary:hover{background:#0cb0e8}',
            '.jfpd-link{background:none;border:0;color:#00a4dc;cursor:pointer;font:inherit;padding:2px 4px}',
            '.jfpd-tools{display:flex;gap:8px;margin:4px 0 8px}',
            '.jfpd-group{border-radius:6px;margin-bottom:2px}',
            '.jfpd-row{display:flex;align-items:center;gap:8px;padding:6px 4px;border-radius:6px;cursor:pointer;user-select:none}',
            '.jfpd-row:hover{background:rgba(255,255,255,.06)}',
            '.jfpd-row input{width:18px;height:18px;margin:0;accent-color:#00a4dc;flex:none;cursor:pointer}',
            '.jfpd-row .jfpd-name{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
            '.jfpd-row .jfpd-meta{opacity:.6;font-size:.9em;white-space:nowrap}',
            '.jfpd-row .jfpd-label{opacity:.6;font-variant-numeric:tabular-nums;margin-right:2px}',
            '.jfpd-row.jfpd-off{opacity:.45;cursor:default}',
            '.jfpd-grow{flex:none;width:24px;height:24px;line-height:24px;text-align:center;transition:transform .15s;opacity:.7}',
            '.jfpd-group.jfpd-open>.jfpd-row .jfpd-grow{transform:rotate(90deg)}',
            '.jfpd-entries{padding-left:28px}',
            '.jfpd-section{margin:14px 0 6px;font-weight:600}',
            '.jfpd-check{display:flex;align-items:center;gap:8px;margin:10px 0;cursor:pointer}',
            '.jfpd-check input{width:18px;height:18px;accent-color:#00a4dc}',
            '.jfpd-modes{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:8px}',
            '.jfpd-mode{border:1px solid rgba(255,255,255,.15);border-radius:8px;padding:10px 12px;cursor:pointer;display:block}',
            '.jfpd-mode input{position:absolute;opacity:0;pointer-events:none}',
            '.jfpd-mode b{display:block;margin-bottom:4px}',
            '.jfpd-mode small{display:block;opacity:.7;line-height:1.35}',
            '.jfpd-mode.jfpd-sel{border-color:#00a4dc;background:rgba(0,164,220,.12)}',
            '.jfpd-mode.jfpd-dis{opacity:.45;cursor:default}',
            '.jfpd-zipopt{margin-top:8px;display:flex;gap:16px;flex-wrap:wrap}',
            '.jfpd-zipopt label{display:flex;align-items:center;gap:6px;cursor:pointer}',
            '.jfpd-msg{padding:24px 4px;opacity:.8}',
            '.jfpd-spin{display:inline-block;width:18px;height:18px;border:2px solid rgba(255,255,255,.25);border-top-color:#00a4dc;border-radius:50%;animation:jfpdspin 1s linear infinite;vertical-align:middle;margin-right:8px}',
            '@keyframes jfpdspin{to{transform:rotate(360deg)}}',
            /* gestionnaire */
            '.jfpdHeaderButton{position:relative}',
            '.jfpdHeaderButton .material-icons{font-size:24px}',
            '.jfpdHeaderButton .jfpd-badge{position:absolute;top:4px;right:2px;min-width:16px;height:16px;padding:0 4px;box-sizing:border-box;border-radius:8px;background:#00a4dc;color:#fff;font-size:11px;line-height:16px;text-align:center;pointer-events:none}',
            '.jfpdHeaderButton .jfpd-badge:empty{display:none}',
            '.jfpd-float{position:fixed;right:16px;bottom:16px;z-index:1500;width:48px;height:48px;border-radius:50%;border:0;background:#00a4dc;color:#fff;box-shadow:0 4px 14px rgba(0,0,0,.5);cursor:pointer}',
            'html.jfBgBarShown .jfpd-float,.jfpd-float.jfpd-raised{bottom:96px}',
            '.jfpd-panel{position:fixed;top:64px;right:12px;z-index:1600;width:min(440px,calc(100vw - 24px));max-height:calc(100vh - 80px);display:flex;flex-direction:column;background:#202020;color:#eee;border-radius:10px;box-shadow:0 10px 40px rgba(0,0,0,.6);font-size:14px}',
            '.jfpd-panel .jfpd-body{padding:4px 12px}',
            '.jfpd-job{padding:10px 2px;border-bottom:1px solid rgba(255,255,255,.07)}',
            '.jfpd-job:last-child{border-bottom:0}',
            '.jfpd-job-top{display:flex;align-items:center;gap:6px}',
            '.jfpd-job-name{flex:1;min-width:0;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}',
            '.jfpd-job-line{opacity:.7;font-size:.92em;margin-top:3px}',
            '.jfpd-job-hint{color:#f0b429;font-size:.9em;margin-top:4px}',
            '.jfpd-bar{height:4px;background:rgba(255,255,255,.12);border-radius:2px;margin-top:6px;overflow:hidden}',
            '.jfpd-bar>div{height:100%;background:#00a4dc;width:0;transition:width .4s}',
            '.jfpd-job.jfpd-st-done .jfpd-bar>div{background:#3fb950}',
            '.jfpd-job.jfpd-st-failed .jfpd-bar>div{background:#e5534b}',
            '.jfpd-icon{background:none;border:0;color:inherit;cursor:pointer;padding:3px;border-radius:50%;line-height:0;opacity:.8}',
            '.jfpd-icon:hover{background:rgba(255,255,255,.1);opacity:1}',
            '.jfpd-icon .material-icons{font-size:20px}',
            '@media (max-width:600px){.jfpd-panel{top:auto;bottom:0;right:0;width:100vw;border-radius:10px 10px 0 0;max-height:70vh}.jfpd-entries{padding-left:12px}}'
        ].join('\n');
        var style = document.createElement('style');
        style.id = 'jfpdStyles';
        style.textContent = css;
        document.head.appendChild(style);
    }

    function icon(name) {
        return '<span class="material-icons ' + name + '" aria-hidden="true"></span>';
    }

    function overlay(dialogClass) {
        injectStyles();
        var ov = document.createElement('div');
        ov.className = 'jfpd-overlay';
        ov.innerHTML = '<div class="jfpd-dialog ' + (dialogClass || '') + '" role="dialog" aria-modal="true"></div>';
        document.body.appendChild(ov);
        var dlg = ov.firstChild;
        var closed = false;
        var onKey = function (e) { if (e.key === 'Escape') close(); };
        function close() {
            if (closed) return;
            closed = true;
            document.removeEventListener('keydown', onKey, true);
            ov.remove();
        }
        document.addEventListener('keydown', onKey, true);
        ov.addEventListener('mousedown', function (e) { if (e.target === ov) close(); });
        return { overlay: ov, dialog: dlg, close: close, isClosed: function () { return closed; } };
    }

    /* ------------------------------------------------------------------ */
    /*  5. Fenêtre de sélection                                            */
    /* ------------------------------------------------------------------ */

    var PREFS_KEY = 'jfpd:prefs';

    function loadPrefs() {
        try { return JSON.parse(localStorage.getItem(PREFS_KEY)) || {}; } catch (e) { return {}; }
    }

    function savePrefs(p) {
        try { localStorage.setItem(PREFS_KEY, JSON.stringify(p)); } catch (e) { /* ignore */ }
    }

    function availableModes() {
        var modes = [];
        if (info.AllowFolderMode) modes.push({ id: 'folder', ok: folderModeSupported() });
        if (info.AllowQueueMode) modes.push({ id: 'queue', ok: true });
        if (info.AllowZipMode) modes.push({ id: 'zip', ok: true });
        if (!modes.length) modes.push({ id: 'queue', ok: true });
        return modes;
    }

    function openPicker(itemIds) {
        loadInfo().then(function () {
            if (!info || !info.CanDownload) return;
            var ui = overlay();
            ui.dialog.innerHTML = '<div class="jfpd-head"><h2>' + escapeHtml(t('downloadMany').replace('…', '')) + '</h2>'
                + '<button class="jfpd-x" title="' + escapeHtml(t('close')) + '">' + icon('close') + '</button></div>'
                + '<div class="jfpd-body"><div class="jfpd-msg"><span class="jfpd-spin"></span>' + escapeHtml(t('loading')) + '</div></div>';
            ui.dialog.querySelector('.jfpd-x').addEventListener('click', ui.close);
            apiJson('POST', 'ProDownloader/Resolve', { ItemIds: itemIds }).then(function (tree) {
                if (!ui.isClosed()) renderPicker(ui, tree);
            }, function (err) {
                console.error(TAG, 'resolve failed', err);
                if (!ui.isClosed()) ui.dialog.querySelector('.jfpd-body').innerHTML = '<div class="jfpd-msg">' + escapeHtml(t('error')) + '</div>';
            });
        });
    }

    function renderPicker(ui, tree) {
        var groups = tree.Groups || [];
        var prefs = loadPrefs();
        var selected = {}; // ItemId -> true
        var openGroups = {};
        var any = false;
        groups.forEach(function (g) {
            g.Entries.forEach(function (e) {
                if (!e.Unavailable) { selected[e.ItemId] = true; any = true; }
            });
        });
        if (groups.length === 1) openGroups[groups[0].Key] = true;

        ui.dialog.querySelector('h2').textContent = tree.Title || t('downloadMany');
        var body = ui.dialog.querySelector('.jfpd-body');
        if (!any) {
            body.innerHTML = '<div class="jfpd-msg">' + escapeHtml(t('nothing')) + '</div>';
            return;
        }

        var modes = availableModes();
        var mode = prefs.mode;
        if (!modes.some(function (m) { return m.id === mode && m.ok; })) {
            mode = (modes.filter(function (m) { return m.ok; })[0] || modes[0]).id;
        }
        var zipSingle = !!prefs.zipSingle;
        var withExtras = prefs.extras !== false;

        function entrySize(e) {
            var s = 0;
            e.Files.forEach(function (f) { if (withExtras || f.Kind === 'media') s += f.Size; });
            return s;
        }

        function groupStats(g) {
            var n = 0, size = 0, sel = 0, avail = 0;
            g.Entries.forEach(function (e) {
                if (e.Unavailable) return;
                avail++;
                if (selected[e.ItemId]) { sel++; n++; size += entrySize(e); }
            });
            if (withExtras && sel) g.Extras.forEach(function (f) { size += f.Size; });
            return { n: n, size: size, sel: sel, avail: avail };
        }

        function totals() {
            var files = 0, size = 0, anySel = false;
            groups.forEach(function (g) {
                var st = groupStats(g);
                size += st.size;
                if (st.sel) anySel = true;
                g.Entries.forEach(function (e) {
                    if (!e.Unavailable && selected[e.ItemId]) files += withExtras ? e.Files.length : 1;
                });
                if (withExtras && st.sel) files += g.Extras.length;
            });
            if (withExtras && anySel) {
                (tree.Extras || []).forEach(function (f) { size += f.Size; files++; });
            }
            return { files: files, size: size };
        }

        function entryHtml(e) {
            return '<label class="jfpd-row' + (e.Unavailable ? ' jfpd-off' : '') + '">'
                + '<input type="checkbox" data-entry="' + e.ItemId + '"' + (selected[e.ItemId] ? ' checked' : '') + (e.Unavailable ? ' disabled' : '') + '>'
                + '<span class="jfpd-name">' + (e.Label ? '<span class="jfpd-label">' + escapeHtml(e.Label) + '</span> ' : '') + escapeHtml(e.Name) + '</span>'
                + '<span class="jfpd-meta">' + (e.Unavailable ? escapeHtml(t('unavailable')) : formatSize(entrySize(e))) + '</span></label>';
        }

        function groupHtml(g, gi) {
            var st = groupStats(g);
            var open = !!openGroups[g.Key];
            var single = groups.length === 1;
            return '<div class="jfpd-group' + (open ? ' jfpd-open' : '') + '" data-group="' + gi + '">'
                + (single ? '' : '<div class="jfpd-row" data-toggle="' + gi + '"><input type="checkbox" data-gcheck="' + gi + '"' + (st.sel ? ' checked' : '') + (st.avail ? '' : ' disabled') + '>'
                    + '<span class="jfpd-name">' + escapeHtml(g.Name) + '</span>'
                    + '<span class="jfpd-meta">' + escapeHtml(t('groupFiles', { n: st.sel + '/' + st.avail, size: formatSize(st.size) })) + '</span>'
                    + '<span class="jfpd-grow">' + icon('chevron_right') + '</span></div>')
                + (open || single ? '<div class="jfpd-entries"' + (single ? ' style="padding-left:0"' : '') + '>' + g.Entries.map(entryHtml).join('') + '</div>' : '')
                + '</div>';
        }

        function modeHtml(m) {
            var name = { folder: t('modeFolder'), queue: t('modeQueue'), zip: t('modeZip') }[m.id];
            var desc = { folder: m.ok ? t('modeFolderDesc') : t('modeFolderNo'), queue: t('modeQueueDesc'), zip: t('modeZipDesc') }[m.id];
            return '<label class="jfpd-mode' + (m.id === mode ? ' jfpd-sel' : '') + (m.ok ? '' : ' jfpd-dis') + '">'
                + '<input type="radio" name="jfpdMode" value="' + m.id + '"' + (m.id === mode ? ' checked' : '') + (m.ok ? '' : ' disabled') + '>'
                + '<b>' + escapeHtml(name) + '</b><small>' + escapeHtml(desc) + '</small></label>';
        }

        function render() {
            var scroll = body.scrollTop;
            body.innerHTML = '<div class="jfpd-tools"><button class="jfpd-link" data-all="1">' + escapeHtml(t('selectAll')) + '</button>'
                + '<button class="jfpd-link" data-all="0">' + escapeHtml(t('selectNone')) + '</button></div>'
                + groups.map(groupHtml).join('')
                + '<label class="jfpd-check"><input type="checkbox" data-extras' + (withExtras ? ' checked' : '') + '>' + escapeHtml(t('extras')) + '</label>'
                + '<div class="jfpd-section">' + escapeHtml(t('mode')) + '</div>'
                + '<div class="jfpd-modes">' + modes.map(modeHtml).join('') + '</div>'
                + (mode === 'zip' && groups.length > 1 ? '<div class="jfpd-zipopt">'
                    + '<label><input type="radio" name="jfpdZip" value="group"' + (zipSingle ? '' : ' checked') + '>' + escapeHtml(t('zipPerGroup')) + '</label>'
                    + '<label><input type="radio" name="jfpdZip" value="single"' + (zipSingle ? ' checked' : '') + '>' + escapeHtml(t('zipSingle')) + '</label></div>' : '');
            body.scrollTop = scroll;
            groups.forEach(function (g, gi) {
                var cb = body.querySelector('[data-gcheck="' + gi + '"]');
                if (!cb) return;
                var st = groupStats(g);
                cb.indeterminate = st.sel > 0 && st.sel < st.avail;
            });
            updateFoot();
        }

        var foot = document.createElement('div');
        foot.className = 'jfpd-foot';
        foot.innerHTML = '<span class="jfpd-sum"></span><button class="jfpd-btn" data-cancel>' + escapeHtml(t('cancel')) + '</button>'
            + '<button class="jfpd-btn jfpd-primary" data-go>' + escapeHtml(t('start')) + '</button>';
        ui.dialog.appendChild(foot);

        function updateFoot() {
            var tot = totals();
            foot.querySelector('.jfpd-sum').textContent = t('selected', { n: tot.files, size: formatSize(tot.size) });
            foot.querySelector('[data-go]').disabled = tot.files === 0;
        }

        body.addEventListener('click', function (e) {
            var all = e.target.closest('[data-all]');
            if (all) {
                var on = all.getAttribute('data-all') === '1';
                groups.forEach(function (g) { g.Entries.forEach(function (en) { if (!en.Unavailable) selected[en.ItemId] = on; }); });
                render();
                return;
            }
            var toggle = e.target.closest('[data-toggle]');
            if (toggle && !e.target.matches('input')) {
                e.preventDefault();
                var g = groups[+toggle.getAttribute('data-toggle')];
                openGroups[g.Key] = !openGroups[g.Key];
                render();
            }
        });

        body.addEventListener('change', function (e) {
            var el = e.target;
            if (el.hasAttribute('data-entry')) {
                selected[el.getAttribute('data-entry')] = el.checked;
                render();
            } else if (el.hasAttribute('data-gcheck')) {
                var g = groups[+el.getAttribute('data-gcheck')];
                g.Entries.forEach(function (en) { if (!en.Unavailable) selected[en.ItemId] = el.checked; });
                render();
            } else if (el.hasAttribute('data-extras')) {
                withExtras = el.checked;
                render();
            } else if (el.name === 'jfpdMode') {
                mode = el.value;
                render();
            } else if (el.name === 'jfpdZip') {
                zipSingle = el.value === 'single';
            }
        });

        foot.querySelector('[data-cancel]').addEventListener('click', ui.close);
        foot.querySelector('[data-go]').addEventListener('click', function () {
            var go = foot.querySelector('[data-go]');
            go.disabled = true;
            savePrefs({ mode: mode, zipSingle: zipSingle, extras: withExtras });
            var selection = buildSelection(tree, selected, withExtras);
            // Mode Dossier : le sélecteur de dossier doit être ouvert pendant le clic (geste utilisateur).
            var dirPromise = mode === 'folder' ? window.showDirectoryPicker({ id: 'jfprodownloader', mode: 'readwrite' }) : Promise.resolve(null);
            dirPromise.then(function (dir) {
                ui.close();
                return manager.add({
                    name: tree.Title || 'Jellyfin',
                    mode: mode,
                    files: selection.files,
                    zips: mode === 'zip' ? buildZips(selection, zipSingle, tree.Title) : []
                }, dir);
            }).catch(function (err) {
                go.disabled = false;
                if (err && err.name === 'AbortError') return; // sélecteur de dossier fermé
                console.error(TAG, err);
            });
        });

        render();
    }

    // Fichiers choisis, dans l'ordre d'affichage. group = indice du groupe (pour les ZIP par groupe).
    function buildSelection(tree, selected, withExtras) {
        var files = [];
        var anySel = false;
        var byGroup = [];
        (tree.Groups || []).forEach(function (g, gi) {
            var list = [];
            var gSel = g.Entries.some(function (e) { return !e.Unavailable && selected[e.ItemId]; });
            if (gSel) anySel = true;
            if (gSel && withExtras) g.Extras.forEach(function (f) { list.push(f); });
            g.Entries.forEach(function (e) {
                if (e.Unavailable || !selected[e.ItemId]) return;
                e.Files.forEach(function (f) { if (withExtras || f.Kind === 'media') list.push(f); });
            });
            byGroup.push({ name: g.ZipName || g.Name, start: files.length, count: list.length });
            list.forEach(function (f) { files.push({ itemId: f.ItemId, part: f.Part, path: f.Path, size: f.Size, kind: f.Kind }); });
        });
        var extras = [];
        if (anySel && withExtras) {
            (tree.Extras || []).forEach(function (f) { extras.push({ itemId: f.ItemId, part: f.Part, path: f.Path, size: f.Size, kind: f.Kind }); });
        }
        // Annexes communes en tête (affiche de la série…).
        byGroup.forEach(function (g) { g.start += extras.length; });
        return { files: extras.concat(files), groups: byGroup.filter(function (g) { return g.count > 0; }), extrasCount: extras.length };
    }

    function buildZips(selection, single, title) {
        var all = selection.files.map(function (f, i) { return i; });
        if (single || selection.groups.length <= 1) {
            return [{ name: (selection.groups.length === 1 && !single ? selection.groups[0].name : title) || 'Jellyfin', files: all }];
        }
        // Annexes communes copiées dans chaque ZIP : chacun reste complet une fois décompressé.
        var common = all.slice(0, selection.extrasCount);
        return selection.groups.map(function (g) {
            var idx = common.slice();
            for (var i = 0; i < g.count; i++) idx.push(g.start + i);
            return { name: g.name, files: idx };
        });
    }

    function downloadDirect(itemId) {
        loadInfo().then(function () {
            if (!info || !info.CanDownload) return null;
            return apiJson('POST', 'ProDownloader/Resolve', { ItemIds: [itemId] });
        }).then(function (tree) {
            if (!tree) return;
            var media = null;
            (tree.Groups || []).forEach(function (g) {
                g.Entries.forEach(function (e) {
                    if (!media && !e.Unavailable && e.Files.length && e.Files[0].Kind === 'media') media = e.Files[0];
                });
            });
            if (!media) {
                toast(t('nothing'));
                return;
            }
            var name = media.Path.split('/').pop();
            return manager.add({
                name: name,
                mode: 'queue',
                direct: true,
                files: [{ itemId: media.ItemId, part: media.Part, path: name, size: media.Size, kind: 'media' }],
                zips: []
            }, null);
        }).catch(function (err) { console.error(TAG, err); });
    }

    function toast(text) {
        injectStyles();
        var el = document.createElement('div');
        el.style.cssText = 'position:fixed;left:50%;bottom:24px;transform:translateX(-50%);background:#323232;color:#fff;padding:10px 18px;border-radius:6px;z-index:2100;box-shadow:0 4px 14px rgba(0,0,0,.5)';
        el.textContent = text;
        document.body.appendChild(el);
        setTimeout(function () { el.remove(); }, 3500);
    }

    /* ------------------------------------------------------------------ */
    /*  6. Dossiers choisis (mode Dossier) : IndexedDB                     */
    /* ------------------------------------------------------------------ */

    var dirs = {
        db: null,
        open: function () {
            if (this.db) return this.db;
            this.db = new Promise(function (resolve, reject) {
                var req = indexedDB.open('jfProDownloader', 1);
                req.onupgradeneeded = function () { req.result.createObjectStore('dirs'); };
                req.onsuccess = function () { resolve(req.result); };
                req.onerror = function () { reject(req.error); };
            });
            return this.db;
        },
        op: function (mode, fn) {
            return this.open().then(function (db) {
                return new Promise(function (resolve, reject) {
                    var tx = db.transaction('dirs', mode);
                    var req = fn(tx.objectStore('dirs'));
                    tx.oncomplete = function () { resolve(req && req.result); };
                    tx.onerror = function () { reject(tx.error); };
                });
            });
        },
        set: function (key, handle) { return this.op('readwrite', function (s) { return s.put(handle, key); }); },
        get: function (key) { return this.op('readonly', function (s) { return s.get(key); }).catch(function () { return null; }); },
        del: function (key) { return this.op('readwrite', function (s) { return s.delete(key); }).catch(function () { /* ignore */ }); }
    };

    /* ------------------------------------------------------------------ */
    /*  7. Gestionnaire de téléchargements                                 */
    /* ------------------------------------------------------------------ */

    var MAX_HISTORY = 50;
    var BROWSER_START_TIMEOUT = 20000;

    var manager = {
        jobs: [],
        key: null,
        running: {},   // clé d'unité -> { abort, ... }
        loopTimer: null,
        pollTimer: null,
        panel: null,

        storageKey: function () {
            return 'jfpd:v1:' + serverId() + ':' + currentUserId();
        },

        load: function () {
            this.key = this.storageKey();
            try { this.jobs = JSON.parse(localStorage.getItem(this.key)) || []; } catch (e) { this.jobs = []; }
            // Ce qui tournait lors de la fermeture de la page est remis en attente (vérifié auprès du serveur au besoin).
            this.jobs.forEach(function (job) {
                units(job).forEach(function (u) { if (u.status === 'active') u.status = 'pending'; });
                if (job.status === 'running') job.status = 'paused';
            });
        },

        save: function () {
            if (!this.key) return;
            try {
                localStorage.setItem(this.key, JSON.stringify(this.jobs));
            } catch (e) {
                console.warn(TAG, 'could not save the download list', e);
            }
        },

        add: function (spec, dirHandle) {
            var job = {
                id: 'j' + Date.now().toString(36) + Math.random().toString(36).slice(2, 7),
                name: spec.name,
                mode: spec.mode,
                direct: !!spec.direct,
                created: Date.now(),
                status: 'running',
                serverJob: null,
                files: spec.files.map(function (f) { return { itemId: f.itemId, part: f.part, path: f.path, size: f.size, status: 'pending', loaded: 0 }; }),
                zips: (spec.zips || []).map(function (z) {
                    return { name: z.name, files: z.files, size: 0, status: 'pending', loaded: 0 };
                })
            };
            var self = this;
            var ready = dirHandle ? dirs.set(job.id, dirHandle) : Promise.resolve();
            return ready.then(function () {
                self.jobs.unshift(job);
                self.trimHistory();
                self.save();
                self.ensureButton();
                if (!job.direct) self.showPanel();
                self.kick();
                return job;
            });
        },

        trimHistory: function () {
            var finished = this.jobs.filter(function (j) { return isFinished(j); });
            if (finished.length > MAX_HISTORY) {
                var drop = finished.slice(MAX_HISTORY);
                this.jobs = this.jobs.filter(function (j) { return drop.indexOf(j) === -1; });
            }
        },

        // Crée (ou recrée, après expiration ou redémarrage) la tâche côté serveur.
        ensureServerJob: function (job) {
            if (job.serverJob) return Promise.resolve(job.serverJob);
            if (job.creating) return job.creating;
            var self = this;
            job.creating = apiJson('POST', 'ProDownloader/Jobs', {
                Name: job.name,
                Files: job.files.map(function (f) { return { ItemId: f.itemId, Part: f.part, Path: f.path }; }),
                Zips: job.zips.map(function (z) { return { Name: z.name, Files: z.files }; })
            }).then(function (res) {
                job.serverJob = res.Id;
                job.urls = res.Files.map(function (f) { return f.Url; });
                job.zipUrls = res.Zips.map(function (z) { return z.Url; });
                res.Zips.forEach(function (z, i) { if (job.zips[i]) job.zips[i].size = z.Size; });
                job.creating = null;
                self.save();
                return res.Id;
            }, function (err) {
                job.creating = null;
                throw err;
            });
            return job.creating;
        },

        forgetServerJob: function (job) {
            job.serverJob = null;
            job.urls = null;
            job.zipUrls = null;
        },

        concurrency: function () {
            return Math.max(1, (info && info.MaxConcurrent) || 2);
        },

        activeCount: function () {
            return Object.keys(this.running).length;
        },

        kick: function () {
            var self = this;
            if (this.loopTimer) return;
            this.loopTimer = setTimeout(function () {
                self.loopTimer = null;
                self.schedule();
            }, 0);
        },

        schedule: function () {
            var self = this;
            var slots = this.concurrency() - this.activeCount();
            for (var j = 0; j < this.jobs.length && slots > 0; j++) {
                var job = this.jobs[j];
                if (job.status !== 'running') continue;
                var list = units(job);
                for (var i = 0; i < list.length && slots > 0; i++) {
                    var u = list[i];
                    if (u.status !== 'pending' || (u.retryAt && u.retryAt > Date.now())) continue;
                    slots--;
                    this.startUnit(job, u, i);
                }
                this.refreshJobStatus(job);
            }
            // Réessais différés.
            var waiting = this.jobs.some(function (jb) {
                return jb.status === 'running' && units(jb).some(function (u) { return u.status === 'pending' && u.retryAt; });
            });
            if (waiting) setTimeout(function () { self.kick(); }, 2000);
            this.ensurePolling();
            this.save();
            this.render();
        },

        startUnit: function (job, u, index) {
            var self = this;
            var key = job.id + ':' + index;
            var slot = { job: job, unit: u, index: index };
            this.running[key] = slot;
            u.status = 'active';
            u.error = null;
            var run = job.mode === 'folder' ? this.runFolderFile(job, u, index, slot) : this.runNative(job, u, index, slot);
            run.then(function () {
                delete self.running[key];
                self.refreshJobStatus(job);
                self.save();
                self.render();
                self.kick();
            });
        },

        // Mode Dossier : fetch en flux vers le fichier, dans l'arborescence.
        runFolderFile: function (job, u, index, slot) {
            var self = this;
            var controller = new AbortController();
            slot.abort = function () { controller.abort(); };
            u.loaded = 0;
            u.startedAt = Date.now();
            var writable = null;
            return dirs.get(job.id).then(function (root) {
                if (!root) throw new Error(t('folderPermission'));
                return ensurePermission(root).then(function (ok) {
                    if (!ok) throw new Error(t('folderPermission'));
                    return getFileHandle(root, u.path);
                });
            }).then(function (target) {
                return target.handle.getFile().then(function (existing) { return existing.size === u.size ? null : target.handle; }, function () { return target.handle; });
            }).then(function (handle) {
                if (!handle) {
                    u.status = 'done';
                    u.loaded = u.size;
                    u.skipped = true;
                    return null;
                }
                return self.ensureServerJob(job).then(function () {
                    return fetch(apiUrl(job.urls[index]), { signal: controller.signal, cache: 'no-store' });
                }).then(function (res) {
                    if (res.status === 429) {
                        var e429 = new Error('busy');
                        e429.busy = true;
                        throw e429;
                    }
                    if (res.status === 404) {
                        self.forgetServerJob(job);
                        var e404 = new Error('job expired');
                        e404.retryNow = true;
                        throw e404;
                    }
                    if (!res.ok || !res.body) throw new Error('HTTP ' + res.status);
                    return handle.createWritable().then(function (w) {
                        writable = w;
                        var counter = new TransformStream({
                            transform: function (chunk, ctl) {
                                u.loaded += chunk.byteLength;
                                ctl.enqueue(chunk);
                            }
                        });
                        return res.body.pipeThrough(counter).pipeTo(w, { signal: controller.signal });
                    });
                }).then(function () {
                    writable = null;
                    u.status = 'done';
                    u.loaded = u.size;
                });
            }).catch(function (err) {
                if (writable) {
                    try { writable.abort(); } catch (e) { /* ignore */ }
                }
                self.handleUnitError(job, u, err, controller.signal.aborted);
            });
        },

        // Modes File d'attente et ZIP : le navigateur télécharge, le serveur dit où il en est.
        runNative: function (job, u, index, slot) {
            var self = this;
            var isZip = job.mode === 'zip';
            return this.ensureServerJob(job).then(function () {
                return apiJson('GET', 'ProDownloader/Jobs/' + job.serverJob);
            }).then(function (state) {
                var s = (isZip ? state.Zips : state.Files)[index].State;
                if (s.Status === 'done') {
                    u.status = 'done';
                    u.loaded = u.size || (isZip ? job.zips[index].size : u.size);
                    return null;
                }
                slot.triggeredAt = Date.now();
                u.hint = null;
                if (s.Status === 'active') {
                    // Déjà en cours dans le navigateur (page rechargée pendant le téléchargement) : on suit.
                    slot.baseRequests = s.Requests - 1;
                } else {
                    slot.baseRequests = s.Requests;
                    triggerDownload(apiUrl(isZip ? job.zipUrls[index] : job.urls[index]));
                }
                return new Promise(function (resolve) {
                    slot.finish = resolve;
                    slot.abort = function () { resolve(); };
                });
            }).catch(function (err) {
                if (err && err.status === 404) self.forgetServerJob(job);
                self.handleUnitError(job, u, err, false);
            });
        },

        handleUnitError: function (job, u, err, aborted) {
            if (u.status === 'done') return;
            if (aborted || job.status !== 'running') {
                // Pause ou annulation : le fichier repart de zéro à la reprise.
                u.status = job.status === 'canceled' ? 'canceled' : 'pending';
                u.loaded = 0;
                return;
            }
            if (err && (err.busy || err.retryNow)) {
                u.status = 'pending';
                u.retryAt = err.retryNow ? 0 : Date.now() + 3000;
                return;
            }
            u.tries = (u.tries || 0) + 1;
            console.warn(TAG, 'download failed', u.path, err);
            if (u.tries < 3) {
                u.status = 'pending';
                u.retryAt = Date.now() + 2000 * u.tries;
            } else {
                u.status = 'failed';
                u.error = String((err && err.message) || err);
            }
        },

        ensurePolling: function () {
            var self = this;
            var native = Object.keys(this.running).some(function (k) { return self.running[k].job.mode !== 'folder'; });
            var folder = Object.keys(this.running).length > 0;
            if ((native || folder) && !this.pollTimer) {
                this.pollTimer = setInterval(function () { self.poll(); }, 1500);
            } else if (!native && !folder && this.pollTimer) {
                clearInterval(this.pollTimer);
                this.pollTimer = null;
                this.render();
            }
        },

        // Vitesse lissée, mesurée quand la progression est mise à jour (pas à chaque affichage).
        sampleSpeed: function (slot) {
            var now = Date.now();
            var loaded = slot.unit.loaded || 0;
            if (!slot.sAt) {
                slot.sAt = now;
                slot.sLoaded = loaded;
                return;
            }
            var dt = (now - slot.sAt) / 1000;
            if (dt < 0.5) return;
            var inst = Math.max(0, (loaded - slot.sLoaded) / dt);
            slot.speed = slot.speed == null ? inst : slot.speed * 0.5 + inst * 0.5;
            slot.sAt = now;
            slot.sLoaded = loaded;
        },

        // Suit les téléchargements natifs via l'état serveur ; met à jour vitesses et affichage.
        poll: function () {
            var self = this;
            var byJob = {};
            Object.keys(this.running).forEach(function (k) {
                var slot = self.running[k];
                if (slot.job.mode === 'folder') {
                    self.sampleSpeed(slot);
                    return;
                }
                if (!slot.job.serverJob) return;
                (byJob[slot.job.id] = byJob[slot.job.id] || { job: slot.job, slots: [] }).slots.push(slot);
            });
            Object.keys(byJob).forEach(function (id) {
                var entry = byJob[id];
                apiJson('GET', 'ProDownloader/Jobs/' + entry.job.serverJob).then(function (state) {
                    entry.slots.forEach(function (slot) {
                        self.checkNative(slot, state);
                        self.sampleSpeed(slot);
                    });
                    self.save();
                    self.render();
                }, function (err) {
                    if (err && err.status === 404) {
                        self.forgetServerJob(entry.job);
                        entry.slots.forEach(function (slot) {
                            slot.unit.status = 'pending';
                            if (slot.finish) slot.finish();
                        });
                    }
                });
            });
            this.render();
            this.ensurePolling();
        },

        checkNative: function (slot, state) {
            var isZip = slot.job.mode === 'zip';
            var u = slot.unit;
            var s = (isZip ? state.Zips : state.Files)[slot.index];
            if (!s) return;
            var st = s.State;
            var total = isZip ? s.Size : u.size;
            if (isZip) slot.job.zips[slot.index].size = s.Size;
            if (st.Status === 'done') {
                u.status = 'done';
                u.loaded = total;
                u.hint = null;
                slot.finish();
                return;
            }
            var started = st.Requests > slot.baseRequests;
            if (st.Status === 'active' && started) {
                u.loaded = st.Position;
                u.hint = null;
                slot.lastActivity = Date.now();
                return;
            }
            if (st.Status === 'rejected' && started) {
                u.status = 'pending';
                u.retryAt = Date.now() + 5000;
                slot.finish();
                return;
            }
            if (st.Status === 'failed' && started) {
                // Annulé dans le navigateur, ou connexion coupée : le navigateur peut reprendre seul ;
                // sans nouvelle requête au bout d'un moment, c'est un échec.
                if (!slot.failedAt) slot.failedAt = Date.now();
                if (Date.now() - slot.failedAt > 30000) {
                    u.status = 'failed';
                    u.error = 'interrupted';
                    slot.finish();
                }
                return;
            }
            if (!started && Date.now() - slot.triggeredAt > BROWSER_START_TIMEOUT) {
                // Rien n'est arrivé au serveur : le navigateur a bloqué ce téléchargement.
                u.status = 'failed';
                u.hint = 'browserBlocked';
                slot.finish();
            }
        },

        refreshJobStatus: function (job) {
            if (job.status === 'canceled') return;
            var list = units(job);
            var pending = list.some(function (u) { return u.status === 'pending' || u.status === 'active'; });
            var failed = list.some(function (u) { return u.status === 'failed'; });
            if (!pending) {
                job.status = failed ? 'failed' : 'done';
                if (!failed) {
                    job.finished = Date.now();
                    dirs.del(job.id);
                    if (job.serverJob) apiDelete('ProDownloader/Jobs/' + job.serverJob);
                    job.serverJob = null;
                }
            }
        },

        pause: function (job) {
            job.status = 'paused';
            this.stopJobUnits(job);
            this.save();
            this.render();
        },

        resume: function (job) {
            var self = this;
            var start = function () {
                job.status = 'running';
                units(job).forEach(function (u) {
                    if (u.status === 'failed') { u.status = 'pending'; u.tries = 0; u.hint = null; }
                });
                self.save();
                self.kick();
            };
            if (job.mode !== 'folder') return Promise.resolve(start());
            // Mode Dossier : le navigateur redemande l'accès au dossier (geste utilisateur en cours).
            return dirs.get(job.id).then(function (root) {
                if (!root) return false;
                return ensurePermission(root, true);
            }).then(function (ok) {
                if (ok) start();
                else toast(t('folderPermission'));
            });
        },

        cancel: function (job) {
            job.status = 'canceled';
            this.stopJobUnits(job, true);
            units(job).forEach(function (u) { if (u.status !== 'done') u.status = 'canceled'; });
            if (job.serverJob) apiDelete('ProDownloader/Jobs/' + job.serverJob);
            job.serverJob = null;
            dirs.del(job.id);
            this.save();
            this.render();
        },

        remove: function (job) {
            if (!isFinished(job)) this.cancel(job);
            this.jobs = this.jobs.filter(function (j) { return j !== job; });
            this.save();
            this.render();
        },

        clearHistory: function () {
            this.jobs = this.jobs.filter(function (j) { return !isFinished(j); });
            this.save();
            this.render();
        },

        // Pause : le mode Dossier interrompt ses fichiers ; un téléchargement natif déjà lancé ne
        // s'arrête pas, on continue de le suivre. Annulation : le serveur coupe l'envoi.
        stopJobUnits: function (job, cancel) {
            var self = this;
            Object.keys(this.running).forEach(function (k) {
                var slot = self.running[k];
                if (slot.job !== job) return;
                if (job.mode === 'folder') {
                    if (slot.abort) slot.abort();
                } else if (cancel && slot.finish) {
                    slot.finish();
                }
            });
        },

        /* --- Affichage --- */

        ensureButton: function () {
            injectStyles();
            var visible = this.jobs.length > 0;
            var slot = headerSlot();
            var btn = document.querySelector('.jfpdHeaderButton');
            var floatBtn = document.querySelector('.jfpd-float');
            var self = this;
            if (slot) {
                if (floatBtn) floatBtn.remove();
                if (btn && btn.parentElement !== slot.parent) {
                    btn.remove();
                    btn = null;
                }
                if (!btn) {
                    btn = document.createElement('button');
                    btn.type = 'button';
                    if (slot.template) {
                        // En-tête React (MUI) : mêmes classes que le bouton Rechercher voisin.
                        btn.className = slot.template.className + ' jfpdHeaderButton';
                    } else {
                        btn.setAttribute('is', 'paper-icon-button-light');
                        btn.className = 'headerButton headerButtonRight jfpdHeaderButton paper-icon-button-light';
                    }
                    btn.title = t('downloads');
                    btn.setAttribute('aria-label', t('downloads'));
                    btn.innerHTML = icon('file_download') + '<span class="jfpd-badge"></span>';
                    btn.addEventListener('click', function () { self.togglePanel(); });
                    slot.parent.insertBefore(btn, slot.before);
                }
                btn.classList.toggle('hide', !visible);
            } else {
                if (btn) btn.remove();
                if (!floatBtn && visible) {
                    floatBtn = document.createElement('button');
                    floatBtn.className = 'jfpd-float';
                    floatBtn.title = t('downloads');
                    floatBtn.innerHTML = icon('file_download') + '<span class="jfpd-badge"></span>';
                    floatBtn.addEventListener('click', function () { self.togglePanel(); });
                    document.body.appendChild(floatBtn);
                } else if (floatBtn && !visible) {
                    floatBtn.remove();
                }
            }
            this.updateBadge();
        },

        updateBadge: function () {
            var active = this.jobs.filter(function (j) { return j.status === 'running'; }).length;
            document.querySelectorAll('.jfpd-badge').forEach(function (b) { b.textContent = active ? String(active) : ''; });
        },

        togglePanel: function () {
            if (this.panel) this.hidePanel();
            else this.showPanel();
        },

        showPanel: function () {
            injectStyles();
            if (this.panel) return;
            var self = this;
            var p = document.createElement('div');
            p.className = 'jfpd-panel';
            p.innerHTML = '<div class="jfpd-head"><h2>' + escapeHtml(t('downloads')) + '</h2>'
                + '<button class="jfpd-x" title="' + escapeHtml(t('close')) + '">' + icon('close') + '</button></div>'
                + '<div class="jfpd-body"></div><div class="jfpd-foot"><span class="jfpd-sum"></span><button class="jfpd-link" data-clear>' + escapeHtml(t('clearHistory')) + '</button></div>';
            p.querySelector('.jfpd-x').addEventListener('click', function () { self.hidePanel(); });
            p.querySelector('[data-clear]').addEventListener('click', function () { self.clearHistory(); });
            p.querySelector('.jfpd-body').addEventListener('click', function (e) {
                var b = e.target.closest('[data-act]');
                if (!b) return;
                var job = self.jobs.filter(function (j) { return j.id === b.getAttribute('data-job'); })[0];
                if (!job) return;
                var act = b.getAttribute('data-act');
                if (act === 'pause') self.pause(job);
                else if (act === 'resume' || act === 'retry') self.resume(job);
                else if (act === 'cancel') self.cancel(job);
                else if (act === 'remove') self.remove(job);
            });
            document.body.appendChild(p);
            this.panel = p;
            this.outside = function (e) {
                if (!self.panel) return;
                if (self.panel.contains(e.target) || e.target.closest('.jfpdHeaderButton,.jfpd-float,.jfpd-overlay')) return;
                self.hidePanel();
            };
            setTimeout(function () { document.addEventListener('mousedown', self.outside, true); }, 0);
            this.render();
        },

        hidePanel: function () {
            if (!this.panel) return;
            this.panel.remove();
            this.panel = null;
            document.removeEventListener('mousedown', this.outside, true);
        },

        render: function () {
            this.ensureButton();
            if (!this.panel) return;
            var body = this.panel.querySelector('.jfpd-body');
            if (!this.jobs.length) {
                body.innerHTML = '<div class="jfpd-msg">' + escapeHtml(t('empty')) + '</div>';
                return;
            }
            var self = this;
            body.innerHTML = this.jobs.map(function (job) { return self.jobHtml(job); }).join('');
            this.panel.querySelector('[data-clear]').style.display = this.jobs.some(isFinished) ? '' : 'none';
        },

        jobHtml: function (job) {
            var list = units(job);
            var isZip = job.mode === 'zip';
            var total = 0, loaded = 0, done = 0, failed = 0, speed = 0;
            var self = this;
            list.forEach(function (u, i) {
                var size = isZip ? (u.size || 0) : u.size;
                total += size;
                if (u.status === 'done') { done++; loaded += size; } else loaded += Math.min(u.loaded || 0, size);
                if (u.status === 'failed') failed++;
                var slot = self.running[job.id + ':' + i];
                if (slot && slot.speed) speed += slot.speed;
            });
            var pct = total ? Math.min(100, Math.round(loaded * 100 / total)) : (done === list.length ? 100 : 0);
            var st = { pending: 'stPending', running: 'stRunning', paused: 'stPaused', done: 'stDone', failed: 'stFailed', canceled: 'stCanceled' }[job.status];
            var parts = [t(st)];
            var fileCount = isZip ? job.files.length : list.length;
            var fileDone = isZip
                ? Math.min(fileCount, list.reduce(function (n, z) { return n + (z.status === 'done' ? z.files.length : 0); }, 0))
                : done;
            if (!job.direct) parts.push(t('files', { done: fileDone, total: fileCount }));
            parts.push(formatSize(loaded) + ' / ' + formatSize(total));
            if (job.status === 'running' && speed > 0) parts.push(t('speed', { speed: formatSize(speed) }));
            if (failed) parts.push(t('failedFiles', { n: failed }));
            var hint = list.some(function (u) { return u.hint === 'browserBlocked'; }) ? t('browserBlocked')
                : (job.status === 'paused' && job.mode !== 'folder' && list.some(function (u) { return u.status === 'active'; }) ? t('pausedNative') : '');
            var modeIcon = { folder: 'folder', queue: 'queue', zip: 'folder_zip' }[job.mode] || 'file_download';
            var btns = '';
            var id = escapeHtml(job.id);
            if (job.status === 'running') btns += '<button class="jfpd-icon" data-act="pause" data-job="' + id + '" title="' + escapeHtml(t('pause')) + '">' + icon('pause') + '</button>';
            if (job.status === 'paused') btns += '<button class="jfpd-icon" data-act="resume" data-job="' + id + '" title="' + escapeHtml(t('resume')) + '">' + icon('play_arrow') + '</button>';
            if (job.status === 'failed') btns += '<button class="jfpd-icon" data-act="retry" data-job="' + id + '" title="' + escapeHtml(t('retry')) + '">' + icon('refresh') + '</button>';
            if (!isFinished(job)) btns += '<button class="jfpd-icon" data-act="cancel" data-job="' + id + '" title="' + escapeHtml(t('cancelJob')) + '">' + icon('close') + '</button>';
            else btns += '<button class="jfpd-icon" data-act="remove" data-job="' + id + '" title="' + escapeHtml(t('remove')) + '">' + icon('delete_outline') + '</button>';
            return '<div class="jfpd-job jfpd-st-' + job.status + '">'
                + '<div class="jfpd-job-top">' + icon(modeIcon) + '<span class="jfpd-job-name" title="' + escapeHtml(job.name) + '">' + escapeHtml(job.name) + '</span>' + btns + '</div>'
                + '<div class="jfpd-bar"><div style="width:' + pct + '%"></div></div>'
                + '<div class="jfpd-job-line">' + escapeHtml(parts.join(' · ')) + '</div>'
                + (hint ? '<div class="jfpd-job-hint">' + escapeHtml(hint) + '</div>' : '')
                + '</div>';
        }
    };

    // Où placer le bouton du gestionnaire : en-tête classique (10.11, mise en page « classique ») ou
    // barre React (12.x), à côté de Rechercher ; null = bouton flottant.
    function headerSlot() {
        var legacy = document.querySelector('.skinHeader .headerRight');
        if (legacy && legacy.offsetParent) {
            return { parent: legacy, before: legacy.querySelector('.headerUserButton'), template: null };
        }
        var search = document.querySelector('.MuiAppBar-root a[href*="search"], .MuiToolbar-root a[href*="search"]');
        if (search && search.offsetParent && search.parentElement) {
            return { parent: search.parentElement, before: search.nextSibling, template: search };
        }
        return null;
    }

    function units(job) {
        return job.mode === 'zip' ? job.zips : job.files;
    }

    function isFinished(job) {
        return job.status === 'done' || job.status === 'canceled' || job.status === 'failed';
    }

    function triggerDownload(url) {
        var a = document.createElement('a');
        a.href = url;
        a.download = '';
        a.rel = 'noopener';
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { a.remove(); }, 1000);
    }

    function ensurePermission(handle, ask) {
        var opts = { mode: 'readwrite' };
        return handle.queryPermission(opts).then(function (p) {
            if (p === 'granted') return true;
            if (!ask) return false;
            return handle.requestPermission(opts).then(function (r) { return r === 'granted'; });
        }).catch(function () { return false; });
    }

    // Crée les sous-dossiers de « a/b/c.mkv » et renvoie le fichier (créé s'il n'existe pas).
    function getFileHandle(root, path) {
        var parts = path.split('/').filter(Boolean);
        var name = parts.pop();
        var p = Promise.resolve(root);
        parts.forEach(function (part) {
            p = p.then(function (dir) { return dir.getDirectoryHandle(part, { create: true }); });
        });
        return p.then(function (dir) {
            return dir.getFileHandle(name, { create: true }).then(function (handle) { return { dir: dir, handle: handle }; });
        });
    }

    /* ------------------------------------------------------------------ */
    /*  8. Reprise au chargement                                           */
    /* ------------------------------------------------------------------ */

    function offerResume() {
        var unfinished = manager.jobs.filter(function (j) { return j.status === 'paused' || j.status === 'running' || j.status === 'pending'; });
        if (!unfinished.length) return;
        var left = 0;
        unfinished.forEach(function (j) {
            units(j).forEach(function (u) { if (u.status !== 'done' && u.status !== 'canceled') left += j.mode === 'zip' ? u.files.length : 1; });
        });
        var ui = overlay('jfpd-small');
        ui.dialog.innerHTML = '<div class="jfpd-head"><h2>' + escapeHtml(t('resumeTitle')) + '</h2></div>'
            + '<div class="jfpd-body"><p>' + unfinished.map(function (j) { return '<b>' + escapeHtml(j.name) + '</b>'; }).join('<br>') + '</p>'
            + '<p>' + escapeHtml(t('remaining', { n: left })) + '. ' + escapeHtml(t('resumeText')) + '</p></div>'
            + '<div class="jfpd-foot"><span class="jfpd-sum"></span>'
            + '<button class="jfpd-btn" data-drop>' + escapeHtml(t('resumeDrop')) + '</button>'
            + '<button class="jfpd-btn" data-later>' + escapeHtml(t('resumeLater')) + '</button>'
            + '<button class="jfpd-btn jfpd-primary" data-yes>' + escapeHtml(t('resumeYes')) + '</button></div>';
        ui.dialog.querySelector('[data-later]').addEventListener('click', ui.close);
        ui.dialog.querySelector('[data-drop]').addEventListener('click', function () {
            unfinished.forEach(function (j) { manager.cancel(j); });
            ui.close();
        });
        ui.dialog.querySelector('[data-yes]').addEventListener('click', function () {
            ui.close();
            // Chaque tâche en mode Dossier redemande l'accès pendant ce clic.
            unfinished.forEach(function (j) { manager.resume(j); });
            manager.showPanel();
        });
    }

    /* ------------------------------------------------------------------ */
    /*  9. Démarrage                                                       */
    /* ------------------------------------------------------------------ */

    var started = false;
    var lastUser = null;

    function onUser() {
        var uid = currentUserId();
        if (!uid || uid === lastUser) return;
        lastUser = uid;
        info = null;
        manager.hidePanel();
        loadInfo().then(function (i) {
            if (!i) return;
            manager.load();
            manager.ensureButton();
            if (i.CanDownload) offerResume();
        });
    }

    function start() {
        if (started) return;
        started = true;
        getWebpackRequire().then(function (req) {
            var lastCount = -1;
            var done = false;
            var scan = function () {
                if (done) return;
                var count = Object.keys(req.m || {}).length;
                if (count === lastCount) return;
                lastCount = count;
                scanModules(req);
                hookPlaybackManager();
                hookActionSheet();
                hookShell();
                if (pm && shellObj && actionSheetObj) {
                    done = true;
                    console.info(TAG, 'v' + VERSION + ' – ready');
                }
            };
            // actionSheet n'arrive qu'avec le chunk du menu contextuel, chargé au premier clic sur « ⋮ » :
            // on examine chaque chunk dès son arrivée, avant que le menu ne s'affiche.
            var chunks = self.webpackChunk;
            var origPush = chunks.push;
            chunks.push = function () {
                var r = origPush.apply(this, arguments);
                if (!done) Promise.resolve().then(scan);
                return r;
            };
            scan();
            var timer = setInterval(function () {
                scan();
                if (done) clearInterval(timer);
            }, 1000);
        });
        // Changement d'utilisateur ou de serveur : pas d'évènement public, on vérifie régulièrement.
        setInterval(onUser, 2000);
        onUser();
        // L'en-tête est reconstruit ou change (classique / React) selon les pages.
        var pending = false;
        new MutationObserver(function () {
            if (pending || !manager.jobs.length) return;
            pending = true;
            requestAnimationFrame(function () {
                pending = false;
                var btn = document.querySelector('.jfpdHeaderButton');
                var slot = headerSlot();
                if (slot ? !btn || btn.parentElement !== slot.parent : !document.querySelector('.jfpd-float')) manager.ensureButton();
            });
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
