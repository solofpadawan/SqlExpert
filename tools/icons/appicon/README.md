# Application icon

Original artwork for `SqlExpert.App/Assets/sqlexpert.ico` (transparent background, 9 sizes: 16, 20, 24, 32, 40, 48, 64, 128 and 256 px, 32-bit alpha).

- `genT.py` builds `sqlexpert-T.svg` (master, 256 px), `tuned/*.svg` (simplified shapes for small sizes),
  `out/png/*.png` and `out/sqlexpert.ico`. Sizes 16 to 40 px are drawn pixel by pixel so they stay crisp.
- `previewT.py` writes `out/sqlexpert-appicon-preview.png` (light, dark and blue backgrounds, desktop and taskbar).
- `textpath.py` turns "SQL" into outlines with the Inter Tight font (weight 800, SIL Open Font License).
  Set `SQLEXPERT_ICON_FONT` to the variable font file when it is not at the default path.

Requirements: Python 3, Pillow, cairosvg, fontTools.

```
cd tools/icons/appicon
python genT.py
python previewT.py
copy out\sqlexpert.ico ..\..\..\SqlExpert.App\Assets\sqlexpert.ico
```
