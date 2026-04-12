#if TOOLS
using Godot;
using Godot.Collections;
using GodotMCP.Handlers;
using System.Threading.Tasks;

namespace GodotMCP;

public class CommandRouter
{
    private readonly System.Collections.Generic.Dictionary<string, BaseHandler> _handlers = new();

    public void RegisterHandler(string category, BaseHandler handler)
    {
        _handlers[category] = handler;
    }

    public async Task<string> RouteAsync(string rawMessage)
    {
        var json = Json.ParseString(rawMessage);
        if (json.VariantType == Variant.Type.Nil)
        {
            EditorHandler.RecordLog("Failed to parse incoming MCP JSON payload.", "error", nameof(CommandRouter));
            return MakeError("invalid_json", "Failed to parse JSON");
        }

        var msg = json.AsGodotDictionary();
        var id = msg.ContainsKey("id") ? msg["id"].AsString() : "unknown";
        var category = msg.ContainsKey("category") ? msg["category"].AsString() : "";
        var command = msg.ContainsKey("command") ? msg["command"].AsString() : "";
        var parms = msg.ContainsKey("params")
            ? msg["params"].AsGodotDictionary()
            : new Dictionary();

        if (!_handlers.TryGetValue(category, out var handler))
        {
            EditorHandler.RecordLog($"Unknown command category: {category}", "error", nameof(CommandRouter));
            return MakeResponse(id, false, null, $"Unknown category: {category}");
        }

        try
        {
            var result = await handler.HandleAsync(command, parms);
            var success = result.ContainsKey("success") && result["success"].AsBool();
            var data = result.ContainsKey("data") ? result["data"] : new Dictionary();
            var error = result.ContainsKey("error") ? result["error"].AsString() : string.Empty;
            var errorCode = result.ContainsKey("error_code") ? result["error_code"].AsString() : string.Empty;
            var errorContext = result.ContainsKey("error_context") ? result["error_context"] : default(Variant);
            var retriable = result.ContainsKey("retriable") && result["retriable"].AsBool();
            return MakeResponse(id, success, data, error, errorCode, errorContext, retriable);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[GodotMCP] Error handling {category}.{command}: {ex.Message}");
            var context = new Dictionary
            {
                { "category", category },
                { "command", command },
            };
            EditorHandler.RecordLog($"Error handling {category}.{command}: {ex.Message}", "error", nameof(CommandRouter), "UNHANDLED_EXCEPTION", context);
            return MakeResponse(id, false, null, ex.Message, "UNHANDLED_EXCEPTION", context, false);
        }
    }

    private static string MakeResponse(string id, bool success, Variant? data, string error, string errorCode = "", Variant? errorContext = null, bool retriable = false)
    {
        var dict = new Dictionary
        {
            { "id", id },
            { "success", success }
        };
        if (success && data.HasValue)
            dict["data"] = data.Value;
        if (!success && error != null)
            dict["error"] = error;
        if (!success && !string.IsNullOrWhiteSpace(errorCode))
            dict["error_code"] = errorCode;
        if (!success)
            dict["retriable"] = retriable;
        if (!success && errorContext.HasValue && errorContext.Value.VariantType != Variant.Type.Nil)
            dict["error_context"] = errorContext.Value;
        return Json.Stringify(dict);
    }

    private static string MakeError(string id, string error) =>
        MakeResponse(id, false, null, error);
}
#endif
