using System;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CustomPortraitFramework.Diagnostics;

// Config-gated UI snapshot. Triggered by DiagnosticHotkey, it walks every active
// root Canvas and recurses the full transform tree, recording for each
// GameObject its name, component types and RectTransform layout — plus extra
// text-layout metrics for any TMP_Text (TextMeshProUGUI) or UnityEngine.UI.Text.
//
// Purpose: pin down which object is the dialogue box container and which is its
// text element, and gather enough layout data (rect height vs preferred/text
// bounds height, anchors, wrap/overflow modes, margins) to judge whether the
// safer fix is re-anchoring the box or shrinking the text width.
//
// Output is APPENDED to ui_layout_dump.txt next to the DLL — one timestamped
// block per press — so snapshots of different dialogue states stack up for
// side-by-side comparison. The walk is read-only aside from a ForceMeshUpdate()
// on each text element so its current text bounds can be read.
internal static class UiHierarchyDump
{
    private static string _filePath;
    private const int MaxTextPreview = 80;

    internal static void Configure(string filePath) => _filePath = filePath;

    internal static void Dump()
    {
        if (_filePath == null)
        {
            Plugin.Logger.LogWarning("UiHierarchyDump.Dump called before Configure — skipping.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("======================================================================");
        sb.AppendLine($"UI DUMP @ {DateTime.Now:yyyy-MM-dd HH:mm:ss}  frame {Time.frameCount}");
        sb.AppendLine("======================================================================");

        int canvasCount = 0, goCount = 0;
        try
        {
            // FindObjectsOfTypeAll catches DontDestroyOnLoad canvases too (persistent
            // HUD/dialogue layers), and includes inactive ones — we filter those out.
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null) continue;
                // Nested canvases are reached by recursing from their root, so only
                // enter at root canvases to avoid dumping a subtree twice.
                if (!canvas.isRootCanvas) continue;

                var go = canvas.gameObject;
                if (go == null || !go.activeInHierarchy) continue;

                canvasCount++;
                sb.AppendLine();
                sb.AppendLine($"### CANVAS ROOT: {go.name}  (renderMode={canvas.renderMode}, sortingOrder={canvas.sortingOrder})");
                Walk(go.transform, 1, sb, ref goCount);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[dump error] {ex}");
        }

        sb.AppendLine();
        sb.AppendLine($"--- end dump: {canvasCount} active root canvas(es), {goCount} GameObject(s) ---");
        sb.AppendLine();

        try
        {
            File.AppendAllText(_filePath, sb.ToString());
            Plugin.Logger.LogInfo(
                $"[UI DUMP] wrote {goCount} GameObject(s) across {canvasCount} canvas(es) -> {Path.GetFileName(_filePath)}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"UiHierarchyDump: failed to write {_filePath}: {ex}");
        }
    }

    private static void Walk(Transform t, int depth, StringBuilder sb, ref int goCount)
    {
        if (t == null) return;
        var go = t.gameObject;
        if (go == null) return;
        goCount++;

        var indent = new string(' ', depth * 2);

        sb.Append(indent).Append("- ").Append(go.name);
        if (!go.activeSelf) sb.Append("  (inactive self)");
        sb.AppendLine();

        sb.Append(indent).Append("    components: ").AppendLine(ComponentTypes(go));

        var rt = t.TryCast<RectTransform>();
        if (rt != null)
        {
            var rect = rt.rect;
            sb.Append(indent).Append("    rect: ")
              .Append($"anchoredPos={V2(rt.anchoredPosition)} sizeDelta={V2(rt.sizeDelta)} ")
              .Append($"anchorMin={V2(rt.anchorMin)} anchorMax={V2(rt.anchorMax)} pivot={V2(rt.pivot)} ")
              .AppendLine($"size=({F(rect.width)}x{F(rect.height)})");
        }

        // Text-layout extras. GetComponents<Component> yields every component on
        // the object; we pick out the text ones. TMP_Text is the base of
        // TextMeshProUGUI, so a single cast catches the dialogue text element.
        foreach (var comp in go.GetComponents<Component>())
        {
            if (comp == null) continue;

            var tmp = comp.TryCast<TMP_Text>();
            if (tmp != null) { AppendTmp(sb, indent, tmp); continue; }

            var uiText = comp.TryCast<Text>();
            if (uiText != null) AppendUiText(sb, indent, uiText);
        }

        for (int i = 0; i < t.childCount; i++)
            Walk(t.GetChild(i), depth + 1, sb, ref goCount);
    }

    private static string ComponentTypes(GameObject go)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var c in go.GetComponents<Component>())
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(TypeName(c));
        }
        return first ? "<none>" : sb.ToString();
    }

    private static string TypeName(Component c)
    {
        // A null element here means a missing/destroyed script reference.
        if (c == null) return "<missing>";
        try { return c.GetType().Name; }
        catch { return "<unknown>"; }
    }

    private static void AppendTmp(StringBuilder sb, string indent, TMP_Text t)
    {
        try
        {
            var rt = t.rectTransform;
            float rectH = rt != null ? rt.rect.height : float.NaN;

            // preferredHeight is the layout-driven ask; textBounds is the actual
            // generated mesh extent after a forced update. Both are best-effort.
            float prefH = float.NaN, boundsH = float.NaN;
            try
            {
                prefH = t.preferredHeight;
                t.ForceMeshUpdate();
                boundsH = t.textBounds.size.y;
            }
            catch { /* leave as NaN */ }

            sb.Append(indent).Append("    [TMP ").Append(t.GetType().Name).Append("] ");
            sb.Append($"fontSize={F(t.fontSize)} autoSize={t.enableAutoSizing} ");
            if (t.enableAutoSizing)
                sb.Append($"fontSizeMin={F(t.fontSizeMin)} fontSizeMax={F(t.fontSizeMax)} ");
            sb.Append($"overflow={t.overflowMode} ");
#pragma warning disable CS0612, CS0618 // enableWordWrapping is deprecated in favour of textWrappingMode; log both
            sb.Append($"wordWrap={t.enableWordWrapping} ");
#pragma warning restore CS0612, CS0618
            sb.Append($"wrapMode={t.textWrappingMode} ");
            sb.Append($"margin={V4(t.margin)} ");
            sb.AppendLine($"rectH={F(rectH)} preferredH={F(prefH)} textBoundsH={F(boundsH)}");

            AppendTextPreview(sb, indent, t.text);
        }
        catch (Exception ex)
        {
            sb.Append(indent).AppendLine($"    [TMP read error] {ex.Message}");
        }
    }

    private static void AppendUiText(StringBuilder sb, string indent, Text t)
    {
        try
        {
            var rt = t.rectTransform;
            float rectH = rt != null ? rt.rect.height : float.NaN;

            // UI.Text has no margin/word-wrap bool/autosizing — wrapping is encoded
            // in horizontalOverflow, and best-fit autosizing in resizeTextForBestFit.
            float prefH = float.NaN;
            try { prefH = t.preferredHeight; } catch { /* leave as NaN */ }

            sb.Append(indent).Append("    [UI.Text] ");
            sb.Append($"fontSize={t.fontSize} bestFit={t.resizeTextForBestFit} ");
            if (t.resizeTextForBestFit)
                sb.Append($"minSize={t.resizeTextMinSize} maxSize={t.resizeTextMaxSize} ");
            sb.Append($"hOverflow={t.horizontalOverflow} vOverflow={t.verticalOverflow} ");
            sb.AppendLine($"rectH={F(rectH)} preferredH={F(prefH)}");

            AppendTextPreview(sb, indent, t.text);
        }
        catch (Exception ex)
        {
            sb.Append(indent).AppendLine($"    [UI.Text read error] {ex.Message}");
        }
    }

    private static void AppendTextPreview(StringBuilder sb, string indent, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var preview = text.Length > MaxTextPreview ? text.Substring(0, MaxTextPreview) + "…" : text;
        sb.Append(indent).Append("      text: \"").Append(preview.Replace("\n", "\\n")).AppendLine("\"");
    }

    private static string F(float v) =>
        float.IsNaN(v) ? "n/a" : v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string V2(Vector2 v) =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##})", v.x, v.y);

    private static string V4(Vector4 v) =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##},{3:0.##})", v.x, v.y, v.z, v.w);
}
