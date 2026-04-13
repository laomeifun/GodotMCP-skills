using Godot;
using Godot.Collections;
using GDArray = Godot.Collections.Array;

namespace GodotMCP;

public static class BridgeSerialization
{
    private const int DefaultMaxDepth = 5;

    public static string GetVariantTypeName(Variant.Type type) => type switch
    {
        Variant.Type.Nil => "null",
        Variant.Type.Bool => "bool",
        Variant.Type.Int => "int",
        Variant.Type.Float => "float",
        Variant.Type.String => "string",
        Variant.Type.StringName => "string_name",
        Variant.Type.NodePath => "node_path",
        Variant.Type.Vector2 => "vector2",
        Variant.Type.Vector2I => "vector2i",
        Variant.Type.Rect2 => "rect2",
        Variant.Type.Rect2I => "rect2i",
        Variant.Type.Vector3 => "vector3",
        Variant.Type.Vector3I => "vector3i",
        Variant.Type.Vector4 => "vector4",
        Variant.Type.Vector4I => "vector4i",
        Variant.Type.Transform2D => "transform2d",
        Variant.Type.Transform3D => "transform3d",
        Variant.Type.Basis => "basis",
        Variant.Type.Quaternion => "quaternion",
        Variant.Type.Plane => "plane",
        Variant.Type.Color => "color",
        Variant.Type.Rid => "rid",
        Variant.Type.Object => "object",
        Variant.Type.Dictionary => "dictionary",
        Variant.Type.Array => "array",
        Variant.Type.Callable => "callable",
        Variant.Type.Signal => "signal",
        Variant.Type.PackedByteArray => "packed_byte_array",
        Variant.Type.PackedInt32Array => "packed_int32_array",
        Variant.Type.PackedInt64Array => "packed_int64_array",
        Variant.Type.PackedFloat32Array => "packed_float32_array",
        Variant.Type.PackedFloat64Array => "packed_float64_array",
        Variant.Type.PackedStringArray => "packed_string_array",
        Variant.Type.PackedVector2Array => "packed_vector2_array",
        Variant.Type.PackedVector3Array => "packed_vector3_array",
        Variant.Type.PackedColorArray => "packed_color_array",
        _ => type.ToString().ToLowerInvariant(),
    };

    public static Variant SerializeVariant(Variant value, int depth = 0, int maxDepth = DefaultMaxDepth)
    {
        if (depth >= maxDepth)
            return value.ToString();

        switch (value.VariantType)
        {
            case Variant.Type.Nil:
                return default(Variant);
            case Variant.Type.Bool:
                return value.AsBool();
            case Variant.Type.Int:
                return value.AsInt64();
            case Variant.Type.Float:
                return value.AsDouble();
            case Variant.Type.String:
                return value.AsString();
            case Variant.Type.StringName:
                return value.AsStringName().ToString();
            case Variant.Type.NodePath:
                return value.AsNodePath().ToString();
            case Variant.Type.Vector2:
                return SerializeVector2(value.AsVector2());
            case Variant.Type.Vector2I:
                return SerializeVector2I(value.AsVector2I());
            case Variant.Type.Rect2:
                return SerializeRect2(value.AsRect2());
            case Variant.Type.Rect2I:
                return SerializeRect2I(value.AsRect2I());
            case Variant.Type.Vector3:
                return SerializeVector3(value.AsVector3());
            case Variant.Type.Vector3I:
                return SerializeVector3I(value.AsVector3I());
            case Variant.Type.Vector4:
                return SerializeVector4(value.AsVector4());
            case Variant.Type.Vector4I:
                return SerializeVector4I(value.AsVector4I());
            case Variant.Type.Color:
                return SerializeColor(value.AsColor());
            case Variant.Type.Quaternion:
                return SerializeQuaternion(value.AsQuaternion());
            case Variant.Type.Plane:
                return SerializePlane(value.AsPlane());
            case Variant.Type.Dictionary:
                return SerializeDictionary(value.AsGodotDictionary(), depth + 1, maxDepth);
            case Variant.Type.Array:
                return SerializeArray(value.AsGodotArray(), depth + 1, maxDepth);
            case Variant.Type.Object:
                return SerializeObject(value.AsGodotObject(), depth + 1, maxDepth);
            default:
                return value.ToString();
        }
    }

