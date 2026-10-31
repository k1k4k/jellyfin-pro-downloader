#!/usr/bin/env bash
# Installe (ou désinstalle) le plugin Jellyfin Pro Downloader sans passer par le catalogue de Jellyfin,
# sur un serveur Linux (paquet Debian / Ubuntu : plugins dans /var/lib/jellyfin/plugins).
#
#   sudo ./install.sh                         # dernière release GitHub, variante choisie selon la version du serveur
#   sudo ./install.sh --zip jellyfin-pro-downloader_0.1.0.0_jf12.zip
#   sudo ./install.sh --plugins-dir /chemin/plugins --url http://localhost:8096 --restart
#   sudo ./install.sh --uninstall
#
# Copie Jellyfin.Plugin.ProDownloader.dll dans <plugins>/Jellyfin Pro Downloader_<version>/ avec son meta.json, comme
# le catalogue de Jellyfin, et retire les anciennes versions de ce plugin. Redémarrer Jellyfin ensuite (--restart :
# systemctl restart jellyfin).
set -euo pipefail

GUID="114272b4-a63b-4036-84be-843fc6552d61"
NAME="Jellyfin Pro Downloader"
DLL="Jellyfin.Plugin.ProDownloader.dll"
REPO="k1k4k/jellyfin-pro-downloader"

PLUGINS_DIR="/var/lib/jellyfin/plugins"
URL="http://localhost:8096"
TARGET=""
ZIP=""
RESTART=0
UNINSTALL=0

while [ $# -gt 0 ]; do
  case "$1" in
    --plugins-dir) PLUGINS_DIR="$2"; shift 2 ;;
    --url) URL="$2"; shift 2 ;;
    --target) TARGET="$2"; shift 2 ;;
    --zip) ZIP="$2"; shift 2 ;;
    --restart) RESTART=1; shift ;;
    --uninstall) UNINSTALL=1; shift ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "Option inconnue : $1" >&2; exit 2 ;;
  esac
done

[ -d "$PLUGINS_DIR" ] || { echo "Dossier des plugins introuvable : $PLUGINS_DIR (--plugins-dir)" >&2; exit 1; }

# Dossiers de ce plugin (toutes versions) : meta.json avec notre GUID, ou nom « Jellyfin Pro Downloader_x ».
plugin_folders() {
  for d in "$PLUGINS_DIR"/*/; do
    [ -d "$d" ] || continue
    d="${d%/}"
    if { [ -f "$d/meta.json" ] && grep -q "$GUID" "$d/meta.json"; } || [[ "$(basename "$d")" == "$NAME"_* ]]; then
      printf '%s\n' "$d"
    fi
  done
}

restart_hint() {
  if [ "$RESTART" = 1 ] && command -v systemctl >/dev/null 2>&1; then
    systemctl restart jellyfin && echo "Jellyfin redémarré."
  else
    echo "Redémarrez Jellyfin pour appliquer : sudo systemctl restart jellyfin"
  fi
}

if [ "$UNINSTALL" = 1 ]; then
  found=0
  while IFS= read -r d; do
    [ -n "$d" ] || continue
    rm -rf -- "$d" && echo "Supprimé : $d"
    found=1
  done < <(plugin_folders)
  [ "$found" = 1 ] || echo "Jellyfin Pro Downloader n'est pas installé dans $PLUGINS_DIR."
  echo "Les tâches de téléchargement en cours restent dans <données Jellyfin>/data/pro-downloader (peut être supprimé)."
  restart_hint
  exit 0
fi

# --- Variante : Jellyfin 12.x ou 10.11.x
if [ -z "$TARGET" ]; then
  version="$(curl -fsS --max-time 5 "${URL%/}/System/Info/Public" 2>/dev/null | sed -n 's/.*"Version":"\([^"]*\)".*/\1/p' || true)"
  case "$version" in
    10.11.*) TARGET="jf10" ;;
    1[2-9].*) TARGET="jf12" ;;
    "") echo "Version de Jellyfin inconnue ($URL injoignable) : indiquez --target jf12 (12.x) ou --target jf10 (10.11.x)." >&2; exit 1 ;;
    *) echo "Jellyfin $version n'est pas pris en charge (12.x ou 10.11.x)." >&2; exit 1 ;;
  esac
  echo "Jellyfin $version : variante $TARGET"
fi
case "$TARGET" in
  jf12) ABI="12.0.0.0" ;;
  jf10) ABI="10.11.0.0" ;;
  *) echo "--target : jf12 ou jf10" >&2; exit 2 ;;
esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# --- La DLL : zip donné, sinon dernière release GitHub
if [ -z "$ZIP" ]; then
  echo "Recherche de la dernière release de $REPO..."
  asset="$(curl -fsS -H 'User-Agent: jfpd-install' "https://api.github.com/repos/$REPO/releases" \
    | grep -o "\"browser_download_url\": *\"[^\"]*_${TARGET}\.zip\"" | head -n 1 | sed 's/.*"\(https[^"]*\)"/\1/' || true)"
  [ -n "$asset" ] || { echo "Aucune release publiée pour $TARGET : utilisez --zip." >&2; exit 1; }
  ZIP="$work/$(basename "$asset")"
  curl -fsSL -o "$ZIP" "$asset"
  echo "Téléchargé : $(basename "$asset")"
fi
[ -f "$ZIP" ] || { echo "Zip introuvable : $ZIP" >&2; exit 1; }
if command -v unzip >/dev/null 2>&1; then
  unzip -q -o "$ZIP" -d "$work/dll"
else
  python3 -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$ZIP" "$work/dll"
fi
[ -f "$work/dll/$DLL" ] || { echo "$DLL absent du zip." >&2; exit 1; }

# Version : celle du nom du zip (jellyfin-pro-downloader_<version>_<cible>.zip).
version="$(basename "$ZIP" | sed -n 's/.*_\([0-9][0-9.]*\)_jf1[02]\.zip$/\1/p')"
[ -n "$version" ] || { echo "Version introuvable dans le nom du zip : $(basename "$ZIP")" >&2; exit 1; }

dest="$PLUGINS_DIR/${NAME}_${version}"
while IFS= read -r d; do
  [ -n "$d" ] && [ "$d" != "$dest" ] && rm -rf -- "$d" && echo "Ancienne version supprimée : $d"
done < <(plugin_folders)

mkdir -p "$dest"
cp "$work/dll/$DLL" "$dest/$DLL"
cat > "$dest/meta.json" <<EOF
{
  "category": "General",
  "changelog": "",
  "description": "Télécharger une série, un album ou une collection sans lancer des centaines de téléchargements d'un coup.",
  "guid": "$GUID",
  "name": "$NAME",
  "overview": "Téléchargements groupés maîtrisés : sélection, file d'attente, ZIP.",
  "owner": "k1k4k",
  "targetAbi": "$ABI",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "$version",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": []
}
EOF

# Mêmes droits que le dossier des plugins (utilisateur jellyfin sur les installations par paquet).
owner="$(stat -c '%U:%G' "$PLUGINS_DIR" 2>/dev/null || true)"
[ -n "$owner" ] && chown -R "$owner" "$dest" 2>/dev/null || true

echo "Installé : $dest ($TARGET, version $version)"
restart_hint
echo "Puis rechargez la page web de Jellyfin (Ctrl+F5) : « Télécharger… » apparaît dans le menu ⋮ des séries, albums, collections…"
