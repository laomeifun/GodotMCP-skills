#if TOOLS
using Godot;
using Godot.Collections;
using FileAccess = Godot.FileAccess;
using DirAccess = Godot.DirAccess;
using Error = Godot.Error;

namespace GodotMCP.Handlers;

public class SceneHandler : BaseHandler
{
    public SceneHandler(EditorPlugin plugin) : base(plugin) { }

    public override Dictionary Handle(string command, Dictionary parms)
    {
        return command switch
        {
            "create" => CreateScene(parms),
            "open" => OpenScene(parms),
            "get_current" => GetCurrentScene(),
            "save" => SaveScene(parms),
            "delete" => DeleteScene(parms),
            "instance" => InstanceScene(parms),
            "get_tree" => GetTree(parms),
            "play" => PlayScene(parms),
            "stop" => StopScene(),
            _ => Error($"Unknown scene command: {command}")
        };
    }

    private Dictionary CreateScene(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        var rootType = parms["root_type"].AsString();
        var rootName = GetOr(parms,"root_name", "").AsString();

        var node = ClassDB.Instantiate(rootType).AsGodotObject() as Node;
        if (node == null)
            return Error($"Invalid node type: {rootType}");

        node.Name = !string.IsNullOrEmpty(rootName) ? rootName : path.GetFile().GetBaseName();

        var scene = new PackedScene();
        scene.Pack(node);
        var err = ResourceSaver.Save(scene, path);
        node.Free();

        if (err != Godot.Error.Ok)
            return Error($"Failed to save scene: {err}");

        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        return Success(new Dictionary { { "path", path } });
    }

    private Dictionary OpenScene(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        EditorInterface.Singleton.OpenSceneFromPath(path);
        // Force the main screen to 2D so the viewport actually renders the opened scene
        EditorInterface.Singleton.SetMainScreenEditor("2D");
        return Success(new Dictionary { { "path", path } });
    }

    private Dictionary GetCurrentScene()
    {
        var root = GetEditedRoot();
        if (root == null) return Error("No scene is currently open");
        return Success(new Dictionary { { "name", root.Name }, { "path", root.SceneFilePath }, { "type", root.GetClass() } });
    }

    private Dictionary SaveScene(Dictionary parms)
    {
        var path = GetOr(parms,"path", "").AsString();
        if (string.IsNullOrEmpty(path))
        {
            EditorInterface.Singleton.SaveScene();
            return Success(new Dictionary { { "saved", "current" } });
        }
        var pathError = ValidateProjectPath(path, out path, "path");
        if (pathError != null) return pathError;

        EditorInterface.Singleton.OpenSceneFromPath(path);
        EditorInterface.Singleton.SaveScene();
        return Success(new Dictionary { { "path", path } });
    }

    private Dictionary DeleteScene(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["path"].AsString(), out var path, "path");
        if (pathError != null) return pathError;

