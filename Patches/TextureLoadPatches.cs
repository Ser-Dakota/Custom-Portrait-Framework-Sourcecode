using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CustomPortraitFramework.Patches;

// Shared census of asset names already logged this session, across every
// diagnostic hook below. A name prints once TOTAL, tagged with whichever hook
// saw it first — so the tag tells you which draw path surfaced the asset.
internal static class SeenLog
{
    private static readonly HashSet<string> Seen = new();

    internal static void Note(string name, string hook)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (Seen.Add(name))
            Plugin.Logger.LogInfo($"[SEEN via {hook}] {name}");
    }
}

// Builds replacement Sprites from the cached PNG textures and caches them, so
// each portrait sprite is constructed only once. Used by the Image.sprite
// swapper below.
internal static class SpriteCache
{
    // Ordinal to match TextureCache's case-sensitive keying.
    private static readonly Dictionary<string, Sprite> Built = new(StringComparer.Ordinal);

    // True while we construct a replacement sprite. SpriteCreatePatch checks
    // this so OUR Sprite.Create call doesn't redundantly re-swap (and log) the
    // texture we already swapped in — the game's own Sprite.Create path is
    // unaffected because the flag is only set during our build.
    internal static bool Building;

    internal static Sprite Get(string key)
    {
        // Cached hit (or cached failure — value is null).
        if (Built.TryGetValue(key, out var cached))
            return cached;

        if (!TextureCache.TryGetReplacement(key, out var tex) || tex == null)
        {
            Built[key] = null;
            return null;
        }

        Building = true;
        try
        {
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            sprite.name = key;
            // Stops Unity from collecting/serializing our runtime sprite.
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Built[key] = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Failed to build replacement sprite '{key}': {ex}");
            Built[key] = null;   // negative-cache so a bad key isn't retried every setter call
            return null;
        }
        finally
        {
            Building = false;
        }
    }
}

// Object.name setter — catches every Texture/Sprite that gets a name assigned.
// Diagnostic-only: Object.name fires constantly, so when logging is off this
// patch is never registered at all.
[DiagnosticOnly]
[HarmonyPatch(typeof(Object), nameof(Object.name), MethodType.Setter)]
internal static class ObjectNameSetterPatch
{
    [HarmonyPostfix]
    private static void Postfix(Object __instance, string value)
    {
        if (__instance == null) return;

        // TryCast<T>() is the IL2CPP-safe equivalent of `as T` — returns null on miss.
        if (__instance.TryCast<Texture>() is null && __instance.TryCast<Sprite>() is null)
            return;

        SeenLog.Note(value, "NameSetter");        // ephemeral console census
        SeenCatalog.Record(value, "NameSetter");  // persistent master file
    }
}

// Swap the source texture before Sprite.Create runs if we have a PNG keyed
// by the source texture's name. Note: ref Texture2D — Harmony rewrites the
// argument the original method sees.
[HarmonyPatch(typeof(Sprite), nameof(Sprite.Create),
    new[] { typeof(Texture2D), typeof(Rect), typeof(Vector2) })]
internal static class SpriteCreatePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref Texture2D texture)
    {
        // Skip our own replacement-sprite construction — the texture is already ours.
        if (SpriteCache.Building) return;
        if (texture == null) return;
        var key = texture.name;
        if (string.IsNullOrEmpty(key)) return;

        // Catalog the original game name before any swap — dialogue portraits
        // come through here, so this is the core discovery category. Gated:
        // diagnostics only. The swap and [SWAP] log below always run.
        if (Plugin.DiagnosticLogging)
            SeenCatalog.Record(key, "Sprite.Create");

        if (TextureCache.TryGetReplacement(key, out var replacement))
        {
            Plugin.Logger.LogInfo($"[SWAP] {key} -> custom PNG");
            texture = replacement;
        }
    }
}

// AssetBundle load — internal entry point for every AssetBundle.LoadAsset overload.
// Diagnostic-only: pure logging, so it stays unregistered when logging is off.
[DiagnosticOnly]
[HarmonyPatch(typeof(AssetBundle), "LoadAsset_Internal")]
internal static class AssetBundleLoadPatch
{
    [HarmonyPostfix]
    private static void Postfix(string name, Object __result)
    {
        if (__result == null) return;
        if (__result.TryCast<Texture>() is null && __result.TryCast<Sprite>() is null) return;

        SeenLog.Note(name, "BundleLoad");
        SeenCatalog.Record(name, "BundleLoad");
    }
}

// UI.Image.sprite setter — second swap path. A property setter, so it dodges
// the overload marshalling that made the Resources.Load hooks crash. Census-logs
// the incoming sprite (pre-swap), then substitutes our PNG-backed sprite when
// the incoming sprite's name (or its texture's name) keys a Portraits PNG.
[HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
internal static class ImageSpriteSetterPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref Sprite value)
    {
        if (value == null) return;

        // Census of what the game is trying to assign, logged before any swap.
        // Gated: diagnostics only. The swap and [SWAP via ImageSprite] log below
        // always run.
        if (Plugin.DiagnosticLogging)
        {
            SeenLog.Note(value.name, "ImageSprite");
            SeenCatalog.Record(value.name, "ImageSprite");
        }

        var key = ResolveKey(value);
        if (key == null) return;

        var replacement = SpriteCache.Get(key);
        if (replacement == null) return;

        Plugin.Logger.LogInfo($"[SWAP via ImageSprite] {key}");
        value = replacement;
    }

    // Match on the sprite's own name first, then its backing texture's name.
    // TextureCache.TryGetReplacement returns false without loading when the key
    // isn't a known PNG, so non-matches stay cheap.
    private static string ResolveKey(Sprite sprite)
    {
        var name = sprite.name;
        if (!string.IsNullOrEmpty(name) && TextureCache.TryGetReplacement(name, out _))
            return name;

        var tex = sprite.texture;
        var texName = tex != null ? tex.name : null;
        if (!string.IsNullOrEmpty(texName) && TextureCache.TryGetReplacement(texName, out _))
            return texName;

        return null;
    }
}

// UI.RawImage.texture setter — diagnostic census only (no swap requested here).
[DiagnosticOnly]
[HarmonyPatch(typeof(RawImage), nameof(RawImage.texture), MethodType.Setter)]
internal static class RawImageTextureSetterPatch
{
    [HarmonyPostfix]
    private static void Postfix(Texture value)
    {
        if (value == null) return;
        SeenLog.Note(value.name, "RawImageTexture");
        SeenCatalog.Record(value.name, "RawImageTexture");
    }
}
