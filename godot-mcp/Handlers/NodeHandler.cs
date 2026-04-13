#if TOOLS
using Godot;
using Godot.Collections;
using System;
using Error = Godot.Error;

namespace GodotMCP.Handlers;

public class NodeHandler : BaseHandler
{
    public NodeHandler(EditorPlugin plugin) : base(plugin) { }

    public override Dictionary Handle(string command, Dictionary parms)
    {
        return command switch
        {
            "add" => AddNode(parms),
            "delete" => DeleteNode(parms),
            "rename" => RenameNode(parms),
            "duplicate" => DuplicateNode(parms),
            "move" => MoveNode(parms),
            "get_properties" => GetProperties(parms),
            "set_property" => SetProperty(parms),
            "get_signals" => GetSignals(parms),
            "connect_signal" => ConnectSignal(parms),
            "disconnect_signal" => DisconnectSignal(parms),
            "get_children" => GetChildren(parms),
            "inspect_deep" => InspectDeep(parms),
            "batch_update" => BatchUpdate(parms),
            _ => Error($"Unknown node command: {command}")
        };
    }

    private Dictionary AddNode(Dictionary parms)
    {
        var parentPath = parms["parent_path"].AsString();
        var type = parms["type"].AsString();
        var name = parms["name"].AsString();
        var parent = FindNode(parentPath);
        if (parent == null) return Error($"Parent not found: {parentPath}");
        var node = ClassDB.Instantiate(type).AsGodotObject() as Node;
        if (node == null) return Error($"Invalid node type: {type}");
        node.Name = name;
        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Add Node");
        undoRedo.AddDoMethod(parent, "add_child", node);
        undoRedo.AddDoProperty(node, "owner", GetEditedRoot());
        undoRedo.AddDoReference(node);
        undoRedo.AddUndoMethod(parent, "remove_child", node);
        undoRedo.CommitAction();
        return Success(new Dictionary { { "node_path", node.GetPath().ToString() }, { "name", node.Name } });
    }

    private Dictionary DeleteNode(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        if (node == GetEditedRoot()) return Error("Cannot delete scene root node");
        var parent = node.GetParent();
        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Delete Node");
        undoRedo.AddDoMethod(parent, "remove_child", node);
        undoRedo.AddUndoMethod(parent, "add_child", node);
        undoRedo.AddUndoProperty(node, "owner", GetEditedRoot());
        undoRedo.AddUndoReference(node);
        undoRedo.CommitAction();
        return Success(new Dictionary { { "deleted", nodePath } });
    }