    public static Dictionary BuildPropertySnapshot(string name, Variant value, Dictionary? propertyInfo = null)
    {
        var result = new Dictionary
        {
            { "name", name },
            { "type", GetVariantTypeName(value.VariantType) },
            { "display_value", value.ToString() },
            { "raw_value", SerializeVariant(value) },
        };

        if (propertyInfo != null)
        {
            if (propertyInfo.TryGetValue("usage", out var usage))
                result["usage"] = usage;
            if (propertyInfo.TryGetValue("hint", out var hint))
                result["hint"] = hint;
            if (propertyInfo.TryGetValue("hint_string", out var hintString))
                result["hint_string"] = hintString;
            if (propertyInfo.TryGetValue("class_name", out var className))
                result["class_name"] = className;
        }

        return result;
    }

    public static Dictionary BuildNodeSummary(Node? node, bool includeGroups = false)
    {
        var result = new Dictionary
        {
            { "name", node?.Name.ToString() ?? string.Empty },
            { "type", node?.GetClass() ?? string.Empty },
            { "path", node?.GetPath().ToString() ?? string.Empty },
            { "scene_file_path", node?.SceneFilePath ?? string.Empty },
            { "child_count", node?.GetChildCount() ?? 0 },
            { "instance_id", node != null ? (long)node.GetInstanceId() : 0L },
        };

        if (node?.GetOwner() != null)
            result["owner_path"] = node.GetOwner().GetPath().ToString();

        if (node != null)
        {
            var scriptVariant = node.GetScript();
            if (scriptVariant.VariantType != Variant.Type.Nil)
            {
                var script = scriptVariant.As<Script>();
                if (script != null)
                    result["script_path"] = script.ResourcePath;
            }

            if (includeGroups)
            {
                var groups = new GDArray();
                foreach (var group in node.GetGroups())
                    groups.Add(group.ToString());
                result["groups"] = groups;
            }
        }

        return result;
    }

    public static Variant UnwrapTransportValue(Variant value)
    {
        if (value.VariantType != Variant.Type.Dictionary)
            return value;

        var dict = value.AsGodotDictionary();

        // 优先使用显式包装格式 {"type": "vector3", "raw_value": {...}}
        if (dict.TryGetValue("type", out var typeVariant) && dict.TryGetValue("raw_value", out var rawValue))
            return DeserializeVariant(rawValue, typeVariant.AsString());

        // 当没有包装时，根据键名结构自动推断 Godot 类型
        var inferred = TryInferVariantFromDict(dict);
        if (inferred.HasValue)
            return inferred.Value;

        return value;
    }

    /// <summary>
    /// 根据字典键名结构自动推断 Godot 复合类型。
    /// 支持 Vector2/2I/3/3I/4/4I、Color、Quaternion、Plane、Rect2/2I、Transform2D/3D、Basis。
    /// </summary>
    private static Variant? TryInferVariantFromDict(Dictionary dict)
    {
        var keys = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in dict.Keys)
            keys.Add(key.AsString());

