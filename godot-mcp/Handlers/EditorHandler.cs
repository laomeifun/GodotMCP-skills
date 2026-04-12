#if TOOLS
using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GodotMCP.Handlers;

using Math = System.Math;

public class EditorHandler : BaseHandler
{
    private static readonly Godot.Collections.Array SharedLog = new();
    private const int MaxLogEntries = 500;

    public EditorHandler(EditorPlugin plugin) : base(plugin) { }

    public override Dictionary Handle(string command, Dictionary parms)
    {
        return command switch
        {
            "screenshot" => TakeScreenshot(parms),
            "get_errors" => GetErrors(parms),
            "get_compilation_errors" => GetCompilationErrors(parms),
            "execute_gdscript" => ExecuteGdScript(parms),
            "execute_csharp" => ExecuteCSharp(parms),
            "reload_project" => ReloadProject(),
            "get_open_files" => GetOpenFiles(),
            "open_file" => OpenFile(parms),
            "game_screenshot" => Error("Editor command 'game_screenshot' requires async routing."),
            _ => Error($"Unknown editor command: {command}")
        };
    }

    public override async Task<Dictionary> HandleAsync(string command, Dictionary parms)
    {
        return command switch
        {
            "game_screenshot" => await TakeGameScreenshotAsync(),
            _ => await base.HandleAsync(command, parms),
        };
    }

    private const int MaxScreenshotWidth = 1920;

    private Dictionary TakeScreenshot(Dictionary parms)
    {
        var viewport = GetOr(parms, "viewport", "full").AsString();
        switch (viewport)
        {
            case "2d": EditorInterface.Singleton.SetMainScreenEditor("2D"); break;
            case "3d": EditorInterface.Singleton.SetMainScreenEditor("3D"); break;
        }
        var editorViewport = EditorInterface.Singleton.GetBaseControl().GetViewport();
        var texRid = editorViewport.GetTexture().GetRid();
        var image = RenderingServer.Texture2DGet(texRid);
        if (image == null || image.IsEmpty()) return Error("Failed to capture viewport");

        if (image.GetWidth() > MaxScreenshotWidth)
        {
            var scale = (float)MaxScreenshotWidth / image.GetWidth();
            image.Resize((int)(image.GetWidth() * scale), (int)(image.GetHeight() * scale));
        }

        var savePath = ProjectSettings.GlobalizePath($"user://mcp_screenshot_{Time.GetTicksMsec()}.jpg");
        var err = image.SaveJpg(savePath, 0.85f);
        if (err != Godot.Error.Ok) return Error($"Failed to save screenshot: {err}");
        return Success(new Dictionary { { "path", savePath }, { "format", "jpeg" } });
    }

