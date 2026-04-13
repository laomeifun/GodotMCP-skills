#if TOOLS
using Godot;
using Godot.Collections;
using System;
using System.Threading.Tasks;

namespace GodotMCP.Handlers;

public class InputHandler : BaseHandler
{
    public InputHandler(EditorPlugin plugin) : base(plugin) { }

    public override Dictionary Handle(string command, Dictionary parms)
    {
        return Error($"Input command '{command}' requires async routing.");
    }

    public override async Task<Dictionary> HandleAsync(string command, Dictionary parms)
    {
        EnsureGameIsRunning();

        return command switch
        {
            "key" => Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandInputKey, BuildKeyPayload(parms), GetKeyTimeout(parms))),
            "mouse" => Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandInputMouse, BuildMousePayload(parms), 5000)),
            "action" => Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandInputAction, BuildActionPayload(parms), GetActionTimeout(parms))),
            "text" => Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandInputText, BuildTextPayload(parms), 5000)),
            "sequence" => await HandleSequenceAsync(parms),
            _ => Error($"Unknown input command: {command}")
        };
    }

    private static Dictionary BuildKeyPayload(Dictionary parms) => new()
    {
        { "key", parms["key"].AsString() },
        { "pressed", GetOr(parms, "pressed", true).AsBool() },
        { "duration", Math.Max(0, GetOr(parms, "duration", 0).AsInt32()) },
    };

    private static Dictionary BuildMousePayload(Dictionary parms) => new()
    {
        { "position", parms["position"] },
        { "button", GetOr(parms, "button", "left").AsString() },
        { "action", GetOr(parms, "action", "click").AsString() },
    };

    private static Dictionary BuildActionPayload(Dictionary parms) => new()
    {
        { "action_name", parms["action_name"].AsString() },
        { "pressed", GetOr(parms, "pressed", true).AsBool() },
        { "strength", GetOr(parms, "strength", 1.0).AsDouble() },
        { "duration", Math.Max(0, GetOr(parms, "duration", 0).AsInt32()) },
    };

    private static Dictionary BuildTextPayload(Dictionary parms) => new()
    {
        { "text", parms["text"].AsString() },
    };

    private async Task<Dictionary> HandleSequenceAsync(Dictionary parms)
    {
        // 回放已有宏
        var replay = GetOr(parms, "replay", string.Empty).AsString();
        if (!string.IsNullOrWhiteSpace(replay))
        {
            var playbackPayload = new Dictionary
            {
                { "name", replay },
                { "loop_count", Math.Clamp(GetOr(parms, "loop_count", 1).AsInt32(), 1, 32) },
            };
            return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandPlaybackMacro, playbackPayload, GetMacroPlaybackTimeout(parms)));
        }

        // 保存为命名宏
        var saveAs = GetOr(parms, "save_as", string.Empty).AsString();
        if (!string.IsNullOrWhiteSpace(saveAs))
        {
            var recordPayload = new Dictionary
            {
                { "name", saveAs },
                { "steps", parms["steps"] },
            };
            return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandRecordMacro, recordPayload, 5000));
        }

        // 普通序列执行
        var sequencePayload = new Dictionary { { "steps", parms["steps"] } };
        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandInputSequence, sequencePayload, GetSequenceTimeout(parms)));
    }

    private static int GetKeyTimeout(Dictionary parms) => Math.Max(5000, GetOr(parms, "duration", 0).AsInt32() + 5000);

    private static int GetActionTimeout(Dictionary parms) => Math.Max(5000, GetOr(parms, "duration", 0).AsInt32() + 5000);

    private static int GetSequenceTimeout(Dictionary parms)
    {
        var totalDelay = 0;
        if (parms.TryGetValue("steps", out var stepsVariant) && stepsVariant.VariantType == Variant.Type.Array)
        {
            foreach (var step in stepsVariant.AsGodotArray())
            {
                if (step.VariantType != Variant.Type.Dictionary)
                    continue;

                var stepDict = step.AsGodotDictionary();
                totalDelay += Math.Max(0, GetOr(stepDict, "delay_ms", 0).AsInt32());
                if (stepDict.TryGetValue("params", out var paramsVariant) && paramsVariant.VariantType == Variant.Type.Dictionary)
                {
                    var stepParams = paramsVariant.AsGodotDictionary();
                    totalDelay += Math.Max(0, GetOr(stepParams, "duration", 0).AsInt32());
                }
            }
        }

        return Math.Max(5000, totalDelay + 5000);
    }

    private static int GetMacroPlaybackTimeout(Dictionary parms)
    {
        var loopCount = Math.Clamp(GetOr(parms, "loop_count", 1).AsInt32(), 1, 32);
        return 5000 * loopCount;
    }

    private static void EnsureGameIsRunning()
    {
        if (!EditorInterface.Singleton.IsPlayingScene())
            throw new InvalidOperationException("No game is currently running. Start a scene first with scene_play.");
    }
}
#endif
