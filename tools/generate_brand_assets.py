"""One-shot helper: resize generated brand art into App + module assemblies."""

from __future__ import annotations

from pathlib import Path

from PIL import Image

SRC = Path(r"C:\Users\fhild\.cursor\projects\d-Tools-SlopClean\assets")
APP_OUT = Path(r"D:\Tools\SlopClean\src\SlopClean.App\Assets")
SRC_ROOT = Path(r"D:\Tools\SlopClean\src")

MODULE_PROJECTS = [
    ("temp-cleaner", "SlopClean.Modules.TempCleaner"),
    ("browser-cleaner", "SlopClean.Modules.BrowserCleaner"),
    ("recycle-bin", "SlopClean.Modules.RecycleBin"),
    ("startup-manager", "SlopClean.Modules.StartupManager"),
    ("disk-analyzer", "SlopClean.Modules.DiskAnalyzer"),
    ("uninstall-cleanup", "SlopClean.Modules.UninstallCleanup"),
    ("service-advisor", "SlopClean.Modules.ServiceAdvisor"),
    ("core-isolation-drivers", "SlopClean.Modules.CoreIsolationDrivers"),
]


def square_pad(im: Image.Image) -> Image.Image:
    bbox = im.getbbox()
    if bbox:
        im = im.crop(bbox)
    side = max(im.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
    return canvas


def trim_tile(im: Image.Image) -> Image.Image:
    px = im.load()
    w, h = im.size
    minx, miny, maxx, maxy = w, h, 0, 0
    found = False
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 20:
                continue
            if r < 40 and g < 40 and b < 40 and a > 200:
                continue
            found = True
            minx = min(minx, x)
            miny = min(miny, y)
            maxx = max(maxx, x)
            maxy = max(maxy, y)
    if not found:
        return square_pad(im)
    pad = 4
    cropped = im.crop(
        (
            max(0, minx - pad),
            max(0, miny - pad),
            min(w, maxx + 1 + pad),
            min(h, maxy + 1 + pad),
        )
    )
    return square_pad(cropped)


def write_app_branding() -> None:
    APP_OUT.mkdir(parents=True, exist_ok=True)
    icon = square_pad(Image.open(SRC / "slopclean-app-icon-source.png").convert("RGBA"))
    icon256 = icon.resize((256, 256), Image.Resampling.LANCZOS)
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
    icon256.save(APP_OUT / "BrandMark.png")

    splash = Image.open(SRC / "slopclean-splash-source.png").convert("RGBA")
    splash.resize((1240, 600), Image.Resampling.LANCZOS).save(APP_OUT / "SplashScreen.scale-200.png")
    splash.resize((620, 300), Image.Resampling.LANCZOS).save(APP_OUT / "Wide310x150Logo.scale-200.png")


def write_module_illustrations() -> None:
    sheet = Image.open(SRC / "slopclean-modules-sheet.png").convert("RGBA")
    content = sheet.getbbox()
    assert content is not None
    region = sheet.crop(content)
    rw, rh = region.size
    cols, rows = 4, 2
    cell_w = rw / cols
    cell_h = rh / rows

    for i, (_, project) in enumerate(MODULE_PROJECTS):
        r, c = divmod(i, cols)
        inset_x = cell_w * 0.04
        inset_y = cell_h * 0.04
        cell = region.crop(
            (
                int(c * cell_w + inset_x),
                int(r * cell_h + inset_y),
                int((c + 1) * cell_w - inset_x),
                int((r + 1) * cell_h - inset_y),
            )
        )
        tile = trim_tile(cell).resize((256, 256), Image.Resampling.LANCZOS)
        dest_dir = SRC_ROOT / project / "Assets"
        dest_dir.mkdir(parents=True, exist_ok=True)
        path = dest_dir / "illustration.png"
        tile.save(path)
        print(f"wrote {project}/Assets/illustration.png")


def main() -> None:
    write_app_branding()
    write_module_illustrations()
    print("brand assets ready")


if __name__ == "__main__":
    main()
