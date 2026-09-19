namespace OsXos.Tools;

/// <summary>
/// The shared half of every tool that finds files and then deletes them. Kept apart
/// from the tools themselves so it can be tested against a temp directory without any
/// of them running for real.
/// </summary>
public static class FileSweep
{
    /// <summary>
    /// Every file in <paramref name="dir"/> matching any of <paramref name="patterns"/>,
    /// newest-largest first, as preview items carrying their size. A missing or
    /// unreadable directory yields nothing rather than throwing: a machine that has
    /// never had an icon cache is not an error.
    /// </summary>
    public static IEnumerable<PreviewItem> Files(string dir, params string[] patterns)
    {
        if (!Directory.Exists(dir)) yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
        {
            string[] matches;
            try
            {
                matches = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var path in matches.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (!seen.Add(path)) continue;
                long size;
                try { size = new FileInfo(path).Length; }
                catch { continue; }
                yield return new PreviewItem(Path.GetFileName(path), path, size);
            }
        }
    }

    /// <summary>
    /// Each immediate subdirectory of <paramref name="dir"/> with its total size, plus
    /// any loose files at the top level. Used by the cache-clearing tools, where a user
    /// wants to see "Google/ 1.2 GB" rather than forty thousand file names.
    /// </summary>
    public static IEnumerable<PreviewItem> Children(string dir)
    {
        if (!Directory.Exists(dir)) yield break;

        string[] subdirs;
        try { subdirs = Directory.GetDirectories(dir); }
        catch { yield break; }

        foreach (var sub in subdirs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            yield return new PreviewItem(Path.GetFileName(sub) + "/", sub, SizeOf(sub));

        string[] files;
        try { files = Directory.GetFiles(dir); }
        catch { yield break; }

        foreach (var f in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            long size;
            try { size = new FileInfo(f).Length; }
            catch { continue; }
            yield return new PreviewItem(Path.GetFileName(f), f, size);
        }
    }

    /// <summary>Recursive byte total, skipping anything that cannot be read.</summary>
    public static long SizeOf(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch { /* vanished or locked mid-walk */ }
            }
        }
        catch
        {
            // An unreadable tree reports what it managed to add up.
        }
        return total;
    }

    /// <summary>
    /// Deletes each previewed path. Never throws: a file held open by another process
    /// is the normal case, not a failure of the tool, so it is counted and reported.
    /// </summary>
    public static SweepOutcome Delete(IEnumerable<PreviewItem> items)
    {
        int deleted = 0, skipped = 0;
        long freed = 0;
        var problems = new List<string>();

        foreach (var item in items)
        {
            var path = item.Detail;
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else if (File.Exists(path)) File.Delete(path);
                else continue;

                deleted++;
                freed += item.Bytes ?? 0;
            }
            catch (Exception ex)
            {
                skipped++;
                if (problems.Count < 5) problems.Add($"{item.Label} — {ex.GetType().Name}");
            }
        }

        return new SweepOutcome(deleted, skipped, freed, problems);
    }

    /// <summary>Human byte sizes, one decimal from MB up. Used everywhere sizes show.</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes < 1024) return $"{bytes} B";

        string[] units = { "KB", "MB", "GB", "TB" };
        double value = bytes / 1024.0;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}

public sealed record SweepOutcome(int Deleted, int Skipped, long Freed, IReadOnlyList<string> Problems);