        try
        {
            // Color: {r, g, b} 或 {r, g, b, a}（也可单独用 html 键）
            if (keys.Contains("r") && keys.Contains("g") && keys.Contains("b"))
                return DeserializeColor(dict);

            // Plane: {normal, d}
            if (keys.Contains("normal") && keys.Contains("d") && keys.Count == 2)
            {
                var normal = DeserializeVector3(dict["normal"].AsGodotDictionary());
                return new Plane(normal, GetSingle(dict, "d"));
            }

            // Rect2 / Rect2I: {position, size}
            if (keys.Contains("position") && keys.Contains("size") && keys.Count == 2)
            {
                var posDict = dict["position"].AsGodotDictionary();
                var sizeDict = dict["size"].AsGodotDictionary();
                if (IsIntegerDict(posDict) && IsIntegerDict(sizeDict))
                    return DeserializeRect2I(dict);
                return DeserializeRect2(dict);
            }

            // Quaternion: {x, y, z, w} — 必须在 Vector4 之前检查，
            // 因为键完全相同，但 Quaternion 比 Vector4 更常见于旋转属性。
            // 这里默认推断为 Quaternion；如果需要 Vector4，用户应使用包装格式。
            if (keys.Contains("x") && keys.Contains("y") && keys.Contains("z") && keys.Contains("w"))
            {
                // 如果所有值都是整数类型，推断为 Vector4I
                if (IsIntegerDict(dict))
                    return DeserializeVector4I(dict);
                return DeserializeQuaternion(dict);
            }

            // Vector3 / Vector3I: {x, y, z}
            if (keys.Contains("x") && keys.Contains("y") && keys.Contains("z"))
            {
                if (IsIntegerDict(dict))
                    return DeserializeVector3I(dict);
                return DeserializeVector3(dict);
            }

            // Vector2 / Vector2I: {x, y}
            if (keys.Contains("x") && keys.Contains("y") && keys.Count == 2)
            {
                if (IsIntegerDict(dict))
                    return DeserializeVector2I(dict);
                return DeserializeVector2(dict);
            }
        }
        catch
        {
            // 推断失败时回退，让原始值直接传入
        }

