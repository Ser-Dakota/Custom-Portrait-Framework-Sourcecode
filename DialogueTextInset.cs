using System;
using TMPro;
using UnityEngine;

namespace CustomPortraitFramework;

// Config-gated dialogue text inset. Widens the RIGHT margin of the dialogue
// subtitle TMP element so the text wraps earlier and clears the portrait zone on
// the right of the screen.
//
// This deliberately uses NO Harmony patch and NO string-based type lookup:
// AccessTools.TypeByName / a name-targeted patch forces HarmonyX to call
// Assembly.GetTypes() across UnityEngine.CoreModule, which throws
// ReflectionTypeLoadException noise on broken IL2CPP interop types. Instead we
// resolve the live element by walking the known scene hierarchy
//   Canvas > UIDialogueView > Dialogue Panel > Text Panel > SubtitleTextMeshPro
// with GameObject.Find / Transform.Find (scene-graph lookups only — no reflection),
// and reference the interop TMP type directly (TMPro.TMP_Text — note this build's
// Il2CppInterop preserves the original namespace, so it is TMPro, not Il2CppTMPro).
//
// Polled by DialogueInsetWatcher. Idempotent: the natural right margin is captured
// once per element instance, then we always set right = baseline + inset, so
// reapplying (the Dialogue System can reset the margin per line) never compounds.
//
// Diagnostics: logs whether the element was found, the inset being applied, and
// the margin before/after each write (with an immediate read-back), plus a
// throttled warning when the margin reverts — so "can't find it" is clearly
// distinguishable from "found it but it won't stick". Logs are emitted on state
// changes only, never every poll.
internal static class DialogueTextInset
{
    private const string ViewName    = "UIDialogueView";
    private const string ChildPath   = "Dialogue Panel/Text Panel/SubtitleTextMeshPro";
    private const string ElementName = "SubtitleTextMeshPro";

    // Cached element; stays valid across polls until the dialogue view is
    // destroyed (Unity-overloaded null), at which point we re-find on the next
    // active dialogue.
    private static TMP_Text _cached;

    // Baseline natural right margin, tied to the instance it was read from so a
    // freshly created subtitle element re-captures rather than inheriting our value.
    private static int   _baselineId;
    private static float _baselineRight;
    private static bool  _haveBaseline;

    // Diagnostic state so logs fire on transitions, not every poll.
    private static int    _foundLoggedId;   // instance we've logged "found" for
    private static string _lastMiss;        // last resolve-failure reason logged
    private static int    _appliedId;       // instance we last wrote to
    private static int    _revertCount;     // consecutive reverts seen for _appliedId

