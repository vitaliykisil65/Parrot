# Design

The mockups the current interface was designed from. Open [index.html](index.html) to see them
all on one page, or open any board in `mockups/` directly — each is a self-contained HTML file.
`canvas.json` only records where the boards sat on the canvas they were drawn on.

The boards show Ukrainian UI text because that is the language the design was reviewed in; the
app itself now ships English by default.

## Decisions

- **One window, one frame.** Dictionary, statistics and settings are pages of a single window
  with a custom title bar, not separate windows. The sidebar carries navigation, decks, the
  streak, the prompt schedule and — when there is one — the update card.
- **Palette D, "yellow leads".** Yellow is the accent and carries the primary buttons; cobalt is
  the supporting colour for data: difficulty bars, charts, progress. Neutrals are warm
  (`#F7F5EF` page, `#FFFFFF` cards) in light, cool (`#0E131A` / `#151B24`) in dark.
  Palette A ("cobalt and sun") was the runner-up and is kept in `mockups-palette-A/`.
- **Logo, direction C.** A blue macaw over a yellow disc. It is described once as path geometry
  in `src/Parrot.Core/Branding/ParrotLogo.cs` and feeds the window icon, the .ico and the tray;
  on a dark taskbar the tray icon is the outline version.
- **Quiet surfaces.** Flat fills, one hairline border, 14–16 px corners, no shadows except under
  the floating prompt. The prompt window never steals focus, so it must read at a glance.

## Tokens

The palette lives in `src/Parrot.App/Themes/Light.xaml` and `Dark.xaml` as `Brush.*` resources;
everything else references those, so a theme swap is one dictionary. The most used ones:

| Token | Light | Dark | Used for |
|---|---|---|---|
| `Brush.Window` | `#F7F5EF` | `#0E131A` | window background |
| `Brush.Surface` | `#FFFFFF` | `#151B24` | cards, panels |
| `Brush.Text` | `#14202E` | `#EEF2F7` | body text |
| `Brush.TextMuted` | `#5E6878` | `#8C98A8` | captions |
| `Brush.Line` | `#EFEDE6` | `#232C38` | hairlines |
| `Brush.Primary` | `#FFC23A` | `#FFC23A` | accent, primary buttons |
| `Brush.Active` | `#FFF1CC` | `#3A2E10` | selected rows, soft buttons |
| `Brush.Info` | `#2F6BF2` | `#5B9BFF` | data, links, progress |
| `Brush.Danger` | `#C2413A` | `#FF6B60` | destructive actions |

Icons are stroke geometries on a 24×24 grid in `src/Parrot.App/Themes/Icons.xaml`, drawn by the
`Icon` control with round caps; controls receive one through the `Ui.Icon` attached property.
