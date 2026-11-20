# Copy Paste

A PlateUp mod for copying an appliance's settings and pasting them onto other appliances of the same type, at night, in Advanced Build Mode.

## How to use

At night, switch on **Advanced Build Mode** (hold ping), move the crane over an appliance, and:

| | Keyboard | Controller |
| --- | --- | --- |
| **Copy** | **Hold K** (half a second) | **Hold Y** |
| **Paste** | **Tap K** | **Tap Y** |

These are the game's own ready-up button, so if you've changed it in the game's controls menu, use your own key. Copy and paste need empty hands.

Paste only works on an appliance of the **same type** as the one you copied.

| Appliance | What gets copied |
| --- | --- |
| Rotating grabbers | The direction they push, relative to the grabber (it turns with the grabber, like the base game) |
| Smart grabbers | The item they're set to grab (an unset filter is copied too, so pasting it clears the target's filter) |
| Appliances that switch what they provide | The selected item |

In the base game these settings can only be changed during the day or in practice. Copy Paste lets you set them up at night, while you build. The changes are kept when the day starts and when the game is saved.

Each player has their own clipboard, with one slot per appliance type. Copying a rotating grabber doesn't forget the smart grabber you copied earlier. Clipboards last until the host closes the game.

### What you see

- **Popups:** a short label over the appliance: "Copied", "Pasted", "Already the same", "Copy one of these first" or "Nothing to copy".
- **Preview:** with the crane over an appliance of a type you copied, a ghost of what a paste would do: an arrow for a grabber's direction, or the filter or selected item spinning above it. It only appears when a paste would change something.

Both can be turned off in the settings.

### Things to know

- **Needs Advanced Build Mode,** the franchise upgrade that adds the crane. The mod does nothing without it.
- **Why the ready-up button:** while anyone is in Advanced Build Mode, the base game doesn't count ready-up votes, so the day can't start by accident.
- **Only the host needs the mod.** Online players without it can copy and paste too, because the game already sends their buttons to the host.
- **The popups and the preview only show on the host's screen** (including couch co-op players there). Online players see the result: a grabber's arrow or filter icon changes as usual.
- **[Blueprint Shop](https://github.com/Zambie223/PlateUp-BlueprintShop):** its build queue uses the same button while you carry a queued item. Copy Paste ignores the button while your hands are full, so they don't clash.

## Settings

The settings are in `CopyPaste.cfg`, in the game's data folder (`%USERPROFILE%\AppData\LocalLow\It's Happening\PlateUp\`). If you have the [Mod List](https://github.com/Zambie223/PlateUp-ModList) mod (0.2.0 or later), you can also change them in-game: Pause → Mods → the gear next to Copy Paste. Only the host's settings count.

| Setting | Default | Meaning |
| --- | --- | --- |
| `enabled` | `true` | Turns copy and paste on or off. |
| `hold_to_copy_ms` | `500` | How long the button must be held to copy, from 200 to 2000 ms. Shorter presses paste. |
| `show_popups` | `true` | Show the "Copied" / "Pasted" labels. |
| `show_preview` | `true` | Show the preview over a matching appliance before pasting. |

## Install

1. **Download** `CopyPaste-<version>.zip` from the [Releases page](https://github.com/Zambie223/PlateUp-CopyPaste/releases).
2. **Unzip it into PlateUp's `Mods` folder,** so you end up with `...\PlateUp\PlateUp\Mods\CopyPaste\CopyPaste.dll`.
   - To find the folder in Steam: right-click PlateUp, then Manage, then Browse local files, and open the `PlateUp` folder.
   - Create the `Mods` folder if it doesn't exist.
   - Keep the `.pdb` next to the `.dll`: it adds line numbers to error reports.
3. **Start the game.** No other mods are required. [Mod List](https://github.com/Zambie223/PlateUp-ModList) 0.2.0+ is optional, for changing the settings in-game.

**To uninstall,** delete the `Mods\CopyPaste` folder. Settings already pasted stay as they are, since they're ordinary game settings.

## Building

You need the .NET SDK 8 or newer and PlateUp installed. If PlateUp isn't in the default Steam folder, create `Directory.Build.local.props` next to `Directory.Build.props`, or set the `PLATEUP_GAME_DIR` environment variable (see the comment at the top of `Directory.Build.props`). Then run:

```
dotnet build -c Release
```

A successful build copies the mod into the game's `Mods` folder.

## Responsible AI use disclosure

This mod was written with the help of an AI coding assistant (Anthropic's Claude), working under the author's direction. The design decisions were made by the author. The assistant researched the game's code, wrote the implementation and the documentation, and the author reviewed the results. Testing was done in-game by the author.
