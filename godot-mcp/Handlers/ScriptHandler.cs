#if TOOLS
using Godot;
using Godot.Collections;
using System;
using System.Text.RegularExpressions;
using FileAccess = Godot.FileAccess;
using DirAccess = Godot.DirAccess;

namespace GodotMCP.Handlers;

public class ScriptHandler : BaseHandler
{
    public ScriptHandler(EditorPlugin plugin) : base(plugin) { }

    public override Dictionary Handle(string command, Dictionary parms)
    {
        return command switch
        {
            "list" => ListScripts(parms),
            "read" => ReadScript(parms),
            "create" => CreateScript(parms),
            "edit" => EditScript(parms),
            "attach" => AttachScript(parms),
            "detach" => DetachScript(parms),
            "find_references" => FindReferences(parms),
            _ => Error($"Unknown script command: {command}")
        };
    }

    private Dictionary ListScripts(Dictionary parms)
    {
        var pathError = ValidateProjectPath(GetOr(parms,"path", "res://").AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var language = GetOr(parms,"language", "all").AsString();
        var scripts = new Godot.Collections.Array();
        CollectScripts(path, language, scripts);
        return Success(new Dictionary { { "scripts", scripts }, { "count", scripts.Count } });
    }

    private void CollectScripts(string path, string language, Godot.Collections.Array scripts)
    {
        var dir = DirAccess.Open(path);
        if (dir == null) return;
        dir.ListDirBegin();
        var fileName = dir.GetNext();
        while (!string.IsNullOrEmpty(fileName))
        {
            var fullPath = JoinProjectPath(path, fileName);
            if (dir.CurrentIsDir())
            {
                if (!fileName.StartsWith(".") && fileName != "addons")
                    CollectScripts(fullPath, language, scripts);
            }
            else
            {
                bool include = language switch
                {
                    "cs" => fileName.EndsWith(".cs"),
                    "gd" => fileName.EndsWith(".gd"),
                    _ => fileName.EndsWith(".cs") || fileName.EndsWith(".gd")
                };
                if (include) scripts.Add(fullPath);
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    private Dictionary ReadScript(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        if (!FileAccess.FileExists(path)) return Error($"Script not found: {path}");
        var content = FileAccess.GetFileAsString(path);
        return Success(new Dictionary { { "path", path }, { "content", content } });
    }

    private Dictionary CreateScript(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var content = parms["content"].AsString();
        if (FileAccess.FileExists(path)) return Error($"Script already exists: {path}");
        var dir = path[..path.LastIndexOf('/')];
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null) return Error($"Cannot create: {path}");
        file.StoreString(content);
        file.Close();
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        return Success(new Dictionary { { "path", path } });
    }

    private Dictionary EditScript(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var content = parms["content"].AsString();
        if (!FileAccess.FileExists(path)) return Error($"Script not found: {path}");
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null) return Error($"Cannot write to: {path}");
        file.StoreString(content);
        file.Close();
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        return Success(new Dictionary { { "path", path } });
    }

    private Dictionary AttachScript(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var pathError = ValidateProjectPath(parms["script_path"].AsString(), out var scriptPath, "script_path");
        if (pathError != null) return pathError;

        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var script = ResourceLoader.Load<Script>(scriptPath);
        if (script == null) return Error($"Script not found: {scriptPath}");
        node.SetScript(script);
        return Success(new Dictionary { { "node_path", nodePath }, { "script_path", scriptPath } });
    }

    private Dictionary DetachScript(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        node.SetScript(default(Variant));
        return Success(new Dictionary { { "node_path", nodePath } });
    }

    private Dictionary FindReferences(Dictionary parms)
    {
        var pathError = ValidateProjectPath(GetOr(parms, "path", "res://").AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var symbol = GetOr(parms, "symbol", string.Empty).AsString();
        if (string.IsNullOrWhiteSpace(symbol))
            return Error("symbol is required.");

        var language = GetOr(parms, "language", "all").AsString();
        var wholeWord = GetOr(parms, "whole_word", true).AsBool();
        var caseSensitive = GetOr(parms, "case_sensitive", false).AsBool();
        var maxResults = Math.Clamp(GetOr(parms, "max_results", 200).AsInt32(), 1, 2000);

        var scripts = new Godot.Collections.Array();
        CollectScripts(path, language, scripts);

        var regexPattern = wholeWord ? $"\\b{Regex.Escape(symbol)}\\b" : Regex.Escape(symbol);
        var regexOptions = caseSensitive ? RegexOptions.Multiline : RegexOptions.Multiline | RegexOptions.IgnoreCase;
        var regex = new Regex(regexPattern, regexOptions);

        var references = new Godot.Collections.Array();
        foreach (var scriptVariant in scripts)
        {
            var scriptPath = scriptVariant.AsString();
            if (!FileAccess.FileExists(scriptPath))
                continue;

            var content = FileAccess.GetFileAsString(scriptPath);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var lines = content.Replace("\r\n", "\n").Split('\n');
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                foreach (Match match in regex.Matches(lines[lineIndex]))
                {
                    references.Add(new Dictionary
                    {
                        { "path", scriptPath },
                        { "line", lineIndex + 1 },
                        { "column", match.Index + 1 },
                        { "match", match.Value },
                        { "excerpt", lines[lineIndex].Trim() },
                        { "language", scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? "cs" : "gd" },
                    });

                    if (references.Count >= maxResults)
                    {
                        return Success(new Dictionary
                        {
                            { "symbol", symbol },
                            { "references", references },
                            { "count", references.Count },
                            { "truncated", true },
                        });
                    }
                }
            }
        }

        return Success(new Dictionary
        {
            { "symbol", symbol },
            { "references", references },
            { "count", references.Count },
            { "truncated", false },
        });
    }
}
#endif
