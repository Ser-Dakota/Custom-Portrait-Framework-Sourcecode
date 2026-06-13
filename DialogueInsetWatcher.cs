using System;
using UnityEngine;

namespace CustomPortraitFramework;

// Drives the dialogue text inset on a light poll instead of a Harmony patch — a
// name-targeted patch / AccessTools.TypeByName would force a broad HarmonyX
// assembly type scan that throws ReflectionTypeLoadException noise on broken
// IL2CPP interop types. A few times per second this walks the known dialogue
// hierarchy and, when the view is active, reapplies the right margin (so it also
// covers the view becoming active and the game resetting the margin per line).
//
// Only added when EnableDialogueTextInset is true, so with the feature off no
// component exists and no per-frame work happens.
internal sealed class DialogueInsetWatcher : MonoBehaviour
{
    // IL2CPP injection requires the IntPtr constructor.
    public DialogueInsetWatcher(IntPtr ptr) : base(ptr) { }

    // ~4 checks/second, on unscaled time so it keeps working while dialogue pauses
    // game time.
    private const float IntervalSeconds = 0.25f;
    private float _accum;

    private void Update()
    {
        try
        {
            _accum += Time.unscaledDeltaTime;
            if (_accum < IntervalSeconds) return;
            _accum = 0f;

            DialogueTextInset.Poll();
        }
        catch (Exception ex)
        {
            // Never let the watcher throw into Unity's update loop.
            Plugin.Logger.LogError($"DialogueInsetWatcher.Update: {ex}");
        }
    }
}
