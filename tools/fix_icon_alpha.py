"""Convert baked checkerboard backgrounds to real alpha and recrop icons."""

from __future__ import annotations

from collections import deque
from pathlib import Path

from PIL import Image

SRC = Path(r"C:\Users\fhild\.cursor\projects\d-Tools-SlopClean\assets")
APP_OUT = Path(r"D:\Tools\SlopClean\src\SlopClean.App\Assets")
SRC_ROOT = Path(r"D:\Tools\SlopClean\src")

MODULE_PROJECTS = [
    "SlopClean.Modules.TempCleaner",
    "SlopClean.Modules.BrowserCleaner",
    "SlopClean.Modules.RecycleBin",
    "SlopClean.Modules.StartupManager",
    "SlopClean.Modules.DiskAnalyzer",
    "SlopClean.Modules.UninstallCleanup",
    "SlopClean.Modules.ServiceAdvisor",
    "SlopClean.Modules.CoreIsolationDrivers",
]


def is_background(r: int, g: int, b: int, a: int) -> bool:
    if a < 12:
        return True
    # Near-black padding from bad crops
    if r + g + b < 45:
        return True
    mx, mn = max(r, g, b), min(r, g, b)
    sat = mx - mn
    lum = (r + g + b) / 3.0
    # Baked checkerboard / light gray / white margin (not saturated icon fills)
    if sat < 28 and lum >= 95:
        return True
    if sat < 18 and lum >= 70:
        return True
    return False


def remove_background(im: Image.Image) -> Image.Image:
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    visited = [[False] * w for _ in range(h)]
    q: deque[tuple[int, int]] = deque()

    def try_enqueue(x: int, y: int) -> None:
        if x < 0 or y < 0 or x >= w or y >= h or visited[y][x]:
            return
        r, g, b, a = px[x, y]
        if not is_background(r, g, b, a):
            return
        visited[y][x] = True
        q.append((x, y))

    for x in range(w):
        try_enqueue(x, 0)
        try_enqueue(x, h - 1)
    for y in range(h):
        try_enqueue(0, y)
        try_enqueue(w - 1, y)

    while q:
        x, y = q.popleft()
        px[x, y] = (0, 0, 0, 0)
        try_enqueue(x + 1, y)
        try_enqueue(x - 1, y)
        try_enqueue(x, y + 1)
        try_enqueue(x, y - 1)

    return im


def content_bbox(im: Image.Image, alpha_min: int = 16) -> tuple[int, int, int, int] | None:
    px = im.load()
    w, h = im.size
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] < alpha_min:
                continue
            minx = min(minx, x)
            miny = min(miny, y)
            maxx = max(maxx, x)
            maxy = max(maxy, y)
    if maxx < 0:
        return None
    return minx, miny, maxx + 1, maxy + 1


def fit_square(im: Image.Image, size: int = 256, pad_ratio: float = 0.0) -> Image.Image:
    """Crop to opaque content and center on a transparent square canvas."""
    im = remove_background(im)
    bbox = content_bbox(im)
    if bbox is None:
        return Image.new("RGBA", (size, size), (0, 0, 0, 0))
    cropped = im.crop(bbox)
    side = max(cropped.size)
    pad = max(0, int(round(side * pad_ratio)))
    canvas_side = max(side + pad * 2, 1)
    canvas = Image.new("RGBA", (canvas_side, canvas_side), (0, 0, 0, 0))
    canvas.paste(cropped, ((canvas_side - cropped.width) // 2, (canvas_side - cropped.height) // 2), cropped)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def write_app_branding(icon: Image.Image) -> None:
    APP_OUT.mkdir(parents=True, exist_ok=True)
    icon256 = fit_square(icon, 256, pad_ratio=0.0)
    icon256.save(APP_OUT / "BrandMark.png")
    icon256.save(
        APP_OUT / "AppIcon.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    icon256.resize((300, 300), Image.Resampling.LANCZOS).save(APP_OUT / "Square150x150Logo.scale-200.png")
    icon256.resize((88, 88), Image.Resampling.LANCZOS).save(APP_OUT / "Square44x44Logo.scale-200.png")
    icon256.resize((24, 24), Image.Resampling.LANCZOS).save(
        APP_OUT / "Square44x44Logo.targetsize-24_altform-unplated.png"
    )
    icon256.resize((48, 48), Image.Resampling.LANCZOS).save(
        APP_OUT / "Square44x44Logo.targetsize-48_altform-lightunplated.png"
    )
    icon256.resize((48, 48), Image.Resampling.LANCZOS).save(APP_OUT / "LockScreenLogo.scale-200.png")
    icon256.resize((50, 50), Image.Resampling.LANCZOS).save(APP_OUT / "StoreLogo.png")

    # Splash / wide: use cleaned brand on a solid gradient (no checkerboard).
    splash = Image.new("RGBA", (1240, 600), (0, 0, 0, 0))
    # teal → emerald fill
    for y in range(600):
        t = y / 599
        r = int(13 + (4 - 13) * t)
        g = int(148 + (120 - 148) * t)
        b = int(136 + (87 - 136) * t)
        for x in range(1240):
            splash.putpixel((x, y), (r, g, b, 255))
    mark = icon256.resize((220, 220), Image.Resampling.LANCZOS)
    splash.paste(mark, ((1240 - 220) // 2, (600 - 220) // 2), mark)
    splash.save(APP_OUT / "SplashScreen.scale-200.png")
    splash.resize((620, 300), Image.Resampling.LANCZOS).save(APP_OUT / "Wide310x150Logo.scale-200.png")
    print("wrote app branding")


def write_module_illustrations(sheet: Image.Image) -> None:
    cleaned = remove_background(sheet)
    bbox = content_bbox(cleaned)
    assert bbox is not None
    region = cleaned.crop(bbox)
    rw, rh = region.size
    cols, rows = 4, 2
    cell_w = rw / cols
    cell_h = rh / rows

    for i, project in enumerate(MODULE_PROJECTS):
        r, c = divmod(i, cols)
        # slight inset to avoid neighboring tile bleed, then re-fit tightly
        inset_x = cell_w * 0.02
        inset_y = cell_h * 0.02
        cell = region.crop(
            (
                int(c * cell_w + inset_x),
                int(r * cell_h + inset_y),
                int((c + 1) * cell_w - inset_x),
                int((r + 1) * cell_h - inset_y),
            )
        )
        tile = fit_square(cell, 256, pad_ratio=0.0)
        dest = SRC_ROOT / project / "Assets"
        dest.mkdir(parents=True, exist_ok=True)
        path = dest / "illustration.png"
        tile.save(path)
        # sanity: corners must be transparent, center opaque
        px = tile.load()
        assert px[0, 0][3] == 0, path
        assert px[128, 128][3] > 200, path
        print(f"wrote {project}/Assets/illustration.png")


def main() -> None:
    write_app_branding(Image.open(SRC / "slopclean-app-icon-source.png"))
    write_module_illustrations(Image.open(SRC / "slopclean-modules-sheet.png"))
    print("done")


if __name__ == "__main__":
    main()
