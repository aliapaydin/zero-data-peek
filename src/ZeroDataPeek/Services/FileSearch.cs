using System.Text.RegularExpressions;
using ZeroDataPeek.Models;

namespace ZeroDataPeek.Services;

/// <summary>Dosyalarda arama seçenekleri.</summary>
public sealed record SearchOptions(string Folder, string Query, bool MatchCase, bool WholeWord, bool UseRegex, string Include, string Exclude);

/// <summary>Aramanın özeti: taranan dosya, eşleşen dosya ve eşleşme sayıları; sınıra takılıp takılmadığı.</summary>
public sealed record SearchSummary(int FilesScanned, int FilesMatched, int Matches, bool Truncated);

/// <summary>
/// Bir klasördeki metin dosyalarında arama. Arka planda çalışır, sonuçları dosya dosya bildirir.
/// İkili dosyalar, çok büyük dosyalar ve derleme / bağımlılık klasörleri (.git, node_modules, bin, obj…) atlanır.
/// </summary>
public static class FileSearch
{
    public const int MaxMatches = 5000;
    private const int MaxMatchesPerFile = 500;
    private const long MaxFileBytes = 8 * 1024 * 1024;
    private const int MaxLineShown = 260;

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea", "node_modules", "bin", "obj", "__pycache__", ".venv", "venv", ".mypy_cache", ".pytest_cache", "packages",
    };

    /// <summary>Arama desenini kurar. Düzenli ifade geçersizse ArgumentException fırlatır.</summary>
    public static Regex BuildPattern(SearchOptions options)
    {
        string pattern = options.UseRegex ? options.Query : Regex.Escape(options.Query);
        if (options.WholeWord) pattern = @"(?<!\w)(?:" + pattern + @")(?!\w)";
        var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(pattern, flags, TimeSpan.FromSeconds(2));
    }

    /// <summary>"*.sql; *.py" gibi bir listeyi dosya adı süzgecine çevirir. Boş liste null döner (her dosya).</summary>
    public static Regex? BuildGlob(string list)
    {
        var parts = list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var patterns = parts.Select(p =>
        {
            if (!p.Contains('*') && !p.Contains('?')) p = p.StartsWith('.') ? "*" + p : "*" + p + "*"; // ".sql" → "*.sql", "rapor" → "*rapor*"
            return "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        });
        return new Regex(string.Join("|", patterns), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Aramayı çalıştırır. <paramref name="onFile"/> her eşleşen dosya için arka plan iş parçacığından çağrılır:
    /// ilk öğe dosya başlığı, sonrakiler eşleşen satırlardır.
    /// </summary>
    public static SearchSummary Run(SearchOptions options, Action<IReadOnlyList<SearchHit>> onFile, CancellationToken cancel)
    {
        var pattern = BuildPattern(options);
        var include = BuildGlob(options.Include);
        var exclude = BuildGlob(options.Exclude);
        int scanned = 0, matchedFiles = 0, matches = 0;
        bool truncated = false;

        var pending = new Stack<string>();
        pending.Push(options.Folder);
        while (pending.Count > 0 && !truncated)
        {
            cancel.ThrowIfCancellationRequested();
            string folder = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(folder).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue; // okunamayan klasör atlanır
            }

            var folders = new List<string>();
            foreach (var entry in entries)
            {
                cancel.ThrowIfCancellationRequested();
                if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (entry is DirectoryInfo)
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0 && !SkippedFolders.Contains(entry.Name)) folders.Add(entry.FullName);
                    continue;
                }
                if (entry is not FileInfo file) continue;
                if (include is not null && !include.IsMatch(file.Name)) continue;
                if (exclude is not null && exclude.IsMatch(file.Name)) continue;
                if (file.Length == 0 || file.Length > MaxFileBytes || FileKinds.IsBinaryFormat(file.FullName)) continue;

                LoadedText? loaded;
                try
                {
                    loaded = TextFileIO.Load(file.FullName);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    continue;
                }
                if (loaded is null) continue; // ikili içerik
                scanned++;

                var hits = SearchText(file.FullName, options.Folder, loaded.Text, pattern, MaxMatches - matches, cancel);
                if (hits.Count == 0) continue;
                matchedFiles++;
                matches += hits.Count - 1;
                onFile(hits);
                if (matches >= MaxMatches)
                {
                    truncated = true;
                    break;
                }
            }
            for (int i = folders.Count - 1; i >= 0; i--) pending.Push(folders[i]); // alfabetik sırayla gezilsin
        }
        return new SearchSummary(scanned, matchedFiles, matches, truncated);
    }

    /// <summary>Tek bir metinde arar. Sonuç boş değilse ilk öğe dosya başlığıdır.</summary>
    public static List<SearchHit> SearchText(string path, string root, string text, Regex pattern, int budget, CancellationToken cancel)
    {
        var hits = new List<SearchHit>();
        int limit = Math.Min(budget, MaxMatchesPerFile);
        int lineNumber = 0, position = 0;
        try
        {
            while (position <= text.Length && hits.Count < limit)
            {
                int end = text.IndexOf('\n', position);
                if (end < 0) end = text.Length;
                lineNumber++;
                int length = end - position;
                if (length > 0 && text[end - 1] == '\r') length--;
                if (length > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    string line = text.Substring(position, length);
                    foreach (Match match in pattern.Matches(line))
                    {
                        if (match.Length == 0) continue;
                        hits.Add(MakeHit(path, lineNumber, line, match.Index, match.Length));
                        if (hits.Count >= limit) break;
                    }
                }
                if (end >= text.Length) break;
                position = end + 1;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // desen bu dosyada çok yavaş: o ana kadar bulunanlarla yetin
        }
        if (hits.Count == 0) return hits;

        string relative;
        try
        {
            relative = Path.GetRelativePath(root, path);
        }
        catch (ArgumentException)
        {
            relative = path;
        }
        hits.Insert(0, SearchHit.ForFile(path, relative, hits.Count));
        return hits;
    }

    private static SearchHit MakeHit(string path, int lineNumber, string line, int index, int length)
    {
        // Uzun satırlarda eşleşmenin çevresi gösterilir; baştaki girinti atılır.
        int start = 0;
        while (start < index && char.IsWhiteSpace(line[start])) start++;
        if (index - start > 60) start = index - 60;
        int stop = Math.Min(line.Length, Math.Max(index + length + 80, start + MaxLineShown));
        if (stop - start > MaxLineShown) stop = Math.Max(index + Math.Min(length, MaxLineShown), start + MaxLineShown);
        stop = Math.Min(line.Length, stop);
        int matchEnd = Math.Min(index + length, stop);

        string before = (start > 0 && start == index - 60 ? "…" : string.Empty) + line[start..index];
        string match = line[index..matchEnd];
        string after = line[matchEnd..stop] + (stop < line.Length ? "…" : string.Empty);
        return SearchHit.ForMatch(path, lineNumber, index + 1, length, before.Replace('\t', ' '), match.Replace('\t', ' '), after.Replace('\t', ' '));
    }
}
