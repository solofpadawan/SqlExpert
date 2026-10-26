# Application icon

Original artwork for `SqlExpert.App/Assets/sqlexpert.ico` (transparent background, 9 sizes: 16, 20, 24, 32, 40, 48, 64, 128 and 256 px, 32-bit alpha).

Current variant: **T2** (the T drawing with larger squares).

- `genT2.py` builds `sqlexpert-T2.svg` (master, 256 px), `tuned/sqlexpert-T2-48.svg` (48 px tier),
  `out/png/sqlexpert-T2-*.png` and `out/sqlexpert.ico`. Sizes 16 to 40 px are drawn pixel by pixel so they stay crisp.
- `genT.py` is the previous variant T (`sqlexpert-T.svg`, `tuned/sqlexpert-T-*.svg`); `genT2.py` reuses its drawing code.
- `previewT2.py` writes `out/sqlexpert-appicon-preview-T2.png` (T and T2 side by side; desktop, light and dark backgrounds).
  `previewT.py` is the preview of variant T.
- `textpath.py` turns "SQL" into outlines with the Inter Tight font (weight 800, SIL Open Font License).
  Set `SQLEXPERT_ICON_FONT` to the variable font file when it is not at the default path.

Requirements: Python 3, Pillow, cairosvg, fontTools.

```
cd tools/icons/appicon
python genT2.py
python previewT2.py
copy out\sqlexpert.ico ..\..\..\SqlExpert.App\Assets\sqlexpert.ico
```

Windows Explorer caches icons: after an update, a desktop or Start menu shortcut may keep showing the old icon
until the icon cache is refreshed (for example `ie4uinit.exe -show`, or sign out and back in).
