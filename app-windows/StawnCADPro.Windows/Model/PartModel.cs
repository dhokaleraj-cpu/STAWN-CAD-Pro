using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stawn.Native;

namespace Stawn.Model;

/// <summary>
/// C# side of a part document, the counterpart of PartModel.swift. Holds the core document and
/// a JSON mirror of it that the panels edit. Every edit writes the JSON back to the core and
/// rebuilds, the same way a change in the PropertyManager triggers a rebuild.
/// </summary>
public sealed class PartModel : IDisposable
{
    private IntPtr _handle;

    public JsonObject Json { get; private set; } = new();
    public MeshBuffers? Mesh { get; private set; }
    public IReadOnlyList<string> RebuildErrors { get; private set; } = Array.Empty<string>();
    public StawnNative.MassProperties Mass { get; private set; }
    public IReadOnlyDictionary<string, string> ResolvedProperties { get; private set; } = new Dictionary<string, string>();
    public string? SelectedFeature { get; private set; }

    /// <summary>Path of the .stprt file, or null for a new unsaved part.</summary>
    public string? FilePath { get; set; }
    public bool IsDirty { get; private set; }

    /// <summary>Raised after every rebuild so the window can refresh its panels.</summary>
    public event Action? Changed;
    /// <summary>Raised when the selected feature changes without a rebuild.</summary>
    public event Action? SelectionChanged;

    public void Select(string? feature)
    {
        if (feature == SelectedFeature) return;
        SelectedFeature = feature;
        SelectionChanged?.Invoke();
    }

    private PartModel(IntPtr handle)
    {
        _handle = handle;
        Refresh();
    }

    public static PartModel SampleShaft() => new(StawnNative.stawn_doc_new_sample_shaft());
    public static PartModel Empty() => new(StawnNative.stawn_doc_new());

