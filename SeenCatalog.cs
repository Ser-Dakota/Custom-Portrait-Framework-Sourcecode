using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CustomPortraitFramework;

// Persistent, cross-session catalog of every unique texture/sprite name the
// watched hooks encounter. Seeded from seen_textures.txt on Load, then appended
// to — one name, one line, ever — as new names appear. Entries are grouped under
// [Prefix] section headers; anything unmatched lands in [Other], so no naming
// scheme is silently dropped.
//
// The file is rewritten in full (atomically, via temp + move) on each new entry
// because new names must slot under their section header rather than the end of
// the file. New uniques are rare once the set is seeded, so the cost is trivial
// and every write leaves a complete, flushed catalog — a crash can't shear it.
internal static class SeenCatalog
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<string>> Sections = new(StringComparer.Ordinal);

    private static string _filePath;

    // First matching prefix wins; Label is the section header text. The order
    // here is the order sections are written to the file. [Other] is appended
    // last by Flush().
    private static readonly (string Prefix, string Label)[] PrefixMap =
    {
        ("Dialog_",     "Dialog_"),
        ("QuestImage_", "QuestImage_"),
        ("Wedding_",    "Wedding_"),
        ("Artwork",     "Artwork"),
        ("Tex_Item_",   "Tex_Item_"),
        ("Dynamic_",    "Dynamic_"),
    };
    private const string OtherLabel = "Other";

    // Read the existing catalog (if any) and seed the in-memory set so names
    // never duplicate across sessions. Creates the file when missing.
    internal static void Load(string filePath)
    {
        lock (Gate)
        {
            _filePath = filePath;
            try
            {
                if (File.Exists(filePath))
                {
                    foreach (var raw in File.ReadAllLines(filePath))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0) continue;
                        if (line.StartsWith("[")) continue;   // section header — re-derived on rewrite

                        var name = ParseName(line);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (Seen.Add(name))
                            Bucket(name).Add(line);           // keep the original tag text verbatim
                    }
                    Plugin.Logger.LogInfo($"SeenCatalog: seeded {Seen.Count} name(s) from {Path.GetFileName(filePath)}");
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllText(filePath, string.Empty);
                    Plugin.Logger.LogInfo($"SeenCatalog: created {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"SeenCatalog.Load failed: {ex}");
            }
        }
    }

    // Record a name under the hook that surfaced it. No-op if the name is
    // already catalogued (this session or a prior one).
    internal static void Record(string name, string hook)
    {
        if (string.IsNullOrEmpty(name)) return;
        lock (Gate)
        {
            if (!Seen.Add(name)) return;                 // one name, one line, ever
            Bucket(name).Add($"{name} [{hook}]");
            if (_filePath != null) Flush();              // persist immediately so a crash keeps the catalog
        }
    }

    // --- helpers; all callers hold Gate ---

    private static List<string> Bucket(string name)
    {
        var label = SectionFor(name);
        if (!Sections.TryGetValue(label, out var list))
        {
            list = new List<string>();
            Sections[label] = list;
        }
        return list;
    }

    private static string SectionFor(string name)
    {
        foreach (var (prefix, label) in PrefixMap)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return label;
        return OtherLabel;
    }

    private static string ParseName(string line)
    {
        // Entry format is "name [hook]"; strip the trailing tag if present.
        var idx = line.LastIndexOf(" [", StringComparison.Ordinal);
        if (idx >= 0 && line.EndsWith("]"))
            return line.Substring(0, idx);
        return line;
    }

    private static void Flush()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var (_, label) in PrefixMap)   // known sections, declared order...
                AppendSection(sb, label);
            AppendSection(sb, OtherLabel);          // ...then the catch-all

            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, _filePath, overwrite: true);   // atomic replace on the same volume
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"SeenCatalog.Flush failed: {ex}");
        }
    }

    private static void AppendSection(StringBuilder sb, string label)
    {
        if (!Sections.TryGetValue(label, out var list) || list.Count == 0) return;
        sb.AppendLine($"[{label}]");
        foreach (var entry in list)
            sb.AppendLine(entry);
        sb.AppendLine();
    }
}