    private Dictionary RenameNode(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var newName = parms["new_name"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var oldName = node.Name;
        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Rename Node");
        undoRedo.AddDoProperty(node, "name", newName);
        undoRedo.AddUndoProperty(node, "name", oldName);
        undoRedo.CommitAction();
        return Success(new Dictionary { { "old_name", oldName }, { "new_name", newName } });
    }

    private Dictionary DuplicateNode(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var newName = GetOr(parms,"new_name", "").AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var duplicate = node.Duplicate();
        if (!string.IsNullOrEmpty(newName)) duplicate.Name = newName;
        var parent = node.GetParent();
        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Duplicate Node");
        undoRedo.AddDoMethod(parent, "add_child", duplicate);
        undoRedo.AddDoProperty(duplicate, "owner", GetEditedRoot());
        undoRedo.AddDoReference(duplicate);
        undoRedo.AddUndoMethod(parent, "remove_child", duplicate);
        undoRedo.CommitAction();
        return Success(new Dictionary { { "node_path", duplicate.GetPath().ToString() }, { "name", duplicate.Name } });
    }

    private Dictionary MoveNode(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var newParentPath = parms["new_parent_path"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var newParent = FindNode(newParentPath);
        if (newParent == null) return Error($"New parent not found: {newParentPath}");
        var oldParent = node.GetParent();
        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Move Node");
        undoRedo.AddDoMethod(oldParent, "remove_child", node);
        undoRedo.AddDoMethod(newParent, "add_child", node);
        undoRedo.AddDoProperty(node, "owner", GetEditedRoot());
        undoRedo.AddUndoMethod(newParent, "remove_child", node);
        undoRedo.AddUndoMethod(oldParent, "add_child", node);
        undoRedo.AddUndoProperty(node, "owner", GetEditedRoot());
        undoRedo.CommitAction();
        return Success(new Dictionary { { "node_path", node.GetPath().ToString() } });
    }

    private Dictionary GetProperties(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var requestedProperties = parms.ContainsKey("property_names") ? parms["property_names"].AsGodotArray() : null;
        var offset = Math.Max(0, GetOr(parms, "offset", 0).AsInt32());
        var limit = Math.Clamp(GetOr(parms, "limit", 200).AsInt32(), 1, 500);
        var propertyResult = BuildPropertyResult(node, requestedProperties, offset, limit, editorOnly: true);
        propertyResult["type"] = node.GetClass();
        propertyResult["node_path"] = node.GetPath().ToString();
        return Success(propertyResult);
    }

    private Dictionary SetProperty(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var property = parms["property"].AsString();
        var value = BridgeSerialization.UnwrapTransportValue(parms["value"]);
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var oldValue = node.Get(property);

        // 当推断出的类型与属性的实际类型不匹配时，尝试按目标类型重新转换
        value = BridgeSerialization.CoerceToPropertyType(value, oldValue);

        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Set Property");
        undoRedo.AddDoProperty(node, property, value);
        undoRedo.AddUndoProperty(node, property, oldValue);
        undoRedo.CommitAction();
        var newValue = node.Get(property);
        return Success(new Dictionary
        {
            { "property", property },
            { "value", BridgeSerialization.BuildPropertySnapshot(property, newValue) },
        });
    }

    private Dictionary GetSignals(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var signals = new Godot.Collections.Array();
        foreach (var sigDict in node.GetSignalList())
        {
            signals.Add(new Dictionary { { "name", sigDict["name"] }, { "args", GetOr(sigDict, "args", new Godot.Collections.Array()) } });
        }
        return Success(new Dictionary { { "signals", signals } });
    }

    private Dictionary ConnectSignal(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var signalName = parms["signal"].AsString();
        var targetPath = parms["target_path"].AsString();
        var method = parms["method"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var target = FindNode(targetPath);
        if (target == null) return Error($"Target not found: {targetPath}");
        var err = node.Connect(signalName, new Callable(target, method));
        if (err != Godot.Error.Ok) return Error($"Failed to connect signal: {err}");
        return Success(new Dictionary { { "connected", $"{signalName} -> {targetPath}.{method}" } });
    }

    private Dictionary DisconnectSignal(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var signalName = parms["signal"].AsString();
        var targetPath = parms["target_path"].AsString();
        var method = parms["method"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var target = FindNode(targetPath);
        if (target == null) return Error($"Target not found: {targetPath}");
        if (!node.IsConnected(signalName, new Callable(target, method)))
            return Error("Signal connection not found");
        node.Disconnect(signalName, new Callable(target, method));
        return Success(new Dictionary { { "disconnected", $"{signalName} -> {targetPath}.{method}" } });
    }

    private Dictionary GetChildren(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");
        var children = new Godot.Collections.Array();
        for (int i = 0; i < node.GetChildCount(); i++)
        {
            var child = node.GetChild(i);
            var summary = BridgeSerialization.BuildNodeSummary(child, includeGroups: true);
            summary["index"] = i;
            children.Add(summary);
        }
        return Success(new Dictionary { { "children", children }, { "count", node.GetChildCount() } });
    }

    private Dictionary InspectDeep(Dictionary parms)
    {
        var nodePath = parms["node_path"].AsString();
        var node = FindNode(nodePath);
        if (node == null) return Error($"Node not found: {nodePath}");

        var depth = Math.Clamp(GetOr(parms, "depth", 2).AsInt32(), 0, 6);
        var includeChildProperties = GetOr(parms, "include_child_properties", false).AsBool();
        var propertyOffset = Math.Max(0, GetOr(parms, "property_offset", 0).AsInt32());
        var propertyLimit = Math.Clamp(GetOr(parms, "property_limit", 200).AsInt32(), 1, 500);

        var propertyResult = BuildPropertyResult(node, null, propertyOffset, propertyLimit, editorOnly: true);
        return Success(new Dictionary
        {
            { "node", BridgeSerialization.BuildNodeSummary(node, includeGroups: true) },
            { "signals", BuildSignalArray(node) },
            { "tree", BuildInspectionTree(node, depth, includeChildProperties, 0) },
            { "properties", propertyResult["properties"] },
            { "property_order", propertyResult["property_order"] },
            { "total_properties", propertyResult["total_properties"] },
            { "returned_properties", propertyResult["returned_properties"] },
            { "offset", propertyResult["offset"] },
            { "limit", propertyResult["limit"] },
            { "has_more", propertyResult["has_more"] },
            { "next_offset", propertyResult["next_offset"] },
        });
    }

    private Dictionary BatchUpdate(Dictionary parms)
    {
        if (!parms.ContainsKey("operations") || parms["operations"].VariantType != Variant.Type.Array)
            return Error("operations must be an array.");

        var operations = parms["operations"].AsGodotArray();
        var actionName = GetOr(parms, "action_name", "Batch Node Update").AsString();
        var undoRedo = GetUndoRedo();
        var aliases = new System.Collections.Generic.Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var results = new Godot.Collections.Array();

        undoRedo.CreateAction(actionName);

        foreach (var operationVariant in operations)
        {
            if (operationVariant.VariantType != Variant.Type.Dictionary)
                return Error("Each batch operation must be a dictionary.");

            var operation = operationVariant.AsGodotDictionary();
            var operationType = GetOr(operation, "type", string.Empty).AsString();
            switch (operationType)
            {
                case "add":
                    {
                        var parent = ResolveNodeReference(operation, "parent_path", "parent_alias", aliases);
                        if (parent == null) return Error("Parent node not found for add operation.");
                        var nodeType = GetOr(operation, "node_type", string.Empty).AsString();
                        if (string.IsNullOrWhiteSpace(nodeType))
                            nodeType = GetOr(operation, "type_name", string.Empty).AsString();
                        if (string.IsNullOrWhiteSpace(nodeType))
                            nodeType = GetOr(operation, "class", string.Empty).AsString();
                        var nodeName = GetOr(operation, "name", string.Empty).AsString();
                        if (string.IsNullOrWhiteSpace(nodeType) || string.IsNullOrWhiteSpace(nodeName))
                            return Error("add operation requires node_type and name.");

                        var node = ClassDB.Instantiate(nodeType).AsGodotObject() as Node;
                        if (node == null) return Error($"Invalid node type: {nodeType}");
                        node.Name = nodeName;

                        undoRedo.AddDoMethod(parent, "add_child", node);
                        undoRedo.AddDoProperty(node, "owner", GetEditedRoot());
                        undoRedo.AddDoReference(node);
                        undoRedo.AddUndoMethod(parent, "remove_child", node);

                        var alias = GetOr(operation, "alias", string.Empty).AsString();
                        if (!string.IsNullOrWhiteSpace(alias))
                            aliases[alias] = node;

                        results.Add(new Dictionary
                        {
                            { "type", operationType },
                            { "name", nodeName },
                            { "node_type", nodeType },
                            { "alias", alias },
                        });
                        break;
                    }
                case "set_property":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        if (node == null) return Error("Node not found for set_property operation.");
                        var property = GetOr(operation, "property", string.Empty).AsString();
                        var value = BridgeSerialization.UnwrapTransportValue(operation["value"]);
                        var oldValue = node.Get(property);
                        value = BridgeSerialization.CoerceToPropertyType(value, oldValue);
                        undoRedo.AddDoProperty(node, property, value);
                        undoRedo.AddUndoProperty(node, property, oldValue);
                        results.Add(new Dictionary { { "type", operationType }, { "property", property }, { "target", node.Name } });
                        break;
                    }
                case "rename":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        if (node == null) return Error("Node not found for rename operation.");
                        var newName = GetOr(operation, "new_name", string.Empty).AsString();
                        var oldName = node.Name;
                        undoRedo.AddDoProperty(node, "name", newName);
                        undoRedo.AddUndoProperty(node, "name", oldName);
                        results.Add(new Dictionary { { "type", operationType }, { "old_name", oldName }, { "new_name", newName } });
                        break;
                    }
                case "move":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        var newParent = ResolveNodeReference(operation, "new_parent_path", "parent_alias", aliases);
                        if (node == null || newParent == null) return Error("move operation requires a valid node and new parent.");
                        var oldParent = node.GetParent();
                        undoRedo.AddDoMethod(oldParent, "remove_child", node);
                        undoRedo.AddDoMethod(newParent, "add_child", node);
                        undoRedo.AddDoProperty(node, "owner", GetEditedRoot());
                        undoRedo.AddUndoMethod(newParent, "remove_child", node);
                        undoRedo.AddUndoMethod(oldParent, "add_child", node);
                        undoRedo.AddUndoProperty(node, "owner", GetEditedRoot());
                        results.Add(new Dictionary { { "type", operationType }, { "target", node.Name }, { "new_parent", newParent.Name } });
                        break;
                    }
                case "delete":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        if (node == null) return Error("Node not found for delete operation.");
                        if (node == GetEditedRoot()) return Error("Cannot delete scene root node");
                        var parent = node.GetParent();
                        undoRedo.AddDoMethod(parent, "remove_child", node);
                        undoRedo.AddUndoMethod(parent, "add_child", node);
                        undoRedo.AddUndoProperty(node, "owner", GetEditedRoot());
                        undoRedo.AddUndoReference(node);
                        results.Add(new Dictionary { { "type", operationType }, { "target", node.Name } });
                        break;
                    }
                case "attach_script":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        if (node == null) return Error("Node not found for attach_script operation.");
                        var pathError = ValidateProjectPath(GetOr(operation, "script_path", string.Empty).AsString(), out var scriptPath, "script_path");
                        if (pathError != null) return pathError;
                        var script = ResourceLoader.Load<Script>(scriptPath);
                        if (script == null) return Error($"Script not found: {scriptPath}");
                        var oldScript = node.GetScript();
                        undoRedo.AddDoMethod(node, "set_script", script);
                        undoRedo.AddUndoMethod(node, "set_script", oldScript);
                        results.Add(new Dictionary { { "type", operationType }, { "target", node.Name }, { "script_path", scriptPath } });
                        break;
                    }
                case "detach_script":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        if (node == null) return Error("Node not found for detach_script operation.");
                        var oldScript = node.GetScript();
                        undoRedo.AddDoMethod(node, "set_script", default(Variant));
                        undoRedo.AddUndoMethod(node, "set_script", oldScript);
                        results.Add(new Dictionary { { "type", operationType }, { "target", node.Name } });
                        break;
                    }
                case "connect_signal":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        var target = ResolveNodeReference(operation, "target_path", "receiver_alias", aliases);
                        if (node == null || target == null) return Error("connect_signal operation requires source and target nodes.");
                        var signalName = GetOr(operation, "signal", string.Empty).AsString();
                        var method = GetOr(operation, "method", string.Empty).AsString();
                        var callable = new Callable(target, method);
                        undoRedo.AddDoMethod(node, "connect", signalName, callable);
                        undoRedo.AddUndoMethod(node, "disconnect", signalName, callable);
                        results.Add(new Dictionary { { "type", operationType }, { "signal", signalName }, { "target", target.Name }, { "method", method } });
                        break;
                    }
                case "disconnect_signal":
                    {
                        var node = ResolveNodeReference(operation, "node_path", "target_alias", aliases);
                        var target = ResolveNodeReference(operation, "target_path", "receiver_alias", aliases);
                        if (node == null || target == null) return Error("disconnect_signal operation requires source and target nodes.");
                        var signalName = GetOr(operation, "signal", string.Empty).AsString();
                        var method = GetOr(operation, "method", string.Empty).AsString();
                        var callable = new Callable(target, method);
                        undoRedo.AddDoMethod(node, "disconnect", signalName, callable);
                        undoRedo.AddUndoMethod(node, "connect", signalName, callable);
                        results.Add(new Dictionary { { "type", operationType }, { "signal", signalName }, { "target", target.Name }, { "method", method } });
                        break;
                    }
                default:
                    return Error($"Unsupported batch operation type: {operationType}");
            }
        }

        undoRedo.CommitAction();

        var aliasPaths = new Dictionary();
        foreach (var alias in aliases)
        {
            if (GodotObject.IsInstanceValid(alias.Value))
                aliasPaths[alias.Key] = alias.Value.GetPath().ToString();
        }

        return Success(new Dictionary
        {
            { "action_name", actionName },
            { "operation_count", operations.Count },
            { "operations", results },
            { "aliases", aliasPaths },
        });
    }

    private Dictionary BuildPropertyResult(Node node, Godot.Collections.Array? requestedProperties, int offset, int limit, bool editorOnly)
    {
        var props = new Dictionary();
        var propertyOrder = new Godot.Collections.Array();
        int totalProperties = 0;
        int returnedProperties = 0;

        foreach (var propDict in node.GetPropertyList())
        {
            var propName = propDict["name"].AsString();
            var usage = propDict["usage"].AsInt32();
            if (editorOnly && (usage & (int)PropertyUsageFlags.Editor) == 0)
                continue;
            if (!IsPropertyRequested(requestedProperties, propName))
                continue;

            totalProperties++;
            if (totalProperties <= offset)
                continue;
            if (returnedProperties >= limit)
                break;

            try
            {
                props[propName] = BridgeSerialization.BuildPropertySnapshot(propName, node.Get(propName), propDict);
            }
            catch (Exception ex)
            {
                props[propName] = new Dictionary
                {
                    { "name", propName },
                    { "type", "error" },
                    { "display_value", $"<unreadable: {ex.Message}>" },
                    { "raw_value", default(Variant) },
                };
            }

            propertyOrder.Add(propName);
            returnedProperties++;
        }

        var hasMore = offset + returnedProperties < totalProperties;
        return new Dictionary
        {
            { "properties", props },
            { "property_order", propertyOrder },
            { "total_properties", totalProperties },
            { "returned_properties", returnedProperties },
            { "offset", offset },
            { "limit", limit },
            { "has_more", hasMore },
            { "next_offset", hasMore ? offset + returnedProperties : -1 },
        };
    }

    private static bool IsPropertyRequested(Godot.Collections.Array? requestedProperties, string propertyName)
    {
        if (requestedProperties == null || requestedProperties.Count == 0)
            return true;

        foreach (var requestedProperty in requestedProperties)
        {
            if (string.Equals(requestedProperty.AsString(), propertyName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static Godot.Collections.Array BuildSignalArray(Node node)
    {
        var signals = new Godot.Collections.Array();
        foreach (var sigDict in node.GetSignalList())
        {
            signals.Add(new Dictionary
            {
                { "name", sigDict["name"] },
                { "args", GetOr(sigDict, "args", new Godot.Collections.Array()) },
            });
        }

        return signals;
    }

    private Dictionary BuildInspectionTree(Node node, int maxDepth, bool includeChildProperties, int currentDepth)
    {
        var summary = BridgeSerialization.BuildNodeSummary(node, includeGroups: true);
        summary["signals"] = BuildSignalArray(node);
        if (includeChildProperties)
            summary["properties"] = BuildPropertyResult(node, null, 0, 50, editorOnly: true)["properties"];

        if (currentDepth >= maxDepth)
            return summary;

        var children = new Godot.Collections.Array();
        for (int i = 0; i < node.GetChildCount(); i++)
            children.Add(BuildInspectionTree(node.GetChild(i), maxDepth, includeChildProperties, currentDepth + 1));

        summary["children"] = children;
        return summary;
    }

    private Node? ResolveNodeReference(Dictionary operation, string pathKey, string aliasKey, System.Collections.Generic.Dictionary<string, Node> aliases)
    {
        var alias = GetOr(operation, aliasKey, string.Empty).AsString();
        if (!string.IsNullOrWhiteSpace(alias) && aliases.TryGetValue(alias, out var aliasedNode))
            return aliasedNode;

        var nodePath = GetOr(operation, pathKey, string.Empty).AsString();
        return string.IsNullOrWhiteSpace(nodePath) ? null : FindNode(nodePath);
    }
}
#endif
