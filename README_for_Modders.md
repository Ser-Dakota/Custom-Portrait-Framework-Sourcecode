# Custom Portrait Framework — Modder's Guide

**Version 0.5.0** · For *Ova Magica* (Unity 6 / IL2CPP)

This framework lets you replace in-game character art — dialogue portraits, quest icons, wedding/artwork CGs — by simply dropping PNG files into a folder. No coding, no editing game files, no repacking bundles. Name your image after the texture it replaces and the framework swaps it in at runtime.

This guide is for **modders building portrait packs**. If you just want to *install* the framework, see the install guide on the mod's main page.

---

## Quick start

1. Make sure the Custom Portrait Framework is installed (see the main mod page).
2. Find the framework's portraits folder:
   `…/Ova Magica/BepInEx/plugins/CustomPortraitFramework/Portraits/`
3. Create a PNG named **exactly** after the texture you want to replace — e.g. `Dialog_Ruby_Angry.png`.
4. Drop it in the folder. Launch the game. Done.

That's the whole workflow. Everything below is detail for when something doesn't behave.

---

## ⚠️ Naming rules — read this, it's where everyone trips

The framework matches your PNG filename against the game's internal texture name **exactly**. The filename (minus `.png`) must equal the texture name character-for-character.

- **Case-sensitive.** `Dialog_Ruby_Angry` ≠ `dialog_ruby_angry`.
- **Match the game's typos.** The game's own asset names contain misspellings — e.g. `Suprise` (not "Surprise"), and `Potrait` appears in some internal names (not "Portrait"). Do **not** correct them. If you "fix" the spelling, your file won't match and won't swap.
- **Match the exact variant spelling.** The game is wildly inconsistent — `Swim` vs `Swimwear`, `_Full` vs `_Fullbody`, and three different transparency spellings (`notransparent`, `NoTransparent`, `NotTransparent`) all exist across different characters. Copy the exact string; don't normalize it.

When a swap silently fails, the name is almost always why. Use the included **Texture Reference** doc, or the diagnostic logging below, to confirm the live name.

---

## Finding texture names

Two ways:

**1. The Texture Reference doc** (included in this zip). A character-by-character list of known swappable names — dialogue portraits, swimwear, wedding, artwork, and quest icons. Start here.

**2. Diagnostic logging** (for names not in the doc, or to verify). The framework can log every texture/sprite name it sees, the exact live string, straight from the engine — no transcription, no typos. See below.

### Turning on diagnostic logging

In the framework's config file:
`…/BepInEx/config/` → the Custom Portrait Framework entry → set **`EnableDiagnosticLogging`** to `true`.

With it on:
- Every unique texture name the game loads is recorded once to `…/CustomPortraitFramework/seen_textures.txt`, sorted into buckets by prefix (`[Dialog_]`, `[QuestImage_]`, `[Wedding_]`, etc., with an `[Other]` catch-all).
- Each entry is tagged with the hook it came through, e.g. `QuestImage_Coral [ImageSprite]`.
- The file persists across sessions and never duplicates a name — play over time and it builds a complete map of everything swappable.

To find a specific name: play until the art appears on screen, then Ctrl+F `seen_textures.txt`. The string there is the exact, correct name to use for your PNG.

Leave the flag **off** for normal play — it adds overhead and is purely a discovery tool.

---

## Asset categories & naming schemes

| Asset type | Pattern | Example |
|---|---|---|
| Dialogue portrait | `Dialog_<Character>_<Expression>` | `Dialog_Jade_Default` |
| Wedding CG | `Wedding_<Character>` | `Wedding_Jade` |
| Artwork CG | `Artwork_<Character>` | `Artwork_Jade` |
| Quest icon | `QuestImage_<Character>` | `QuestImage_Jade` |

Full per-character name lists are in the Texture Reference doc.

---

## Making good replacement PNGs

- **Match the original dimensions** (or aspect ratio) where you can. The framework loads whatever size you give it, but a mismatched ratio can look stretched or offset in its UI frame. Pull the original with AssetStudio to check its size if you want a perfect fit.
- **Keep transparency** where the original had it. Dialogue portraits are typically transparent PNGs over the dialogue box. Note that some characters have both transparent and `_notransparent`/`_NoTransparent` variants — replace whichever the game actually uses in the context you care about.
- PNG only.

---

## Known gotchas

- **`Dynamic_` portraits may not swap.** Player and monster portraits use a `Dynamic_` prefix (e.g. `Dynamic_PlayerPotrait_Finn`) and appear to be composited at runtime rather than loaded as flat images. The standard hooks may not catch them cleanly. Consider these untested/unsupported for now.
- **"It logged a swap but I don't see it."** If a sprite was already assigned to its UI element before the swap fired, it may not update until that element is rebuilt. Re-open the conversation / panel / menu and it should catch on the fresh assignment.
- **A name that won't swap no matter what** is almost always a casing/typo mismatch, or the texture loads through a path the framework doesn't hook. Verify the exact name via diagnostic logging first; if it shows in the log but won't swap, the draw path may be unsupported — report it.

---

## How it works (the short version)

The framework hooks the two main runtime draw paths — `Sprite.Create` (dialogue portraits) and `Image.sprite` assignment (icons and most static UI art). When a texture/sprite passes through with a name matching a PNG in your Portraits folder, it substitutes your image. Textures load via Unity Addressables, but the framework intercepts downstream at the point of use, so you never touch the catalog or repack bundles. Loaded PNGs are cached, so repeated swaps don't re-read from disk.

---

## Building on this / source

The framework is open source — fork it, extend the hooks, or add draw paths it doesn't yet cover. If you find a texture that loads through an unsupported path (it shows in the diagnostic log but won't swap), that's the place to add a hook.

*Custom Portrait Framework — the first modding framework for Ova Magica. Made so the rest of you don't have to fight Unity 6 IL2CPP from scratch.*
