#if TOOLS
using Godot;
using Godot.Collections;
using System;
using System.Text.RegularExpressions;
using FileAccess = Godot.FileAccess;
using DirAccess = Godot.DirAccess;
using Error = Godot.Error;

namespace GodotMCP.Handlers;

public class ProjectHandler : BaseHandler
{
    public ProjectHandler(EditorPlugin plugin) : base(plugin) { }

    public override Dictionary Handle(string command, Dictionary parms)
    {
        return command switch
        {
            "get_settings" => GetSettings(parms),
            "list_files" => ListFiles(parms),
            "read_file" => ReadFile(parms),
            "write_file" => WriteFile(parms),
            "search_text" => SearchText(parms),
            "get_dependencies" => GetDependencies(parms),
            "find_unused_assets" => FindUnusedAssets(parms),
            _ => Error($"Unknown project command: {command}")
        };
    }

    private const int MaxSettingsResults = 500;

    private Dictionary GetSettings(Dictionary parms)
    {
        var section = GetOr(parms,"section", "").AsString();
        var key = GetOr(parms,"key", "").AsString();

        if (!string.IsNullOrEmpty(section) && !string.IsNullOrEmpty(key))
        {
            var settingPath = $"{section}/{key}";
            if (ProjectSettings.HasSetting(settingPath))
            {
                var value = ProjectSettings.GetSetting(settingPath);
                return Success(new Dictionary { { "value", value } });
            }
            return Error($"Setting not found: {settingPath}");
        }

        var settings = new Dictionary();
        var ps = (GodotObject)Engine.GetSingleton("ProjectSettings");
        var prefix = string.IsNullOrEmpty(section) ? "" : section + "/";
        int count = 0;
        bool truncated = false;
        foreach (var propDict in ps.GetPropertyList())
        {
            var name = propDict["name"].AsString();
            // Skip internal/metadata properties that bloat results
            if (name.StartsWith("_") || name.StartsWith("editor_plugins/"))
                continue;
            if (string.IsNullOrEmpty(prefix) || name.StartsWith(prefix))
            {
                settings[name] = ProjectSettings.GetSetting(name);
                if (++count >= MaxSettingsResults)
                {
                    truncated = true;
                    break;
                }
            }
        }
        var result = new Dictionary { { "settings", settings } };
        if (truncated)
            result["truncated"] = true;
        return Success(result);
    }

    private const int MaxFileResults = 25000;

