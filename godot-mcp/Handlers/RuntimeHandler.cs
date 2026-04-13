#if TOOLS
using Godot;
using Godot.Collections;
using System;
using System.Threading.Tasks;

namespace GodotMCP.Handlers;

public class RuntimeHandler : BaseHandler
{
    private const int DefaultRuntimeReadyTimeoutMs = 20000;
    private const int DefaultRuntimeReadyPollIntervalMs = 100;

    public RuntimeHandler(EditorPlugin plugin) : base(plugin) { }

    public override Dictionary Handle(string command, Dictionary parms)
    {
        return command switch
        {
            "get_bridge_status" => GetBridgeStatus(),
            "get_logs" => GetLogs(parms),
            "wait_until_ready" => Error("Runtime command 'wait_until_ready' requires async routing."),
            _ => Error($"Runtime command '{command}' requires async routing.")
        };
    }

    public override async Task<Dictionary> HandleAsync(string command, Dictionary parms)
    {
        return command switch
        {
            "get_bridge_status" => GetBridgeStatus(),
            "get_scene_tree" => await GetSceneTreeAsync(parms),
            "get_node_properties" => await GetNodePropertiesAsync(parms),
            "capture_frame" => await CaptureFrameAsync(),
            "monitor_property" => await MonitorPropertyAsync(parms),
            "wait_until_ready" => await WaitUntilReadyAsync(parms),
            "get_logs" => GetLogs(parms),
            "watch_signal" => await WatchSignalAsync(parms),
            "evaluate_expression" => await EvaluateExpressionAsync(parms),
            _ => Error($"Unknown runtime command: {command}")
        };
    }

    private Dictionary GetBridgeStatus() => Success(RuntimeBridgeService.Instance.GetStatus());

    private Dictionary GetLogs(Dictionary parms)
    {
        var count = Math.Clamp(GetOr(parms, "count", 50).AsInt32(), 1, 250);
        var level = GetOr(parms, "level", string.Empty).AsString();
        var logs = RuntimeBridgeService.Instance.GetRecentLogs(count, string.IsNullOrWhiteSpace(level) ? null : level);
        return Success(new Dictionary
        {
            { "logs", logs },
            { "count", logs.Count },
        });
    }

