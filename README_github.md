# Custom Portrait Framework

A runtime texture-replacement framework for **Ova Magica** (Unity 6000.3.10f1, IL2CPP). Drop in PNGs named after the game's textures and they're swapped in at runtime — no editing game files, no repacking Addressables bundles, no recompiling. It also includes an optional fix for dialogue text overlapping character portraits.

**Version:** 0.6.0 · **Loader:** BepInEx 6 (IL2CPP, bleeding-edge) · **Platform:** Windows x64

---

## What it does

- **Swaps character art at runtime** — dialogue portraits, quest icons, and most static UI art — by matching a dropped-in PNG to the game's internal texture name.
- **Optional dialogue text inset** — nudges the dialogue text's wrap boundary left so it stops overlapping the portrait (useful if your replacement art occupies more space than vanilla).
- **Diagnostic tooling** for modders — a persistent, de-duplicated catalog of every texture name the game loads, sorted by category, so you can discover what's swappable.

Everything beyond core swapping is **config-gated and off by default**, so a shipped install is clean for end users.

---

## For portrait-pack authors

You don't need to touch this repo to make a pack. Build a folder of PNGs and ship it as a mod that depends on this framework.

### The workflow

Drop PNGs into:

```
…/Ova Magica/BepInEx/plugins/CustomPortraitFramework/Portraits/
```

Each PNG's filename (minus `.png`) must **exactly** match the game's internal texture name.

### Naming rules — this is where packs break

- **Case-sensitive.** `Dialog_Ruby_Angry` ≠ `dialog_ruby_angry`.
- **Match the game's typos.** The game's own asset names are misspelled — e.g. `Suprise` (not "Surprise"), and `Potrait` appears in some names (not "Portrait"). Reproduce them; don't "fix" them or the match fails.
- **Match the exact variant spelling.** The game is inconsistent: `Swim` vs `Swimwear`, `_Full` vs `_Fullbody`, and `notransparent` / `NoTransparent` / `NotTransparent` all coexist across different characters.

### Naming schemes

| Asset type | Pattern | Example |
|---|---|---|
| Dialogue portrait | `Dialog_<Character>_<Expression>` | `Dialog_Jade_Default` |
| Wedding CG | `Wedding_<Character>` | `Wedding_Jade` |
| Artwork CG | `Artwork_<Character>` | `Artwork_Jade` |
| Quest icon | `QuestImage_<Character>` | `QuestImage_Jade` |

A full per-character name list ships in the **Texture Reference** (modder-tools download).

### Finding names yourself

Enable diagnostic logging (see [Config](#configuration)). The framework writes every unique texture name it encounters to `CustomPortraitFramework/seen_textures.txt`, bucketed by prefix and tagged with the hook it came through, e.g. `QuestImage_Coral [ImageSprite]`. The names there are the exact live strings — no transcription, no typos. Trigger the art in-game, then `Ctrl+F` the file.

### PNG tips

- Match the original's dimensions / aspect ratio where possible — mismatched ratios can stretch or offset in the UI frame. Use AssetStudio to check the original's size.
- Keep transparency where the original had it.

---

## Known limitations

- **`Dynamic_` portraits may not swap.** Player and monster portraits (`Dynamic_PlayerPotrait_*`, `Dynamic_BlobPotrait_*`) appear to be composited at runtime rather than loaded as flat textures, so the standard hooks may not catch them. Treat as unsupported.
- **"Swap logged but not visible."** If a sprite was assigned to its UI element before the swap fired, re-open the panel/conversation to trigger a fresh assignment.
- Textures that load through a path neither hook covers won't swap. If a name shows in the diagnostic catalog but won't swap, that's a candidate for a new hook (see [Extending](#extending)).

---

## Configuration

Config lives at `…/BepInEx/config/<framework GUID>.cfg`.

| Setting | Default | Description |
|---|---|---|
| `EnableDiagnosticLogging` | `false` | Turns on texture-name logging and the persistent `seen_textures.txt` catalog. Off for players; on for discovery work. Adds overhead while on. |
| `EnableDialogueInset` | `false` | Enables the dialogue text inset fix. |
| `DialogueTextRightInset` | `200` | Pixels to inset the dialogue text's right wrap boundary, pushing it left to clear the portrait. Tune to taste. |

The inset value is read at apply-time, so it can be tuned live by editing the `.cfg` between dialogue lines (with BepInEx config auto-reload enabled).

---

## Installation (end users)

Brief version — see the full install guide on the mod page.

1. Install **BepInEx 6 bleeding-edge, IL2CPP, win-x64** (`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.*`) into the game folder. Run the game once to generate interop assemblies.
2. Place the `CustomPortraitFramework` folder into `…/BepInEx/plugins/`.
3. Add a portrait pack (or your own PNGs) to the `Portraits` folder.

---

## Building from source

### Requirements

- .NET SDK capable of targeting `net6.0` (the BepInEx IL2CPP runtime is net6/CoreCLR).
- A local Ova Magica install with BepInEx 6 (IL2CPP) installed and **run at least once** — the project references the generated interop assemblies in `…/BepInEx/interop/`.

### Build

```bash
dotnet build -c Release
```

If your game isn't at the default path, point the build at it:

```bash
dotnet build -c Release -p:GameFolder="C:\Path\To\Ova Magica"
```

The build references interop DLLs under `<GameFolder>\BepInEx\interop\` and copies the output DLL into `<GameFolder>\BepInEx\plugins\CustomPortraitFramework\`. The interop folder must exist (run the game once after installing BepInEx) or references won't resolve.

### Notes

- The TMP interop namespace in this environment is `TMPro` (not `Il2CppTMPro`) — match whatever your interop actually exposes.
- Avoid broad assembly type scans (`AccessTools.GetTypesFromAssembly` / type-by-name lookups) against Unity modules; several IL2CPP interop types fail to reconstruct and throw `ReflectionTypeLoadException`. Resolve UI elements by hierarchy traversal instead.

---

## How it works

The game loads textures via Unity Addressables, but the framework intercepts **downstream at the point of use** rather than at load, which sidesteps the Addressables catalog entirely (no CRC patching, no bundle repacking). Two draw paths are hooked:

- **`Sprite.Create`** — dialogue portraits. The texture name is read from the creation call; if a matching PNG exists, the source texture is substituted before the sprite is built.
- **`UnityEngine.UI.Image.sprite`** (setter) — quest icons and most static UI art. The incoming sprite's name is matched; a replacement sprite built from the cached PNG is substituted.

Loaded PNGs are cached as `Texture2D` so repeated swaps don't re-read from disk.

The **dialogue inset** feature walks the UI hierarchy (`Canvas > UIDialogueView > Dialogue Panel > Text Panel > SubtitleTextMeshPro`) and sets the TMP element's right `margin`, moving the text wrap boundary left. It reapplies while the dialogue view is active in case the layout recomputes.

---

## Extending

If a texture loads through a path the framework doesn't hook (shows in the diagnostic catalog but won't swap), the place to add support is a new interception point following the same pattern: read the name at the point of use, match against the loaded PNG dictionary, substitute. `RawImage.texture` and additional setters are natural candidates. PRs welcome.

---

## Credits

The first modding framework for Ova Magica. Built to spare everyone else the Unity 6 / IL2CPP groundwork.

## License

See `LICENSE`. Note that BepInEx and its dependencies carry their own licenses; this repo does not redistribute them.
