# Ataraxia — Brand Assets

**Status:** staged for integration. Do not edit `windows/` or `android/` from here — those trees belong to the platform workers. Copy files in per the steps below.

## The mark

A perfect circle resting on a single horizon line — a still moon touching still water. Ataraxia (ἀταραξία) means "freedom from disturbance," and the mark renders that idea as the minimum possible geometry: one disc, one line, nothing moving. The composition is flat (no gradients, no shadows, no decoration), so it never reads as generic AI art; at 16px it resolves to a bold, unmistakable disc, and the horizon line carries the stillness meaning at larger sizes. The line runs full-bleed deliberately — on Android the launcher mask crops it, which reads as the horizon continuing past the frame.

## Palette

| Name | Hex | Use |
|------|-----|-----|
| Deep Ink | `#193456` | Icon tile / adaptive-icon background (solid) |
| Moonstone | `#FFF1D8` | Glyph (circle + horizon line) |
| Black | `#000000` | Monochrome layer base (Android 13+ themed icons; the system tints it) |

Hexes are sampled from the final master artwork (`#193456` = median tile pixel, `#FFF1D8` = median glyph pixel), so the Android background PNGs match the master tile exactly.

## Files

```
branding/
├── ataraxia-icon-master.png      # 1024×1024 master artwork
├── ataraxia.ico                  # Windows icon: 16 / 32 / 48 / 256 px, 32-bit PNG-compressed entries
├── README.md                     # this file
├── android/
│   ├── background_color.txt      # canonical background hex (#193456)
│   ├── ic_launcher.xml           # adaptive-icon definition (background + foreground + monochrome)
│   ├── ic_launcher_round.xml     # round variant, same layers
│   └── mipmap-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/
│       ├── ic_launcher_foreground.png   # glyph on transparent, 108/162/216/324/432 px
│       ├── ic_launcher_background.png   # solid #193456, 108/162/216/324/432 px
│       └── ic_launcher_monochrome.png   # black glyph on transparent (themed icons)
└── work/                         # generation intermediates + verification previews (not shipped)
```

Foreground is full-bleed (108dp canvas) with the glyph positioned exactly as in the master, so background + foreground composite reproduces the master artwork pixel-for-pixel (verified, see `work/preview-composed-xxxhdpi.png`). Small-size legibility render at `work/preview-16px.png`.

## Integration — Windows (WinUI 3, unpackaged)

The app is an unpackaged `WinExe` (`windows/src/Hermes.Client.WinUI3/Hermes.Client.WinUI3.csproj`). Note: `app.manifest` does **not** carry the icon for unpackaged apps — the exe/taskbar icon comes from the native Win32 resource.

1. Copy `branding/ataraxia.ico` → `windows/src/Hermes.Client.WinUI3/Assets/ataraxia.ico`.
2. In `Hermes.Client.WinUI3.csproj`, add to the main `<PropertyGroup>`:
   ```xml
   <ApplicationIcon>Assets\ataraxia.ico</ApplicationIcon>
   ```
   This stamps the exe and taskbar icon. Ensure the `.ico` is copied to the output dir alongside the exe (Content + CopyIfNewer, or the default WinUI content handling).
3. Window/chrome icon at runtime — in `MainWindow.xaml.cs` after the `AppWindow` is available:
   ```csharp
   this.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ataraxia.ico"));
   ```

## Integration — Android (Kotlin)

1. Copy the five `branding/android/mipmap-*/` folders → `android/app/src/main/res/` (merge with existing `res/`).
2. Copy `branding/android/ic_launcher.xml` and `ic_launcher_round.xml` → `android/app/src/main/res/mipmap-anydpi-v26/`.
3. In `android/app/src/main/AndroidManifest.xml`, on the `<application>` element (no `android:icon` is set today):
   ```xml
   android:icon="@mipmap/ic_launcher"
   android:roundIcon="@mipmap/ic_launcher_round"
   ```
4. Optional: replace `ic_launcher_background.png` with a color resource — add `<color name="ic_launcher_background">#193456</color>` to `res/values/colors.xml` and point the `<background>` line in `ic_launcher.xml` at `@color/ic_launcher_background`.

## Verification (2026-10-11)

- `.ico`: parsed the ICO directory with `struct` — 4 entries, 16/32/48/256 px, all 32-bit PNG-compressed; PIL re-opens it cleanly.
- Every PNG opened and asserted at its claimed dimensions (master 1024²; mipmap 108/162/216/324/432).
- Adaptive-icon composite (background + foreground at xxxhdpi) visually matches the master.
- 16px downscale render checked by eye: the disc reads strongly; the horizon line is subtle at that size (acceptable — the disc carries the mark).