    private async Task<Dictionary> GetSceneTreeAsync(Dictionary parms)
    {
        await EnsureRuntimeCommandReadyAsync();

        var payload = new Dictionary
        {
            { "depth", Math.Clamp(GetOr(parms, "depth", 10).AsInt32(), 1, 20) },
            { "include_internal", GetOr(parms, "include_internal", false).AsBool() },
            { "node_path", GetOr(parms, "node_path", string.Empty).AsString() },
        };

        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandGetSceneTree, payload, 8000));
    }

    private async Task<Dictionary> GetNodePropertiesAsync(Dictionary parms)
    {
        await EnsureRuntimeCommandReadyAsync();

        var payload = new Dictionary
        {
            { "node_path", parms["node_path"].AsString() },
            { "offset", Math.Max(0, GetOr(parms, "offset", 0).AsInt32()) },
            { "limit", Math.Clamp(GetOr(parms, "limit", 200).AsInt32(), 1, 500) },
        };

        if (parms.ContainsKey("property_names"))
            payload["property_names"] = parms["property_names"];

        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandGetNodeProperties, payload, 8000));
    }

    private async Task<Dictionary> CaptureFrameAsync()
    {
        await EnsureRuntimeCommandReadyAsync();
        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandCaptureFrame, new Dictionary(), 5000));
    }

    private async Task<Dictionary> MonitorPropertyAsync(Dictionary parms)
    {
        await EnsureRuntimeCommandReadyAsync();

        var duration = Math.Max(0, GetOr(parms, "duration", 1000).AsInt32());
        var payload = new Dictionary
        {
            { "node_path", parms["node_path"].AsString() },
            { "property", parms["property"].AsString() },
            { "duration", duration },
            { "interval_ms", Math.Clamp(GetOr(parms, "interval_ms", 100).AsInt32(), 16, 1000) },
            { "max_samples", Math.Clamp(GetOr(parms, "max_samples", 128).AsInt32(), 1, 512) },
        };

        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandMonitorProperty, payload, duration + 5000));
    }

    private async Task<Dictionary> WaitUntilReadyAsync(Dictionary parms)
    {
        var timeoutMs = Math.Max(100, GetOr(parms, "timeout_ms", 10000).AsInt32());
        var pollIntervalMs = Math.Clamp(GetOr(parms, "poll_interval_ms", 100).AsInt32(), 10, 1000);
        var startedAt = Time.GetTicksMsec();
        await EnsureRuntimeCommandReadyAsync(timeoutMs, pollIntervalMs);
        return Success(new Dictionary
        {
            { "ready", true },
            { "waited_ms", (long)(Time.GetTicksMsec() - startedAt) },
            { "status", RuntimeBridgeService.Instance.GetStatus() },
        });
    }

    private async Task<Dictionary> RunSmokeCheckAsync()
    {
        await EnsureRuntimeCommandReadyAsync();

        var status = RuntimeBridgeService.Instance.GetStatus();

        var checks = new Godot.Collections.Array
        {
            new Dictionary { { "name", "runtime_bridge_connected" }, { "ok", true } },
        };

        var tree = await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandGetSceneTree, new Dictionary { { "depth", 2 } }, 8000);
        checks.Add(new Dictionary { { "name", "runtime_scene_tree" }, { "ok", true } });

        var frame = await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandCaptureFrame, new Dictionary(), 5000);
        checks.Add(new Dictionary { { "name", "runtime_frame_capture" }, { "ok", true } });

        var logs = RuntimeBridgeService.Instance.GetRecentLogs(20);
        checks.Add(new Dictionary { { "name", "runtime_log_buffer" }, { "ok", logs.Count >= 0 } });

        return Success(new Dictionary
        {
            { "passed", true },
            { "checks", checks },
            { "status", status },
            { "scene_tree", tree },
            { "frame", frame },
            { "recent_logs", logs },
        });
    }

    private async Task<Dictionary> WatchSignalAsync(Dictionary parms)
    {
        await EnsureRuntimeCommandReadyAsync();

        var payload = new Dictionary
        {
            { "node_path", parms["node_path"].AsString() },
            { "signal", parms["signal"].AsString() },
            { "duration", Math.Max(0, GetOr(parms, "duration", 1000).AsInt32()) },
            { "max_events", Math.Clamp(GetOr(parms, "max_events", 128).AsInt32(), 1, 512) },
        };

        var timeoutMs = payload["duration"].AsInt32() + 5000;
        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandWatchSignal, payload, timeoutMs));
    }

    private async Task<Dictionary> WatchNodeLifecycleAsync(Dictionary parms)
    {
        await EnsureRuntimeCommandReadyAsync();

        var duration = Math.Max(0, GetOr(parms, "duration", 1000).AsInt32());
        var payload = new Dictionary
        {
            { "node_path", parms["node_path"].AsString() },
            { "duration", duration },
            { "poll_interval_ms", Math.Clamp(GetOr(parms, "poll_interval_ms", 100).AsInt32(), 16, 1000) },
            { "max_samples", Math.Clamp(GetOr(parms, "max_samples", 256).AsInt32(), 1, 1024) },
        };

        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandWatchNodeLifecycle, payload, duration + 5000));
    }

    private async Task<Dictionary> EvaluateExpressionAsync(Dictionary parms)
    {
        await EnsureRuntimeCommandReadyAsync();

        var payload = new Dictionary
        {
            { "expression", parms["expression"].AsString() },
            { "node_path", GetOr(parms, "node_path", string.Empty).AsString() },
        };

        return Success(await RuntimeBridgeService.Instance.RequestAsync(RuntimeBridgeProtocol.CommandEvaluateExpression, payload, 5000));
    }

    private static async Task EnsureRuntimeCommandReadyAsync(int timeoutMs = DefaultRuntimeReadyTimeoutMs, int pollIntervalMs = DefaultRuntimeReadyPollIntervalMs)
    {
        var timeoutAt = Time.GetTicksMsec() + (ulong)Math.Max(1, timeoutMs);
        while (!EditorInterface.Singleton.IsPlayingScene())
        {
            if (Time.GetTicksMsec() > timeoutAt)
                throw new InvalidOperationException("No game is currently running. Start a scene first with scene_play.");

            await Task.Delay(Math.Max(10, pollIntervalMs));
        }

        var remainingMs = Math.Max(100, (int)Math.Max(0, (long)(timeoutAt - Time.GetTicksMsec())));
        await RuntimeBridgeService.Instance.WaitUntilReadyAsync(remainingMs, pollIntervalMs);
    }

    private static void EnsureGameIsRunning()
    {
        if (!EditorInterface.Singleton.IsPlayingScene())
            throw new InvalidOperationException("No game is currently running. Start a scene first with scene_play.");
    }
}
#endif
