using System;
using UnityEngine;

namespace CustomPortraitFramework.Diagnostics;

// Injected MonoBehaviour that polls the configured hotkey each frame and fires a
// one-shot UI hierarchy + text-layout dump. It is only registered and attached
// when EnableDiagnosticLogging is true (see Plugin.Load), so it never exists
// during normal play — no per-frame Input cost is paid when diagnostics are off.
internal sealed class DiagnosticHotkey : MonoBehaviour
{
    // IL2CPP injection requires the IntPtr constructor; ClassInjector/AddComponent
    // hands the native pointer through here.
    public DiagnosticHotkey(IntPtr ptr) : base(ptr) { }

    // Set from Plugin.Load before the component is added. Static because the
    // injected instance is constructed by Unity, not by us, so we can't pass
    // ctor args through AddComponent.
    internal static KeyCode Key = KeyCode.F8;

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(Key))
                UiHierarchyDump.Dump();
        }
        catch (Exception ex)
        {
            // Never let a diagnostic throw out into the game's update loop.
            Plugin.Logger.LogError($"DiagnosticHotkey.Update: {ex}");
        }
    }
}
