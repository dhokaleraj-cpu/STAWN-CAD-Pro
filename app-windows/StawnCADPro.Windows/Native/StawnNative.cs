using System;
using System.Runtime.InteropServices;

namespace Stawn.Native;

/// <summary>P/Invoke declarations for core/capi/stawn_c.h, exported from stawn.dll.</summary>
public static unsafe class StawnNative
{
    private const string Lib = "stawn";

    [StructLayout(LayoutKind.Sequential)]
    public struct Mesh
    {
        public float* Positions;
        public float* Normals;
        public uint* Indices;
        public int* TriangleFace;
        public float* EdgeLines;
        public int* SegmentEdge;
        public int VertexCount;
        public int TriangleCount;
        public int SegmentCount;
        public fixed float Bounds[6];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MassProperties
    {
        public double Volume, Area, Mass, Cx, Cy, Cz;
    }

    [DllImport(Lib)] public static extern IntPtr stawn_doc_new();
    [DllImport(Lib)] public static extern IntPtr stawn_doc_new_sample_shaft();
    [DllImport(Lib)] public static extern IntPtr stawn_doc_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr error);
    [DllImport(Lib)] public static extern IntPtr stawn_doc_from_json([MarshalAs(UnmanagedType.LPUTF8Str)] string json, out IntPtr error);
    [DllImport(Lib)] public static extern void stawn_doc_free(IntPtr doc);
    [DllImport(Lib)] public static extern int stawn_doc_save(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Lib)] public static extern IntPtr stawn_doc_json(IntPtr doc);
    [DllImport(Lib)] public static extern int stawn_doc_set_json(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string json, out IntPtr error);
    [DllImport(Lib)] public static extern int stawn_doc_rebuild(IntPtr doc, out IntPtr errors);

    [DllImport(Lib)] public static extern int stawn_doc_set_active_configuration(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] public static extern int stawn_doc_set_dimension(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string fullName, double value, int thisConfigurationOnly);
    [DllImport(Lib)] public static extern int stawn_doc_get_dimension(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string fullName, out double value);
    [DllImport(Lib)] public static extern IntPtr stawn_doc_resolved_properties(IntPtr doc);
    [DllImport(Lib)] public static extern MassProperties stawn_doc_mass_properties(IntPtr doc);

    [DllImport(Lib)] public static extern Mesh* stawn_doc_mesh(IntPtr doc, double deflection);
    [DllImport(Lib)] public static extern void stawn_mesh_free(Mesh* mesh);

    [DllImport(Lib)] public static extern int stawn_doc_import_step(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr error);
    [DllImport(Lib)] public static extern int stawn_doc_export_step(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] public static extern int stawn_doc_export_stl(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] public static extern int stawn_doc_export_drawing(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string paper,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string format, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] public static extern IntPtr stawn_doc_drawing_svg(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string paper);

    [DllImport(Lib)] public static extern IntPtr stawn_sketch_solve([MarshalAs(UnmanagedType.LPUTF8Str)] string sketchJson);
    [DllImport(Lib)] public static extern IntPtr stawn_material_library();
    [DllImport(Lib)] public static extern IntPtr stawn_doc_execute(IntPtr doc, [MarshalAs(UnmanagedType.LPUTF8Str)] string commandJson);
    [DllImport(Lib)] public static extern IntPtr stawn_commands_describe();

    [DllImport(Lib)] public static extern void stawn_free_string(IntPtr s);

    /// <summary>Copies a string returned by the core and releases it. Returns null for NULL.</summary>
    public static string? Take(IntPtr s)
    {
        if (s == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(s); }
        finally { stawn_free_string(s); }
    }
}