        return null;
    }

    /// <summary>
    /// 判断字典中的数值是否全部为整数（用于区分 Vector3 和 Vector3I 等）。
    /// </summary>
    private static bool IsIntegerDict(Dictionary dict)
    {
        foreach (var key in dict.Keys)
        {
            var v = dict[key];
            if (v.VariantType == Variant.Type.Float || v.VariantType == Variant.Type.String)
                return false;
            if (v.VariantType == Variant.Type.Dictionary || v.VariantType == Variant.Type.Array)
                continue; // 跳过嵌套结构
        }
        return true;
    }

    private static Quaternion DeserializeQuaternion(Dictionary value) => new(
        GetSingle(value, "x"),
        GetSingle(value, "y"),
        GetSingle(value, "z"),
        GetSingle(value, "w")
    );

    /// <summary>
    /// 如果推断/传入的值类型与属性实际类型不匹配，尝试将值强制转换为目标类型。
    /// 例如：传入 Dictionary{"x","y","z"} 但属性是 Vector3 → 转为 Vector3。
    /// 传入 Quaternion 但属性是 Vector4 → 转为 Vector4（键结构相同但语义不同）。
    /// </summary>
    public static Variant CoerceToPropertyType(Variant value, Variant targetExample)
    {
        // 类型已经匹配，不需要转换
        if (value.VariantType == targetExample.VariantType)
            return value;

        // 如果值仍然是 Dictionary（TryInferVariantFromDict 未能推断），
        // 根据目标类型再试一次精确反序列化
        if (value.VariantType == Variant.Type.Dictionary)
        {
            var dict = value.AsGodotDictionary();
            try
            {
                return targetExample.VariantType switch
                {
                    Variant.Type.Vector2 => DeserializeVector2(dict),
                    Variant.Type.Vector2I => DeserializeVector2I(dict),
                    Variant.Type.Vector3 => DeserializeVector3(dict),
                    Variant.Type.Vector3I => DeserializeVector3I(dict),
                    Variant.Type.Vector4 => DeserializeVector4(dict),
                    Variant.Type.Vector4I => DeserializeVector4I(dict),
                    Variant.Type.Color => DeserializeColor(dict),
                    Variant.Type.Rect2 => DeserializeRect2(dict),
                    Variant.Type.Rect2I => DeserializeRect2I(dict),
                    Variant.Type.Quaternion => DeserializeQuaternion(dict),
                    _ => value,
                };
            }
            catch
            {
                return value;
            }
        }

        // 处理自动推断类型错误的情况（如 Quaternion↔Vector4）
        try
        {
            // Quaternion → Vector4
            if (value.VariantType == Variant.Type.Quaternion && targetExample.VariantType == Variant.Type.Vector4)
            {
                var q = value.AsQuaternion();
                return new Vector4(q.X, q.Y, q.Z, q.W);
            }

            // Vector4 → Quaternion
            if (value.VariantType == Variant.Type.Vector4 && targetExample.VariantType == Variant.Type.Quaternion)
            {
                var v = value.AsVector4();
                return new Quaternion(v.X, v.Y, v.Z, v.W);
            }

            // int → float
            if (value.VariantType == Variant.Type.Int && targetExample.VariantType == Variant.Type.Float)
                return value.AsDouble();

            // float → int
            if (value.VariantType == Variant.Type.Float && targetExample.VariantType == Variant.Type.Int)
                return value.AsInt64();
        }
        catch
        {
            // 转换失败时回退
        }

        return value;
    }

    private static Dictionary SerializeVector2(Vector2 value) => new()
    {
        { "x", value.X },
        { "y", value.Y },
    };

    private static Dictionary SerializeVector2I(Vector2I value) => new()
    {
        { "x", value.X },
        { "y", value.Y },
    };

    private static Dictionary SerializeRect2(Rect2 value) => new()
    {
        { "position", SerializeVector2(value.Position) },
        { "size", SerializeVector2(value.Size) },
    };

    private static Dictionary SerializeRect2I(Rect2I value) => new()
    {
        { "position", SerializeVector2I(value.Position) },
        { "size", SerializeVector2I(value.Size) },
    };

    private static Dictionary SerializeVector3(Vector3 value) => new()
    {
        { "x", value.X },
        { "y", value.Y },
        { "z", value.Z },
    };

    private static Dictionary SerializeVector3I(Vector3I value) => new()
    {
        { "x", value.X },
        { "y", value.Y },
        { "z", value.Z },
    };

    private static Dictionary SerializeVector4(Vector4 value) => new()
    {
        { "x", value.X },
        { "y", value.Y },
        { "z", value.Z },
        { "w", value.W },
    };

    private static Dictionary SerializeVector4I(Vector4I value) => new()
    {
        { "x", value.X },
        { "y", value.Y },
        { "z", value.Z },
        { "w", value.W },
    };

    private static Dictionary SerializeColor(Color value) => new()
    {
        { "r", value.R },
        { "g", value.G },
        { "b", value.B },
        { "a", value.A },
        { "html", value.ToHtml() },
    };

    private static Dictionary SerializeQuaternion(Quaternion value) => new()
    {
        { "x", value.X },
        { "y", value.Y },
        { "z", value.Z },
        { "w", value.W },
    };

    private static Dictionary SerializePlane(Plane value) => new()
    {
        { "normal", SerializeVector3(value.Normal) },
        { "d", value.D },
    };

    private static Dictionary SerializeDictionary(Dictionary value, int depth, int maxDepth)
    {
        var result = new Dictionary();
        foreach (var key in value.Keys)
            result[key.ToString()] = SerializeVariant(value[key], depth, maxDepth);
        return result;
    }

    private static GDArray SerializeArray(GDArray value, int depth, int maxDepth)
    {
        var result = new GDArray();
        foreach (var item in value)
            result.Add(SerializeVariant(item, depth, maxDepth));
        return result;
    }

    private static Variant SerializeObject(GodotObject? value, int depth, int maxDepth)
    {
        if (value == null)
            return default(Variant);

        if (value is Node node)
        {
            return new Dictionary
            {
                { "kind", "node" },
                { "summary", BuildNodeSummary(node, includeGroups: true) },
            };
        }

        if (value is Resource resource)
        {
            return new Dictionary
            {
                { "kind", "resource" },
                { "type", resource.GetClass() },
                { "resource_path", resource.ResourcePath },
                { "resource_name", resource.ResourceName },
                { "id", (long)resource.GetInstanceId() },
            };
        }

        return new Dictionary
        {
            { "kind", "object" },
            { "type", value.GetClass() },
            { "id", (long)value.GetInstanceId() },
            { "display_value", value.ToString() },
        };
    }

    private static Variant DeserializeVariant(Variant rawValue, string typeName)
    {
        switch (typeName)
        {
            case "bool":
                return rawValue.AsBool();
            case "int":
                return rawValue.AsInt64();
            case "float":
                return rawValue.AsDouble();
            case "string":
            case "string_name":
                return rawValue.AsString();
            case "node_path":
                return new NodePath(rawValue.AsString());
            case "vector2":
                return DeserializeVector2(rawValue.AsGodotDictionary());
            case "vector2i":
                return DeserializeVector2I(rawValue.AsGodotDictionary());
            case "vector3":
                return DeserializeVector3(rawValue.AsGodotDictionary());
            case "vector3i":
                return DeserializeVector3I(rawValue.AsGodotDictionary());
            case "vector4":
                return DeserializeVector4(rawValue.AsGodotDictionary());
            case "vector4i":
                return DeserializeVector4I(rawValue.AsGodotDictionary());
            case "rect2":
                return DeserializeRect2(rawValue.AsGodotDictionary());
            case "rect2i":
                return DeserializeRect2I(rawValue.AsGodotDictionary());
            case "color":
                return DeserializeColor(rawValue.AsGodotDictionary());
            case "array":
                return rawValue.AsGodotArray();
            case "dictionary":
                return rawValue.AsGodotDictionary();
            default:
                return rawValue;
        }
    }

    private static Vector2 DeserializeVector2(Dictionary value) => new(
        GetSingle(value, "x"),
        GetSingle(value, "y")
    );

    private static Vector2I DeserializeVector2I(Dictionary value) => new(
        GetInt(value, "x"),
        GetInt(value, "y")
    );

    private static Rect2 DeserializeRect2(Dictionary value) => new(
        DeserializeVector2(value["position"].AsGodotDictionary()),
        DeserializeVector2(value["size"].AsGodotDictionary())
    );

    private static Rect2I DeserializeRect2I(Dictionary value) => new(
        DeserializeVector2I(value["position"].AsGodotDictionary()),
        DeserializeVector2I(value["size"].AsGodotDictionary())
    );

    private static Vector3 DeserializeVector3(Dictionary value) => new(
        GetSingle(value, "x"),
        GetSingle(value, "y"),
        GetSingle(value, "z")
    );

    private static Vector3I DeserializeVector3I(Dictionary value) => new(
        GetInt(value, "x"),
        GetInt(value, "y"),
        GetInt(value, "z")
    );

    private static Vector4 DeserializeVector4(Dictionary value) => new(
        GetSingle(value, "x"),
        GetSingle(value, "y"),
        GetSingle(value, "z"),
        GetSingle(value, "w")
    );

    private static Vector4I DeserializeVector4I(Dictionary value) => new(
        GetInt(value, "x"),
        GetInt(value, "y"),
        GetInt(value, "z"),
        GetInt(value, "w")
    );

    private static Color DeserializeColor(Dictionary value) => new(
        GetSingle(value, "r"),
        GetSingle(value, "g"),
        GetSingle(value, "b"),
        value.ContainsKey("a") ? GetSingle(value, "a") : 1.0f
    );

    private static float GetSingle(Dictionary dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return 0;
        return (float)value.AsDouble();
    }

    private static int GetInt(Dictionary dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return 0;
        return value.AsInt32();
    }
}