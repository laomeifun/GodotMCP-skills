#if TOOLS
using Godot;
using Godot.Collections;
using System;
using System.Threading.Tasks;

namespace GodotMCP.Handlers;

public abstract class BaseHandler
{
    protected EditorPlugin Plugin { get; }

    protected BaseHandler(EditorPlugin plugin)
    {
        Plugin = plugin;
    }

    public virtual Dictionary Handle(string command, Dictionary parms) =>
        Error($"Synchronous handling is not implemented for {GetType().Name}.");

    public virtual Task<Dictionary> HandleAsync(string command, Dictionary parms) =>
        Task.FromResult(Handle(command, parms));

    protected Node GetEditedRoot() =>
        EditorInterface.Singleton.GetEditedSceneRoot();

    protected Node? FindNode(string path)
    {
        var root = GetEditedRoot();
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(path)) return root;

        // 规范化：去掉多余斜杠和尾部斜杠
        path = path.Replace("//", "/").TrimEnd('/');

        if (path == "." || path == root.Name) return root;
        if (path.StartsWith("/root/"))
            path = path["/root/".Length..];
        var rootName = root.Name + "/";
        if (path.StartsWith(rootName))
            path = path[rootName.Length..];
        else if (path == root.Name)
            return root;

        // 去除规范化后可能残留的前导斜杠
        path = path.TrimStart('/');
        if (string.IsNullOrEmpty(path)) return root;

        return root.GetNodeOrNull(path);
    }

    protected EditorUndoRedoManager GetUndoRedo() =>
        Plugin.GetUndoRedo();

    protected static string NormalizeProjectPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.Trim().Replace('\\', '/');

        if (normalized.StartsWith("res:/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "res://" + normalized["res:/".Length..].TrimStart('/');
        }

        if (normalized.StartsWith("user:/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "user://" + normalized["user:/".Length..].TrimStart('/');
        }

        var schemeSeparator = normalized.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
        {
            var prefix = normalized[..(schemeSeparator + 3)];
            var remainder = normalized[(schemeSeparator + 3)..];
            while (remainder.Contains("//", StringComparison.Ordinal))
                remainder = remainder.Replace("//", "/");
            remainder = remainder.TrimStart('/');
            return prefix + remainder;
        }

        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/");

        return normalized;
    }

    protected bool TryGetSafeProjectPath(string rawPath, out string normalized, bool allowUserPath = true)
    {
        normalized = NormalizeProjectPath(rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var isResPath = normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase);
        var isUserPath = allowUserPath && normalized.StartsWith("user://", StringComparison.OrdinalIgnoreCase);
        if (!isResPath && !isUserPath)
            return false;

        var schemeSeparator = normalized.IndexOf("://", StringComparison.Ordinal);
        var relative = schemeSeparator >= 0 ? normalized[(schemeSeparator + 3)..] : normalized;

        if (relative == ".." ||
            relative.StartsWith("../", StringComparison.Ordinal) ||
            relative.EndsWith("/..", StringComparison.Ordinal) ||
            relative.Contains("/../", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    protected Dictionary? ValidateProjectPath(string rawPath, out string normalized, string label = "path", bool allowUserPath = true)
    {
        if (TryGetSafeProjectPath(rawPath, out normalized, allowUserPath))
            return null;

        normalized = string.Empty;
        return Error(
            "INVALID_PATH",
            $"{label} must stay within res:// or user:// and cannot contain parent traversal.",
            new Dictionary
            {
                { "label", label },
                { "provided", rawPath ?? string.Empty },
                { "allow_user_path", allowUserPath },
            }
        );
    }

    protected static string JoinProjectPath(string basePath, string childName)
    {
        var normalizedBase = NormalizeProjectPath(basePath);
        var normalizedChild = childName.Replace('\\', '/').TrimStart('/');

        if (normalizedBase.EndsWith("://", StringComparison.Ordinal))
            return normalizedBase + normalizedChild;

        return normalizedBase.TrimEnd('/') + "/" + normalizedChild;
    }

    protected Dictionary Success(Dictionary? data = null) =>
        new() { { "success", true }, { "data", data ?? new Dictionary() } };

    protected Dictionary Error(string message)
    {
        return Error("COMMAND_FAILED", message);
    }

    protected Dictionary Error(string code, string message, Dictionary? context = null, bool retriable = false)
    {
        EditorHandler.RecordLog(message, retriable ? "warning" : "error", GetType().Name, code, context);

        var result = new Dictionary
        {
            { "success", false },
            { "error", message },
            { "error_code", code },
            { "retriable", retriable },
        };

        if (context != null && context.Count > 0)
            result["error_context"] = context;

        return result;
    }

    protected static Variant GetOr(Dictionary dict, string key, Variant defaultValue) =>
        dict.ContainsKey(key) ? dict[key] : defaultValue;
}
#endif
