using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using CustomPortraitFramework.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace CustomPortraitFramework;

// Marks a patch class as diagnostic-only — registered solely when
// EnableDiagnosticLogging is true. The swapper patches are NOT marked, so they
// always register.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class DiagnosticOnlyAttribute : Attribute { }

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BasePlugin
{
    public const string PluginGuid    = "com.dakot.ovamagica.custom-portrait-framework";
    public const string PluginName    = "Custom Portrait Framework";
    public const string PluginVersion = "0.6.0";

    internal static ManualLogSource Logger = null!;

    // When false (default), all name-logging/catalog hooks are silenced and the
    // diagnostic-only Harmony patches are never registered. The swappers and
    // their [SWAP] logs run regardless — they are the product.
    internal static bool DiagnosticLogging;

    // Dialogue text inset: when enabled, widens the subtitle TMP element's right
    // margin by DialogueTextRightInset px so text wraps clear of the portrait.
    // Independent of the swapper. Default off.
    internal static bool  DialogueInsetEnabled;
    internal static float DialogueTextRightInset;

    public override void Load()
    {
        Logger = Log;
        Logger.LogInfo($"{PluginName} {PluginVersion} loading…");

        DiagnosticLogging = Config.Bind(
            "Diagnostics",
            "EnableDiagnosticLogging",
            false,
            "Logs every unique texture/sprite name to the console and to seen_textures.txt to help " +
            "discover asset names while developing portrait packs, plus the [SWAP] confirmation lines. " +
            "The portrait swapper itself ALWAYS runs regardless of this setting. Leave false for normal play.").Value;

        Logger.LogInfo($"EnableDiagnosticLogging = {DiagnosticLogging}");

        DialogueInsetEnabled = Config.Bind(
            "Dialogue",
            "EnableDialogueTextInset",
            true,
            "When true, widens the dialogue subtitle text's right margin so it wraps earlier and " +
            "clears the portrait zone on the right of the screen. Reapplied on every dialogue line. " +
            "Independent of the portrait swap. Default on.").Value;

        DialogueTextRightInset = Config.Bind(
            "Dialogue",
            "DialogueTextRightInset",
            200f,
            "Pixels added to the dialogue subtitle text's RIGHT margin when EnableDialogueTextInset " +
            "is true. Larger = text wraps further left. The portrait's left edge sits near screen " +
            "x≈1170 and the text otherwise reaches ~1445; 200 clears it (tested).").Value;

        Logger.LogInfo($"EnableDialogueTextInset = {DialogueInsetEnabled} (inset {DialogueTextRightInset}px)");

        // Bound regardless of the gate so the key is visible/editable in the config
        // file even when diagnostics are off; only wired up when the gate is true.
        var dumpHotkey = Config.Bind(
            "Diagnostics",
            "UiDumpHotkey",
            KeyCode.F8,
            "Key that triggers a one-shot UI hierarchy + text-layout dump to ui_layout_dump.txt " +
            "(appended, one block per press). Walks every active root Canvas and logs each " +
            "GameObject's components and RectTransform, plus font/overflow/wrap/margin and " +
            "rect-vs-preferred height for TextMeshPro and UI.Text elements. Only active when " +
            "EnableDiagnosticLogging is true.").Value;

        var dllDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);

        try
        {
            TextureCache.Initialize(Path.Combine(dllDir, "Portraits"));
        }
        catch (Exception ex)
        {
            Logger.LogError($"TextureCache.Initialize threw: {ex}");
        }

        // Only touch the master catalog file when diagnostics are on — when off,
        // nothing is read, created, or written.
        if (DiagnosticLogging)
        {
            try
            {
                SeenCatalog.Load(Path.Combine(dllDir, "seen_textures.txt"));
            }
            catch (Exception ex)
            {
                Logger.LogError($"SeenCatalog.Load threw: {ex}");
            }

            // Arm the hotkey-driven UI dump. The injected MonoBehaviour only exists
            // while diagnostics are on, so normal play pays no per-frame Input cost.
            try
            {
                DiagnosticHotkey.Key = dumpHotkey;
                UiHierarchyDump.Configure(Path.Combine(dllDir, "ui_layout_dump.txt"));
                AddComponent<DiagnosticHotkey>();
                Logger.LogInfo($"UI dump hotkey armed: press {dumpHotkey} in-game to capture the dialogue UI.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to arm UI dump hotkey: {ex}");
            }
        }

        // Dialogue text inset runs via a light scene-graph poll (no Harmony patch,
        // no type-by-name lookup), so it needs its own injected watcher. Added only
        // when enabled — feature off means no component and no per-frame work.
        if (DialogueInsetEnabled)
        {
            try
            {
                AddComponent<DialogueInsetWatcher>();
                Logger.LogInfo($"Dialogue text inset active: +{DialogueTextRightInset}px right margin (polled).");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to start dialogue inset watcher: {ex}");
            }
        }

        ApplyPatches();
    }

    private static void ApplyPatches()
    {
        var harmony = new Harmony(PluginGuid);
        int ok = 0, fail = 0;

        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            // Only types with [HarmonyPatch] on them — leave everything else alone.
            if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0)
                continue;

            // Diagnostic-only patches don't register unless logging is enabled —
            // skipping registration (not just gating the body) avoids the overhead
            // of hooks like Object.name that fire constantly.
            if (!DiagnosticLogging &&
                type.GetCustomAttributes(typeof(DiagnosticOnlyAttribute), inherit: false).Length > 0)
            {
                Logger.LogInfo($"  skipped : {type.FullName} (diagnostics disabled)");
                continue;
            }

            try
            {
                harmony.CreateClassProcessor(type).Patch();
                Logger.LogInfo($"  patched : {type.FullName}");
                ok++;
            }
            catch (Exception ex)
            {
                Logger.LogError($"  FAILED  : {type.FullName} — {ex.Message}");
                fail++;
            }
        }

        Logger.LogInfo($"{PluginName}: {ok} patch class(es) applied, {fail} failed.");
    }
}