    public static PartModel Open(string path)
    {
        var h = StawnNative.stawn_doc_open(path, out var error);
        if (h == IntPtr.Zero) throw new InvalidOperationException(StawnNative.Take(error) ?? "Could not open the part.");
        return new PartModel(h) { FilePath = path };
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) StawnNative.stawn_doc_free(_handle);
        _handle = IntPtr.Zero;
    }

    // Reading

    public string Name => Json["name"]?.GetValue<string>() ?? "Part";
    public JsonArray Features => Json["features"] as JsonArray ?? new JsonArray();
    public JsonArray Configurations => Json["configurations"] as JsonArray ?? new JsonArray();
    public string ActiveConfiguration => Json["activeConfiguration"]?.GetValue<string>() ?? "Default";

    public IReadOnlyDictionary<string, string> Properties =>
        (Json["properties"] as JsonObject)?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "") ?? new Dictionary<string, string>();

    public (string Name, double Density) Material
    {
        get
        {
            var m = Json["material"] as JsonObject;
            return (m?["name"]?.GetValue<string>() ?? "", m?["density"]?.GetValue<double>() ?? 7850);
        }
    }

    public IEnumerable<(string Name, string Type)> FeatureList =>
        Features.OfType<JsonObject>().Select(f => (f["name"]?.GetValue<string>() ?? "?", f["type"]?.GetValue<string>() ?? ""));

    private JsonObject? ActiveConfigurationNode =>
        Configurations.OfType<JsonObject>().FirstOrDefault(c => c["name"]?.GetValue<string>() == ActiveConfiguration);

    public bool IsSuppressed(string feature) =>
        (ActiveConfigurationNode?["suppressed"] as JsonArray)?.Any(n => n?.GetValue<string>() == feature) ?? false;

    public string? ErrorFor(string feature) =>
        RebuildErrors.FirstOrDefault(e => e.StartsWith(feature + ": "))?[(feature.Length + 2)..];

    /// <summary>Driving dimensions of one feature, e.g. ("D1@Sketch1", 46.9).</summary>
    public List<(string Name, double Value)> Dimensions(string feature)
    {
        var f = Features.OfType<JsonObject>().FirstOrDefault(x => x["name"]?.GetValue<string>() == feature);
        if (f == null) return new();
        var names = new List<string>();
        if (f["type"]?.GetValue<string>() == "Sketch")
        {
            foreach (var c in (f["sketch"]?["constraints"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                if (c["name"]?.GetValue<string>() is { } n) names.Add($"{n}@{feature}");
        }
        else
        {
            names.Add($"D1@{feature}");
        }
        var result = new List<(string, double)>();
        foreach (var n in names)
            if (StawnNative.stawn_doc_get_dimension(_handle, n, out var v) != 0) result.Add((n, v));
        return result;
    }

    public string FileText() => StawnNative.Take(StawnNative.stawn_doc_json(_handle)) ?? "{}";

    // Editing

    public void SetDimension(string name, double value, bool thisConfigurationOnly = false)
    {
        if (StawnNative.stawn_doc_set_dimension(_handle, name, value, thisConfigurationOnly ? 1 : 0) == 0) return;
        RebuildAndNotify();
    }

    public void Activate(string configuration)
    {
        if (StawnNative.stawn_doc_set_active_configuration(_handle, configuration) == 0) return;
        RebuildAndNotify();
    }

    public void SetSuppressed(string feature, bool suppressed) => Edit(j =>
    {
        var active = ConfigurationNode(j, j["activeConfiguration"]?.GetValue<string>());
        if (active == null) return;
        var set = new SortedSet<string>((active["suppressed"] as JsonArray ?? new JsonArray()).Select(n => n!.GetValue<string>()));
        if (suppressed) set.Add(feature); else set.Remove(feature);
        active["suppressed"] = new JsonArray(set.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
    });

    public void AddConfiguration(string name, bool copyFromActive = true)
    {
        if (string.IsNullOrWhiteSpace(name) || ConfigurationNode(Json, name) != null) return;
        Edit(j =>
        {
            var configs = (JsonArray)j["configurations"]!;
            var active = ConfigurationNode(j, j["activeConfiguration"]?.GetValue<string>());
            var c = copyFromActive && active != null
                ? (JsonObject)active.DeepClone()
                : new JsonObject { ["suppressed"] = new JsonArray(), ["dimensions"] = new JsonObject(), ["properties"] = new JsonObject() };
            c["name"] = name;
            c["description"] = name;
            configs.Add(c);
            j["activeConfiguration"] = name;
        });
    }

    public void UpdateConfiguration(string name, string newName, string description, string bomPartNumber) => Edit(j =>
    {
        var c = ConfigurationNode(j, name);
        if (c == null) return;
        if (!string.IsNullOrWhiteSpace(newName) && newName != name && ConfigurationNode(j, newName) == null)
        {
            c["name"] = newName;
            if (j["activeConfiguration"]?.GetValue<string>() == name) j["activeConfiguration"] = newName;
        }
        c["description"] = description;
        c["bomPartNumber"] = bomPartNumber;
    });

    public void DeleteConfiguration(string name)
    {
        if (Configurations.Count <= 1 || name == ActiveConfiguration) return;
        Edit(j =>
        {
            var configs = (JsonArray)j["configurations"]!;
            var c = ConfigurationNode(j, name);
            if (c != null) configs.Remove(c);
        });
    }

    public void SetProperty(string key, string value, bool configurationSpecific = false) => Edit(j =>
    {
        JsonObject? target = configurationSpecific
            ? ConfigurationNode(j, j["activeConfiguration"]?.GetValue<string>())
            : j;
        if (target == null) return;
        if (target["properties"] is not JsonObject p) target["properties"] = p = new JsonObject();
        p[key] = value;
    });

    public void SetMaterial(string name, double density) =>
        Edit(j => j["material"] = new JsonObject { ["name"] = name, ["density"] = density });

    public void RenameFeature(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(to) || FeatureList.Any(f => f.Name == to)) return;
        Edit(j =>
        {
            foreach (var f in ((JsonArray)j["features"]!).OfType<JsonObject>())
            {
                if (f["name"]?.GetValue<string>() == from) f["name"] = to;
                if (f["sketchName"]?.GetValue<string>() == from) f["sketchName"] = to;
            }
            foreach (var c in ((JsonArray)j["configurations"]!).OfType<JsonObject>())
            {
                if (c["dimensions"] is JsonObject dims)
                {
                    var renamed = new JsonObject();
                    foreach (var (k, v) in dims)
                        renamed[k.EndsWith("@" + from) ? k[..^from.Length] + to : k] = v?.DeepClone();
                    c["dimensions"] = renamed;
                }
                if (c["suppressed"] is JsonArray s)
                    c["suppressed"] = new JsonArray(s.Select(n => n!.GetValue<string>()).Select(n => (JsonNode)JsonValue.Create(n == from ? to : n)!).ToArray());
            }
        });
        if (SelectedFeature == from) SelectedFeature = to;
    }

    /// <summary>Runs commands from the command API (docs/COMMANDS.md) and rebuilds.</summary>
    public string Execute(string commandJson)
    {
        var result = StawnNative.Take(StawnNative.stawn_doc_execute(_handle, commandJson)) ?? "{}";
        RebuildAndNotify();
        return result;
    }

    // Files and output

    public bool Save(string path)
    {
        if (StawnNative.stawn_doc_save(_handle, path) == 0) return false;
        FilePath = path;
        IsDirty = false;
        Changed?.Invoke();
        return true;
    }

    public string? ImportStep(string path)
    {
        if (StawnNative.stawn_doc_import_step(_handle, path, out var error) == 0)
            return StawnNative.Take(error) ?? "Import failed";
        RebuildAndNotify();
        return null;
    }

    public bool ExportStep(string path) => StawnNative.stawn_doc_export_step(_handle, path) != 0;
    public bool ExportStl(string path) => StawnNative.stawn_doc_export_stl(_handle, path) != 0;
    public bool ExportDrawing(string path, string paper, string format) =>
        StawnNative.stawn_doc_export_drawing(_handle, paper, format, path) != 0;
    public string DrawingSvg(string paper) => StawnNative.Take(StawnNative.stawn_doc_drawing_svg(_handle, paper)) ?? "";

    public static List<(string Name, double Density)> MaterialLibrary()
    {
        var text = StawnNative.Take(StawnNative.stawn_material_library()) ?? "[]";
        return (JsonNode.Parse(text) as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(m => (m["name"]?.GetValue<string>() ?? "", m["density"]?.GetValue<double>() ?? 0)).ToList();
    }

    // Internals

    private static JsonObject? ConfigurationNode(JsonObject j, string? name) =>
        (j["configurations"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(c => c["name"]?.GetValue<string>() == name);

    private void Edit(Action<JsonObject> change)
    {
        var j = (JsonObject)Json.DeepClone();
        change(j);
        if (StawnNative.stawn_doc_set_json(_handle, j.ToJsonString(), out var error) == 0)
        {
            StawnNative.Take(error);
            return;
        }
        RebuildAndNotify();
    }

    private void RebuildAndNotify()
    {
        IsDirty = true;
        Refresh();
    }

    public void Refresh()
    {
        StawnNative.stawn_doc_rebuild(_handle, out var errors);
        var errorText = StawnNative.Take(errors);
        RebuildErrors = errorText == null ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(errorText) ?? Array.Empty<string>();
        Json = JsonNode.Parse(FileText()) as JsonObject ?? new JsonObject();
        Mass = StawnNative.stawn_doc_mass_properties(_handle);
        var props = StawnNative.Take(StawnNative.stawn_doc_resolved_properties(_handle));
        ResolvedProperties = props == null ? new Dictionary<string, string>()
            : (JsonNode.Parse(props) as JsonObject)?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "") ?? new Dictionary<string, string>();
        Mesh = MeshBuffers.From(_handle);
        Changed?.Invoke();
    }
}

/// <summary>Triangles and edge lines copied out of the core for the viewport.</summary>
public sealed class MeshBuffers
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    public required uint[] Indices { get; init; }
    public required Vector3[] EdgeLines { get; init; }  // pairs of points
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }

    internal static unsafe MeshBuffers? From(IntPtr handle)
    {
        var m = StawnNative.stawn_doc_mesh(handle, 0.01);
        if (m == null) return null;
        try
        {
            if (m->TriangleCount <= 0) return null;
            int v = m->VertexCount, t = m->TriangleCount, s = m->SegmentCount;
            var positions = new Vector3[v];
            var normals = new Vector3[v];
            for (int i = 0; i < v; i++)
            {
                positions[i] = new Vector3(m->Positions[3 * i], m->Positions[3 * i + 1], m->Positions[3 * i + 2]);
                normals[i] = new Vector3(m->Normals[3 * i], m->Normals[3 * i + 1], m->Normals[3 * i + 2]);
            }
            var indices = new uint[t * 3];
            for (int i = 0; i < indices.Length; i++) indices[i] = m->Indices[i];
            var edges = new Vector3[s * 2];
            for (int i = 0; i < edges.Length; i++)
                edges[i] = new Vector3(m->EdgeLines[3 * i], m->EdgeLines[3 * i + 1], m->EdgeLines[3 * i + 2]);
            return new MeshBuffers
            {
                Positions = positions,
                Normals = normals,
                Indices = indices,
                EdgeLines = edges,
                Min = new Vector3(m->Bounds[0], m->Bounds[1], m->Bounds[2]),
                Max = new Vector3(m->Bounds[3], m->Bounds[4], m->Bounds[5]),
            };
        }
        finally
        {
            StawnNative.stawn_mesh_free(m);
        }
    }
}