        if (!FileAccess.FileExists(path)) return Error($"Scene not found: {path}");
        var err = DirAccess.RemoveAbsolute(path);
        if (err != Godot.Error.Ok) return Error($"Failed to delete: {err}");
        if (FileAccess.FileExists(path + ".import"))
            DirAccess.RemoveAbsolute(path + ".import");
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        return Success(new Dictionary { { "deleted", path } });
    }

    private Dictionary InstanceScene(Dictionary parms)
    {
        var pathError = ValidateProjectPath(parms["scene_path"].AsString(), out var scenePath, "scene_path");
        if (pathError != null) return pathError;

        var parentPath = parms["parent_path"].AsString();
        var name = GetOr(parms,"name", "").AsString();

        var parent = FindNode(parentPath);
        if (parent == null) return Error($"Parent node not found: {parentPath}");

        var packedScene = ResourceLoader.Load<PackedScene>(scenePath);
        if (packedScene == null) return Error($"Cannot load scene: {scenePath}");

        var instance = packedScene.Instantiate();
        if (!string.IsNullOrEmpty(name)) instance.Name = name;

        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Instance Scene");
        undoRedo.AddDoMethod(parent, "add_child", instance);
        undoRedo.AddDoProperty(instance, "owner", GetEditedRoot());
        undoRedo.AddDoReference(instance);
        undoRedo.AddUndoMethod(parent, "remove_child", instance);
        undoRedo.CommitAction();

        return Success(new Dictionary { { "node_path", instance.GetPath().ToString() }, { "name", instance.Name } });
    }

    private Dictionary GetTree(Dictionary parms)
    {
        var path = GetOr(parms, "path", "").AsString();
        var depth = Math.Clamp(GetOr(parms, "depth", 10).AsInt32(), 0, 20);
        var requestedNodePath = GetOr(parms, "node_path", string.Empty).AsString();
        var flatten = GetOr(parms, "flatten", false).AsBool();
        var offset = Math.Max(0, GetOr(parms, "offset", 0).AsInt32());
        var limit = Math.Clamp(GetOr(parms, "limit", 200).AsInt32(), 1, 1000);

        if (!string.IsNullOrEmpty(path))
        {
            var pathError = ValidateProjectPath(path, out path, "path");
            if (pathError != null) return pathError;

            // Load and inspect the requested scene without opening it in the editor.
            var packed = ResourceLoader.Load<PackedScene>(path);
            if (packed == null) return Error($"Cannot load scene: {path}");
            var tempRoot = packed.Instantiate();
            var targetNode = ResolveNodeInTree(tempRoot, requestedNodePath);
            if (targetNode == null)
            {
                tempRoot.QueueFree();
                return Error($"Node not found in scene: {requestedNodePath}");
            }

            var flatNodes = new Godot.Collections.Array();
            CollectNodes(targetNode, depth, 0, flatNodes);
            var pagedNodes = SliceArray(flatNodes, offset, limit);
            var hasMore = offset + pagedNodes.Count < flatNodes.Count;

            var result = new Dictionary
            {
                { "path", path },
                { "node_path", targetNode.GetPath().ToString() },
                { "depth", depth },
                { "flatten", flatten },
                { "nodes", pagedNodes },
                { "total_nodes", flatNodes.Count },
                { "returned_nodes", pagedNodes.Count },
                { "offset", offset },
                { "limit", limit },
                { "has_more", hasMore },
            };
            if (hasMore)
                result["next_offset"] = offset + pagedNodes.Count;
            if (!flatten)
                result["tree"] = BuildTreeDict(targetNode, depth, 0);
            tempRoot.QueueFree();
            return Success(result);
        }
        var root = GetEditedRoot();
        if (root == null) return Error("No scene is currently open");

        var target = ResolveNodeInTree(root, requestedNodePath);
        if (target == null) return Error($"Node not found in scene: {requestedNodePath}");

        var flattenedNodes = new Godot.Collections.Array();
        CollectNodes(target, depth, 0, flattenedNodes);
        var paged = SliceArray(flattenedNodes, offset, limit);
        var hasMoreCurrent = offset + paged.Count < flattenedNodes.Count;
        var currentResult = new Dictionary
        {
            { "node_path", target.GetPath().ToString() },
            { "depth", depth },
            { "flatten", flatten },
            { "nodes", paged },
            { "total_nodes", flattenedNodes.Count },
            { "returned_nodes", paged.Count },
            { "offset", offset },
            { "limit", limit },
            { "has_more", hasMoreCurrent },
        };
        if (hasMoreCurrent)
            currentResult["next_offset"] = offset + paged.Count;
        if (!flatten)
            currentResult["tree"] = BuildTreeDict(target, depth, 0);
        return Success(currentResult);
    }

    private Dictionary BuildTreeDict(Node node, int maxDepth, int currentDepth)
    {
        var dict = BridgeSerialization.BuildNodeSummary(node, includeGroups: true);
        if (currentDepth >= maxDepth)
            return dict;

        if (node.GetChildCount() > 0)
        {
            var children = new Godot.Collections.Array();
            for (int i = 0; i < node.GetChildCount(); i++)
                children.Add(BuildTreeDict(node.GetChild(i), maxDepth, currentDepth + 1));
            dict["children"] = children;
        }
        return dict;
    }

    private static void CollectNodes(Node node, int maxDepth, int currentDepth, Godot.Collections.Array target)
    {
        target.Add(BridgeSerialization.BuildNodeSummary(node, includeGroups: true));
        if (currentDepth >= maxDepth)
            return;

        for (int i = 0; i < node.GetChildCount(); i++)
            CollectNodes(node.GetChild(i), maxDepth, currentDepth + 1, target);
    }

    private static Godot.Collections.Array SliceArray(Godot.Collections.Array source, int offset, int limit)
    {
        var result = new Godot.Collections.Array();
        for (int i = offset; i < System.Math.Min(source.Count, offset + limit); i++)
            result.Add(source[i]);
        return result;
    }

    private static Node? ResolveNodeInTree(Node root, string requestedNodePath)
    {
        if (string.IsNullOrWhiteSpace(requestedNodePath) || requestedNodePath == ".")
            return root;

        var normalized = requestedNodePath.Replace("//", "/").TrimEnd('/');
        if (normalized == root.Name || normalized == root.GetPath().ToString())
            return root;
        if (normalized.StartsWith("/root/", System.StringComparison.Ordinal))
            normalized = normalized["/root/".Length..];

        var rootNamePrefix = root.Name + "/";
        if (normalized.StartsWith(rootNamePrefix, System.StringComparison.Ordinal))
            normalized = normalized[rootNamePrefix.Length..];

        normalized = normalized.TrimStart('/');
        return string.IsNullOrWhiteSpace(normalized) ? root : root.GetNodeOrNull(normalized);
    }

    private Dictionary PlayScene(Dictionary parms)
    {
        var scenePath = GetOr(parms,"scene_path", "").AsString();
        if (string.IsNullOrEmpty(scenePath))
            EditorInterface.Singleton.PlayMainScene();
        else
        {
            var pathError = ValidateProjectPath(scenePath, out scenePath, "scene_path");
            if (pathError != null) return pathError;
            EditorInterface.Singleton.PlayCustomScene(scenePath);
        }
        return Success(new Dictionary { { "playing", true } });
    }

    private Dictionary StopScene()
    {
        if (!EditorInterface.Singleton.IsPlayingScene()) return Error("No scene is currently playing");
        EditorInterface.Singleton.StopPlayingScene();
        return Success(new Dictionary { { "stopped", true } });
    }
}
#endif