    private async Task<Dictionary> TakeGameScreenshotAsync()
    {
        if (!EditorInterface.Singleton.IsPlayingScene())
            return Error("No game is currently running");

        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandCaptureScreenshot, new Dictionary(), 5000));
    }

    private Dictionary GetErrors(Dictionary parms)
    {
        var count = GetOr(parms, "count", 50).AsInt32();
        var errors = new Godot.Collections.Array();
        var startIdx = Math.Max(0, SharedLog.Count - count);
        for (int i = startIdx; i < SharedLog.Count; i++)
            errors.Add(SharedLog[i]);

        var diagnostics = CollectCompilationDiagnostics(errors, string.Empty);
        return Success(new Dictionary
        {
            { "errors", errors },
            { "count", errors.Count },
            { "compilation_errors", diagnostics },
            { "compilation_error_count", CountDiagnosticsBySeverity(diagnostics, "error") },
            { "compilation_warning_count", CountDiagnosticsBySeverity(diagnostics, "warning") },
        });
    }

    private Dictionary GetCompilationErrors(Dictionary parms)
    {
        var count = GetOr(parms, "count", 100).AsInt32();
        var language = GetOr(parms, "language", string.Empty).AsString();
        var errors = new Godot.Collections.Array();
        var startIdx = Math.Max(0, SharedLog.Count - count);
        for (int i = startIdx; i < SharedLog.Count; i++)
            errors.Add(SharedLog[i]);

        var diagnostics = CollectCompilationDiagnostics(errors, language);
        return Success(new Dictionary
        {
            { "diagnostics", diagnostics },
            { "count", diagnostics.Count },
            { "error_count", CountDiagnosticsBySeverity(diagnostics, "error") },
            { "warning_count", CountDiagnosticsBySeverity(diagnostics, "warning") },
        });
    }

    public static void RecordLog(string message, string type = "error", string source = "editor", string code = "", Dictionary? context = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var entry = new Dictionary
        {
            { "message", message },
            { "type", type },
            { "source", source },
            { "timestamp_ms", Time.GetTicksMsec() }
        };

        if (!string.IsNullOrWhiteSpace(code))
            entry["code"] = code;
        if (context != null && context.Count > 0)
            entry["context"] = context;

        var diagnostic = TryBuildCompilationDiagnostic(entry);
        if (diagnostic != null)
            entry["compilation_diagnostic"] = diagnostic;

        SharedLog.Add(entry);

        while (SharedLog.Count > MaxLogEntries)
            SharedLog.RemoveAt(0);
    }

    public static bool TryGetRecentCompilationErrorSummary(out string summary, int maxDiagnostics = 3)
    {
        summary = string.Empty;
        if (SharedLog.Count == 0 || maxDiagnostics <= 0)
            return false;

        var diagnostics = CollectCompilationDiagnostics(SharedLog, "cs");
        if (diagnostics.Count == 0)
            return false;

        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = diagnostics.Count - 1; i >= 0 && parts.Count < maxDiagnostics; i--)
        {
            if (diagnostics[i].VariantType != Variant.Type.Dictionary)
                continue;

            var diagnostic = diagnostics[i].AsGodotDictionary();
            if (diagnostic.TryGetValue("severity", out var severityVariant) &&
                !string.Equals(severityVariant.AsString(), "error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var formatted = FormatCompilationDiagnosticSummary(diagnostic);
            if (string.IsNullOrWhiteSpace(formatted) || !seen.Add(formatted))
                continue;

            parts.Add(formatted);
        }

        if (parts.Count == 0)
            return false;

        summary = string.Join(" | ", parts);
        return true;
    }

    private Dictionary ExecuteGdScript(Dictionary parms)
    {
        var code = parms["code"].AsString();
        var expression = new Expression();
        var err = expression.Parse(code);
        if (err != Godot.Error.Ok) return Error($"Parse error: {expression.GetErrorText()}");
        var result = expression.Execute();
        if (expression.HasExecuteFailed())
            return Error($"Execution error: {expression.GetErrorText()}");
        return Success(new Dictionary { { "result", result.ToString() } });
    }

    private Dictionary ExecuteCSharp(Dictionary parms)
    {
        return Error("C# expression execution requires Roslyn scripting. Use editor_execute_gdscript for dynamic code or script_edit for modifying .cs files.");
    }

    private Dictionary ReloadProject()
    {
        EditorInterface.Singleton.RestartEditor(true);
        return Success(new Dictionary { { "reloading", true } });
    }

    private Dictionary GetOpenFiles()
    {
        var scriptEditor = EditorInterface.Singleton.GetScriptEditor();
        var openScripts = scriptEditor.GetOpenScripts();
        var files = new Godot.Collections.Array();
        foreach (var script in openScripts)
            files.Add(new Dictionary { { "path", script.ResourcePath }, { "type", script.GetClass() } });
        return Success(new Dictionary { { "files", files }, { "count", files.Count } });
    }

    private Dictionary OpenFile(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var line = GetOr(parms, "line", 0).AsInt32();
        var script = ResourceLoader.Load<Script>(path);
        if (script != null)
        {
            EditorInterface.Singleton.EditScript(script, line);
            return Success(new Dictionary { { "path", path }, { "line", line } });
        }
        EditorInterface.Singleton.OpenSceneFromPath(path);
        return Success(new Dictionary { { "path", path } });
    }

    private static Godot.Collections.Array CollectCompilationDiagnostics(Godot.Collections.Array entries, string language)
    {
        var diagnostics = new Godot.Collections.Array();
        foreach (var entryVariant in entries)
        {
            if (entryVariant.VariantType != Variant.Type.Dictionary)
                continue;

            var entry = entryVariant.AsGodotDictionary();
            Dictionary? diagnostic = null;
            if (entry.TryGetValue("compilation_diagnostic", out var diagnosticVariant) && diagnosticVariant.VariantType == Variant.Type.Dictionary)
                diagnostic = diagnosticVariant.AsGodotDictionary();
            else
                diagnostic = TryBuildCompilationDiagnostic(entry);

            if (diagnostic == null)
                continue;
            if (!string.IsNullOrWhiteSpace(language) &&
                diagnostic.TryGetValue("language", out var languageVariant) &&
                !string.Equals(languageVariant.AsString(), language, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            diagnostics.Add(diagnostic);
        }

        return diagnostics;
    }

    private static int CountDiagnosticsBySeverity(Godot.Collections.Array diagnostics, string severity)
    {
        int count = 0;
        foreach (var diagnosticVariant in diagnostics)
        {
            if (diagnosticVariant.VariantType != Variant.Type.Dictionary)
                continue;

            var diagnostic = diagnosticVariant.AsGodotDictionary();
            if (diagnostic.TryGetValue("severity", out var severityVariant) &&
                string.Equals(severityVariant.AsString(), severity, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static string FormatCompilationDiagnosticSummary(Dictionary diagnostic)
    {
        var path = diagnostic.TryGetValue("path", out var pathVariant) ? pathVariant.AsString() : string.Empty;
        var line = diagnostic.TryGetValue("line", out var lineVariant) ? lineVariant.AsInt32() : -1;
        var code = diagnostic.TryGetValue("code", out var codeVariant) ? codeVariant.AsString() : string.Empty;
        var message = diagnostic.TryGetValue("message", out var messageVariant) ? messageVariant.AsString() : string.Empty;

        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var location = string.IsNullOrWhiteSpace(path)
            ? "C# build"
            : line > 0 ? $"{path}:{line}" : path;

        return string.IsNullOrWhiteSpace(code)
            ? $"{location}: {message}"
            : $"{location} {code}: {message}";
    }

    private static Dictionary? TryBuildCompilationDiagnostic(Dictionary entry)
    {
        var message = entry.TryGetValue("message", out var messageVariant) ? messageVariant.AsString() : string.Empty;
        var type = entry.TryGetValue("type", out var typeVariant) ? typeVariant.AsString() : "error";
        var source = entry.TryGetValue("source", out var sourceVariant) ? sourceVariant.AsString() : "editor";
        var code = entry.TryGetValue("code", out var codeVariant) ? codeVariant.AsString() : string.Empty;

        if (string.IsNullOrWhiteSpace(message))
            return null;

        var csharpPattern = new Regex(@"(?<path>(?:res|user)://[^\(\s]+)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s*(?<code>[A-Za-z]{1,4}\d+)?\s*:?\s*(?<message>.+)", RegexOptions.IgnoreCase);
        var genericPattern = new Regex(@"(?<path>(?:res|user)://[^:\s]+):(?<line>\d+)(?::(?<column>\d+))?:\s*(?<severity>error|warning)\s*:?\s*(?<message>.+)", RegexOptions.IgnoreCase);

        Match match;
        string language = InferLanguageFromPath(message);
        if (csharpPattern.IsMatch(message))
        {
            match = csharpPattern.Match(message);
            language = "cs";
        }
        else if (genericPattern.IsMatch(message))
        {
            match = genericPattern.Match(message);
        }
        else if (message.Contains("CS", StringComparison.OrdinalIgnoreCase) || source.Contains("csharp", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary
            {
                { "path", string.Empty },
                { "line", -1 },
                { "column", -1 },
                { "severity", type },
                { "code", code },
                { "message", message },
                { "language", string.IsNullOrWhiteSpace(language) ? "cs" : language },
                { "source", source },
                { "timestamp_ms", entry.TryGetValue("timestamp_ms", out var ts) ? ts : Time.GetTicksMsec() },
            };
        }
        else
        {
            return null;
        }

        return new Dictionary
        {
            { "path", match.Groups["path"].Value },
            { "line", ParseOrDefault(match.Groups["line"].Value, -1) },
            { "column", ParseOrDefault(match.Groups["column"].Value, -1) },
            { "severity", match.Groups["severity"].Success ? match.Groups["severity"].Value.ToLowerInvariant() : type },
            { "code", match.Groups["code"].Success && !string.IsNullOrWhiteSpace(match.Groups["code"].Value) ? match.Groups["code"].Value : code },
            { "message", match.Groups["message"].Success ? match.Groups["message"].Value : message },
            { "language", string.IsNullOrWhiteSpace(language) ? InferLanguageFromPath(match.Groups["path"].Value) : language },
            { "source", source },
            { "timestamp_ms", entry.TryGetValue("timestamp_ms", out var timestamp) ? timestamp : Time.GetTicksMsec() },
        };
    }

    private static int ParseOrDefault(string value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;

    private static string InferLanguageFromPath(string text)
    {
        if (text.Contains(".cs", StringComparison.OrdinalIgnoreCase))
            return "cs";
        if (text.Contains(".gd", StringComparison.OrdinalIgnoreCase))
            return "gd";
        return string.Empty;
    }
}
#endif
