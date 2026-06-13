using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace CustomPortraitFramework;

internal static class TextureCache
{
    // Case-sensitive (Ordinal) to match the user's requirement.
    private static readonly Dictionary<string, string> PngPaths = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Texture2D> Loaded = new(StringComparer.Ordinal);

    public static void Initialize(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Could not create portrait folder '{folder}': {ex.Message}");
            return;
        }

        PngPaths.Clear();
        Loaded.Clear();

        foreach (var path in Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            PngPaths[key] = path;
            Plugin.Logger.LogInfo($"  + {key}");
        }

        Plugin.Logger.LogInfo($"Indexed {PngPaths.Count} portrait PNG(s) in {folder}");
    }

    public static bool TryGetReplacement(string key, out Texture2D texture)
    {
        // Cached hit (or cached failure — value is null).
        if (Loaded.TryGetValue(key, out texture))
            return texture != null;

        if (!PngPaths.TryGetValue(key, out var path))
        {
            texture = null;
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2)
            {
                name = key,
                // Prevents Resources.UnloadUnusedAssets() from collecting our texture
                // and stops Unity from trying to serialize it.
                hideFlags = HideFlags.HideAndDontSave,
            };
            // IL2CPP signature is LoadImage(Texture2D, Il2CppStructArray<byte>) —
            // the explicit cast invokes Il2CppInterop's managed-array marshaller.
            // LoadImage resizes the texture to the PNG's actual dimensions.
            ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)bytes);

            Loaded[key] = tex;
            texture = tex;
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Failed to load PNG '{path}': {ex}");
            // Negative-cache so a broken file doesn't get retried every Sprite.Create.
            Loaded[key] = null;
            texture = null;
            return false;
        }
    }
}