    internal static void Poll()
    {
        if (!Plugin.DialogueInsetEnabled) return;

        var inset = Plugin.DialogueTextRightInset;
        if (inset == 0f) return;

        try
        {
            var tmp = Resolve(out var miss);
            if (tmp == null)
            {
                // miss == null means "no dialogue open" (idle) — not an error, stay quiet.
                if (miss != null && miss != _lastMiss)
                {
                    Plugin.Logger.LogWarning($"[INSET] not applied: {miss}");
                    _lastMiss = miss;
                }
                if (miss == null) _foundLoggedId = 0;   // dialogue closed — allow re-log next time
                return;
            }

            _lastMiss = null;

            var id = tmp.GetInstanceID();
            if (_foundLoggedId != id)
            {
                Plugin.Logger.LogInfo(
                    $"[INSET] found {ElementName} (id={id}, active={tmp.gameObject.activeInHierarchy}); inset to apply = {inset:0.##}px");
                _foundLoggedId = id;
            }

            if (!tmp.gameObject.activeInHierarchy) return;   // present but hidden
            ApplyTo(tmp, inset);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"DialogueTextInset.Poll: {ex}");
        }
    }

    // Resolve the live SubtitleTextMeshPro by walking the known hierarchy. No type
    // scan anywhere: GameObject.Find / Transform.Find are scene-graph lookups.
    // On failure, sets miss to a human-readable reason (null when simply idle, i.e.
    // no dialogue view in the scene, which is not worth warning about).
    private static TMP_Text Resolve(out string miss)
    {
        miss = null;

        // Fast path: cached component still alive (Unity-overloaded null check).
        if (_cached != null) return _cached;

        // GameObject.Find returns the ACTIVE dialogue view, or null when no dialogue
        // is showing — so this naturally no-ops outside of dialogue.
        var view = GameObject.Find(ViewName);
        if (view == null) return null;   // idle: no dialogue open

        // Transform.Find resolves the relative path (and reaches inactive children);
        // fall back to a scoped recursive name search bounded to the dialogue subtree.
        var t = view.transform.Find(ChildPath);
        if (t == null) t = FindByNameUnder(view.transform, ElementName);
        if (t == null)
        {
            miss = $"{ViewName} present but {ElementName} child not found (path '{ChildPath}' + name search both missed)";
            return null;
        }

        var tmp = t.GetComponent<TMP_Text>();
        if (tmp == null)
        {
            miss = $"{ElementName} found but it has no TMP_Text component";
            return null;
        }

        _cached = tmp;
        return _cached;
    }

    private static Transform FindByNameUnder(Transform root, string name)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (string.Equals(child.gameObject.name, name, StringComparison.Ordinal))
                return child;

            var hit = FindByNameUnder(child, name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static void ApplyTo(TMP_Text tmp, float inset)
    {
        var id = tmp.GetInstanceID();
        var before = tmp.margin;   // (left, top, right, bottom)

        // Capture the natural right margin once per element instance. The
        // instance-id guard stops us from ever re-reading our OWN applied value as
        // the baseline (which would compound), while still re-capturing when the
        // dialogue is torn down and a new element is created.
        if (!_haveBaseline || _baselineId != id)
        {
            _baselineRight = before.z;
            _baselineId = id;
            _haveBaseline = true;
        }

        var target = _baselineRight + inset;

        if (Mathf.Approximately(before.z, target))
        {
            // Already correct. Confirm "stuck" once (after a fresh apply or a revert
            // streak), then stay silent so a stable margin doesn't spam the log.
            if (_appliedId == id && _revertCount != 0)
            {
                Plugin.Logger.LogInfo($"[INSET] margin now stable at right={target:0.##}");
                _revertCount = 0;
            }
            _appliedId = id;
            return;
        }

        // We are about to write. If we already applied to this instance, the margin
        // came back on its own — the game reset it. That's the "found it but not
        // sticking" case; warn (throttled) rather than spam every poll.
        bool revert = _appliedId == id;
        if (revert)
        {
            _revertCount++;
            if (_revertCount == 1 || _revertCount % 8 == 0)
                Plugin.Logger.LogWarning(
                    $"[INSET] margin did NOT stick — reverted to right={before.z:0.##} (revert #{_revertCount}); reapplying");
        }

        // Only move the right boundary; leave left/top/bottom as the game has them.
        tmp.margin = new Vector4(before.x, before.y, target, before.w);
        var after = tmp.margin;   // read back immediately to confirm the write took

        // Full before/after detail on the first write to an instance (and the first
        // revert), so the log shows exactly what changed without repeating forever.
        if (!revert || _revertCount == 1)
            Plugin.Logger.LogInfo(
                $"[INSET] set right margin: inset={inset:0.##} baseline={_baselineRight:0.##} target={target:0.##} | " +
                $"before={V4(before)} after={V4(after)}");

        // If the read-back doesn't match, TMP rejected/clamped the assignment itself
        // (distinct from the game resetting it later).
        if (!Mathf.Approximately(after.z, target))
            Plugin.Logger.LogWarning(
                $"[INSET] write not accepted by TMP — after right={after.z:0.##}, expected {target:0.##}");

        _appliedId = id;
        if (!revert) _revertCount = 0;
    }

    private static string V4(Vector4 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##}, {v.w:0.##})";
}