    private static readonly System.Collections.Generic.HashSet<string> SkipDirs = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ".godot", ".import", ".mono", ".vs", "bin", "obj"
    };

    private Dictionary ListFiles(Dictionary parms)
    {
        var rawPath = GetOr(parms,"path", "res://").AsString();
        var pathError = ValidateProjectPath(rawPath, out var path, "path");
        if (pathError != null) return pathError;

        var filter = GetOr(parms,"filter", "").AsString();
        var recursive = GetOr(parms,"recursive", false).AsBool();
        var offset = Math.Max(0, GetOr(parms, "offset", 0).AsInt32());
        var limit = Math.Clamp(GetOr(parms, "limit", 200).AsInt32(), 1, 5000);

        var files = new Godot.Collections.Array();
        bool truncated = false;
        ListFilesRecursive(path, filter, recursive, files, ref truncated);
        var pagedFiles = SliceArray(files, offset, limit);
        var hasMore = offset + pagedFiles.Count < files.Count;
        var result = new Dictionary
        {
            { "files", pagedFiles },
            { "total_files", files.Count },
            { "returned_files", pagedFiles.Count },
            { "offset", offset },
            { "limit", limit },
            { "has_more", hasMore },
        };
        if (hasMore)
            result["next_offset"] = offset + pagedFiles.Count;
        if (truncated)
            result["truncated"] = true;
        return Success(result);
    }

    private void ListFilesRecursive(string path, string filter, bool recursive, Godot.Collections.Array files, ref bool truncated, bool textOnlyWhenNoFilter = false, Func<string, bool>? filePredicate = null)
    {
        if (files.Count >= MaxFileResults)
        {
            truncated = true;
            return;
        }

        var dir = DirAccess.Open(path);
        if (dir == null) return;

        dir.ListDirBegin();
        var fileName = dir.GetNext();
        while (!string.IsNullOrEmpty(fileName))
        {
            if (dir.CurrentIsDir())
            {
                if (recursive && !fileName.StartsWith(".") && !SkipDirs.Contains(fileName))
                    ListFilesRecursive(path.TrimEnd('/') + "/" + fileName, filter, true, files, ref truncated, textOnlyWhenNoFilter, filePredicate);
            }
            else
            {
                var matchesFilter = string.IsNullOrEmpty(filter) || MatchesFilter(fileName, filter);
                var matchesTextFallback = !textOnlyWhenNoFilter || !string.IsNullOrEmpty(filter) || LooksLikeTextFile(fileName);
                var matchesPredicate = filePredicate == null || filePredicate(fileName);
                if (matchesFilter && matchesTextFallback && matchesPredicate)
                    files.Add(JoinProjectPath(path, fileName));
            }
            if (files.Count >= MaxFileResults)
            {
                truncated = true;
                break;
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    private static bool MatchesFilter(string fileName, string filter)
    {
        if (filter.StartsWith("*."))
            return fileName.EndsWith(filter[1..], System.StringComparison.OrdinalIgnoreCase);
        return fileName.Contains(filter, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTextFile(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        return normalized.EndsWith(".gd") || normalized.EndsWith(".cs") || normalized.EndsWith(".tscn") || normalized.EndsWith(".tres") ||
               normalized.EndsWith(".res") || normalized.EndsWith(".cfg") || normalized.EndsWith(".json") || normalized.EndsWith(".txt") ||
               normalized.EndsWith(".md") || normalized.EndsWith(".shader") || normalized.EndsWith(".gdshader") || normalized.EndsWith(".yml") ||
               normalized.EndsWith(".yaml") || normalized.EndsWith(".xml");
    }

    private Dictionary ReadFile(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        if (!FileAccess.FileExists(path))
            return Error($"File not found: {path}");
        var content = FileAccess.GetFileAsString(path);
        return Success(new Dictionary { { "content", content }, { "path", path } });
    }

    private Dictionary WriteFile(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var content = parms["content"].AsString();

        var dir = path[..path.LastIndexOf('/')];
        DirAccess.MakeDirRecursiveAbsolute(dir);

        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
            return Error($"Cannot write to: {path} ({FileAccess.GetOpenError()})");
        file.StoreString(content);
        file.Close();

        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        return Success(new Dictionary { { "path", path } });
    }



    private Dictionary SearchText(Dictionary parms)
    {
        var pathError = ValidateProjectPath(GetOr(parms, "path", "res://").AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var query = GetOr(parms, "query", string.Empty).AsString();
        if (string.IsNullOrWhiteSpace(query))
            return Error("query is required.");

        var recursive = GetOr(parms, "recursive", true).AsBool();
        var filter = GetOr(parms, "filter", string.Empty).AsString();
        var caseSensitive = GetOr(parms, "case_sensitive", false).AsBool();
        var wholeWord = GetOr(parms, "whole_word", false).AsBool();
        var maxResults = Math.Clamp(GetOr(parms, "max_results", 200).AsInt32(), 1, 2000);
        var scope = GetOr(parms, "scope", "all").AsString();
        var scriptsOnly = string.Equals(scope, "scripts", StringComparison.OrdinalIgnoreCase);

        var candidateFiles = new Godot.Collections.Array();
        bool truncated = false;

        if (scriptsOnly)
        {
            // 脚本专用搜索：只搜索 .gd/.cs 文件
            var scriptFilter = string.IsNullOrWhiteSpace(filter) ? string.Empty : filter;
            ListFilesRecursive(path, scriptFilter, recursive, candidateFiles, ref truncated, textOnlyWhenNoFilter: false, filePredicate: (fileName) =>
                fileName.EndsWith(".gd", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            ListFilesRecursive(path, filter, recursive, candidateFiles, ref truncated, textOnlyWhenNoFilter: true);
        }

        var regexPattern = wholeWord ? $"\\b{Regex.Escape(query)}\\b" : Regex.Escape(query);
        var regexOptions = caseSensitive ? RegexOptions.Multiline : RegexOptions.Multiline | RegexOptions.IgnoreCase;
        var regex = new Regex(regexPattern, regexOptions | RegexOptions.Compiled);

        var matches = new Godot.Collections.Array();
        foreach (var fileVariant in candidateFiles)
        {
            var filePath = fileVariant.AsString();
            if (!FileAccess.FileExists(filePath))
                continue;

            var content = FileAccess.GetFileAsString(filePath);
            if (string.IsNullOrEmpty(content))
                continue;

            var lines = content.Replace("\r\n", "\n").Split('\n');
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                foreach (Match match in regex.Matches(lines[lineIndex]))
                {
                    var matchEntry = new Dictionary
                    {
                        { "path", filePath },
                        { "line", lineIndex + 1 },
                        { "column", match.Index + 1 },
                        { "match", match.Value },
                        { "excerpt", lines[lineIndex].Trim() },
                    };

                    // 脚本搜索模式自动附加 language 字段
                    if (scriptsOnly)
                    {
                        matchEntry["language"] = filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? "cs" : "gd";
                    }

                    matches.Add(matchEntry);

                    if (matches.Count >= maxResults)
                    {
                        return Success(new Dictionary
                        {
                            { "query", query },
                            { "matches", matches },
                            { "count", matches.Count },
                            { "truncated", true },
                        });
                    }
                }
            }
        }

        return Success(new Dictionary
        {
            { "query", query },
            { "matches", matches },
            { "count", matches.Count },
            { "truncated", truncated },
        });
    }

    private Dictionary GetDependencies(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var dependencies = ResourceLoader.GetDependencies(path);
        var results = new Godot.Collections.Array();
        foreach (var dependency in dependencies)
            results.Add(BuildDependencyEntry(dependency.ToString()));

        return Success(new Dictionary
        {
            { "path", path },
            { "dependencies", results },
            { "count", results.Count },
        });
    }

    private Dictionary FindUnusedAssets(Dictionary parms)
    {
        var pathError = ValidateProjectPath(GetOr(parms, "path", "res://").AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var includeScripts = GetOr(parms, "include_scripts", false).AsBool();
        var candidates = new Godot.Collections.Array();
        bool truncated = false;
        CollectAssetFiles(path, candidates, includeScripts, ref truncated);

        var inboundCounts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dependencyCounts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var candidatePath = candidate.AsString();
            inboundCounts[candidatePath] = 0;
            dependencyCounts[candidatePath] = 0;
        }

        foreach (var candidate in candidates)
        {
            var candidatePath = candidate.AsString();
            foreach (var dependency in ResourceLoader.GetDependencies(candidatePath))
            {
                var normalizedDependencyPath = NormalizeDependencyPath(dependency.ToString());
                if (string.IsNullOrWhiteSpace(normalizedDependencyPath))
                    continue;

                dependencyCounts[candidatePath] = dependencyCounts.TryGetValue(candidatePath, out var dependencyCount)
                    ? dependencyCount + 1
                    : 1;

                if (inboundCounts.ContainsKey(normalizedDependencyPath))
                    inboundCounts[normalizedDependencyPath]++;
            }
        }

        var entryPoints = GetAssetEntryPoints();
        var unusedAssets = new Godot.Collections.Array();
        foreach (var candidate in candidates)
        {
            var candidatePath = candidate.AsString();
            if (entryPoints.Contains(candidatePath))
                continue;
            if (candidatePath.StartsWith("res://addons/godot-mcp/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (inboundCounts.TryGetValue(candidatePath, out var inboundCount) && inboundCount > 0)
                continue;

            unusedAssets.Add(new Dictionary
            {
                { "path", candidatePath },
                { "inbound_ref_count", inboundCount },
                { "dependency_count", dependencyCounts.TryGetValue(candidatePath, out var dependencyCount) ? dependencyCount : 0 },
                { "confidence", "heuristic" },
                { "reason", "No inbound references were found from scanned project resources." },
            });
        }

        return Success(new Dictionary
        {
            { "unused_assets", unusedAssets },
            { "count", unusedAssets.Count },
            { "truncated", truncated },
            { "include_scripts", includeScripts },
        });
    }

    private void CollectAssetFiles(string path, Godot.Collections.Array files, bool includeScripts, ref bool truncated)
    {
        ListFilesRecursive(path, string.Empty, true, files, ref truncated, textOnlyWhenNoFilter: false, filePredicate: (fileName) =>
        {
            if (IsTextScript(fileName))
                return includeScripts;

            return IsAssetCandidate(fileName);
        });
    }

    private static bool IsTextScript(string fileName) =>
        fileName.EndsWith(".gd", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssetCandidate(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();
        return normalized.EndsWith(".tscn") || normalized.EndsWith(".tres") || normalized.EndsWith(".res") ||
               normalized.EndsWith(".png") || normalized.EndsWith(".jpg") || normalized.EndsWith(".jpeg") || normalized.EndsWith(".webp") ||
               normalized.EndsWith(".wav") || normalized.EndsWith(".ogg") || normalized.EndsWith(".mp3") ||
               normalized.EndsWith(".shader") || normalized.EndsWith(".gdshader") || normalized.EndsWith(".material") || normalized.EndsWith(".font") || normalized.EndsWith(".mesh");
    }

    private static Dictionary BuildDependencyEntry(string rawDependency)
    {
        var normalizedPath = NormalizeDependencyPath(rawDependency);
        var uid = rawDependency.StartsWith("uid://", StringComparison.OrdinalIgnoreCase)
            ? rawDependency.Split(new[] { "::" }, StringSplitOptions.None)[0]
            : string.Empty;

        return new Dictionary
        {
            { "raw", rawDependency },
            { "path", normalizedPath },
            { "uid", uid },
        };
    }

    private static string NormalizeDependencyPath(string rawDependency)
    {
        if (string.IsNullOrWhiteSpace(rawDependency))
            return string.Empty;

        var dependency = rawDependency.Trim();
        if (dependency.Contains("::", StringComparison.Ordinal))
            dependency = dependency[(dependency.LastIndexOf("::", StringComparison.Ordinal) + 2)..];

        var resIndex = dependency.IndexOf("res://", StringComparison.OrdinalIgnoreCase);
        if (resIndex >= 0)
            return dependency[resIndex..];

        var userIndex = dependency.IndexOf("user://", StringComparison.OrdinalIgnoreCase);
        if (userIndex >= 0)
            return dependency[userIndex..];

        return dependency;
    }

    private static Godot.Collections.Array SliceArray(Godot.Collections.Array source, int offset, int limit)
    {
        var result = new Godot.Collections.Array();
        for (int i = offset; i < Math.Min(source.Count, offset + limit); i++)
            result.Add(source[i]);
        return result;
    }

    private Godot.Collections.Array GetAssetEntryPoints()
    {
        var entryPoints = new Godot.Collections.Array();
        var mainScene = ProjectSettings.GetSetting("application/run/main_scene").AsString();
        if (!string.IsNullOrWhiteSpace(mainScene))
            entryPoints.Add(mainScene);

        var settingsObject = (GodotObject)Engine.GetSingleton("ProjectSettings");
        foreach (var property in settingsObject.GetPropertyList())
        {
            var propertyName = property["name"].AsString();
            if (!propertyName.StartsWith("autoload/", StringComparison.Ordinal))
                continue;

            var value = ProjectSettings.GetSetting(propertyName).AsString().TrimStart('*');
            if (!string.IsNullOrWhiteSpace(value))
                entryPoints.Add(NormalizeProjectPath(value));
        }

        return entryPoints;
    }
}
#endif
