#!/usr/bin/env python3
"""Dessine icon.png (1280x720, vignette du catalogue Jellyfin), dans le style des autres plugins k1k4k.

    python scripts/make_icon.py [--out icon.png]

Une pile de fichiers qui tombe un par un dans un bac (flèche bleue), « Jellyfin » en blanc,
« Pro Downloader » en bleu, bandeau dégradé en bas. Police Segoe UI Bold (Windows), sinon DejaVu Sans Bold.
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1280, 720
SCALE = 4  # dessin en grand puis réduction : bords lisses
BG = (11, 12, 14)
WHITE = (255, 255, 255)
BLUE = (13, 193, 245)
PURPLE = (155, 48, 255)


def font(size):
    for path in (r"C:\Windows\Fonts\segoeuib.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "icon.png"))
    out = ap.parse_args().out

    s = SCALE
    img = Image.new("RGB", (W * s, H * s), BG)
    d = ImageDraw.Draw(img)
    stroke = 20 * s

    # Pile de fichiers en attente (trois cartes décalées), en haut à gauche du bac.
    for i, (x, y) in enumerate(((150, 200), (178, 228), (206, 256))):
        box = (x * s, y * s, (x + 104) * s, (y + 78) * s)
        d.rounded_rectangle(box, radius=12 * s, fill=BG, outline=WHITE, width=12 * s)

    # Flèche bleue : le fichier suivant descend dans le bac.
    ax = 300 * s
    d.rounded_rectangle((ax - 15 * s, 236 * s, ax + 15 * s, 382 * s), radius=8 * s, fill=BLUE)
    d.polygon([(ax - 52 * s, 352 * s), (ax + 52 * s, 352 * s), (ax, 412 * s)], fill=BLUE)

    # Bac de téléchargement : U blanc.
    x0, x1, y0, y1 = 140 * s, 384 * s, 372 * s, 470 * s
    d.rounded_rectangle((x0, y1 - stroke, x1, y1), radius=10 * s, fill=WHITE)
    d.rounded_rectangle((x0, y0, x0 + stroke, y1), radius=10 * s, fill=WHITE)
    d.rounded_rectangle((x1 - stroke, y0, x1, y1), radius=10 * s, fill=WHITE)

    # Texte : « Jellyfin » / « Pro Downloader », réduit si besoin pour tenir dans la largeur.
    left, right = 450 * s, (W - 70) * s
    size = 96
    while size > 40:
        title, name = font(130 * s * size // 96), font(size * s)
        if d.textlength("Pro Downloader", font=name) <= right - left:
            break
        size -= 2
    d.text((left, 360 * s), "Jellyfin", font=title, fill=WHITE, anchor="ls")
    d.text((left, 470 * s), "Pro Downloader", font=name, fill=BLUE, anchor="ls")

    # Bandeau dégradé en bas.
    band = 30 * s
    for x in range(W * s):
        t = x / (W * s - 1)
        color = tuple(round(BLUE[i] + (PURPLE[i] - BLUE[i]) * t) for i in range(3))
        d.line((x, H * s - band, x, H * s), fill=color)

    img.resize((W, H), Image.LANCZOS).save(out, optimize=True)
    print(out)


if __name__ == "__main__":
    main()
