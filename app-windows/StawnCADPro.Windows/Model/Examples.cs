namespace Stawn.Model;

/// <summary>
/// Starting scripts for the Run Commands dialog. Until the interactive sketcher lands, sketch
/// and feature tools open this dialog with a ready example of the command they map to.
/// </summary>
public static class Examples
{
    public const string Sketch =
        "{\"op\": \"create_sketch\", \"plane\": \"Front\"}\n" +
        "{\"op\": \"draw_rectangle\", \"x1\": -20, \"y1\": -10, \"x2\": 20, \"y2\": 10}\n" +
        "{\"op\": \"close_sketch\"}";

    public const string Extrude =
        "{\"op\": \"create_sketch\", \"plane\": \"Front\"}\n" +
        "{\"op\": \"draw_circle\", \"cx\": 0, \"cy\": 0, \"r\": 30}\n" +
        "{\"op\": \"close_sketch\"}\n" +
        "{\"op\": \"extrude\", \"depth\": 15}";

    public const string Revolve =
        "{\"op\": \"create_sketch\", \"plane\": \"Front\"}\n" +
        "{\"op\": \"draw_centerline\", \"x1\": 0, \"y1\": 0, \"x2\": 50, \"y2\": 0}\n" +
        "{\"op\": \"draw_rectangle\", \"x1\": 0, \"y1\": 0, \"x2\": 50, \"y2\": 4}\n" +
        "{\"op\": \"close_sketch\"}\n" +
        "{\"op\": \"revolve\", \"angle\": 360}";

    public const string CutExtrude =
        "{\"op\": \"create_sketch\", \"plane\": \"Front\"}\n" +
        "{\"op\": \"draw_circle\", \"cx\": 0, \"cy\": 0, \"r\": 5}\n" +
        "{\"op\": \"close_sketch\"}\n" +
        "{\"op\": \"cut_extrude\", \"depth\": 40, \"mid_plane\": true}";

    public const string CutRevolve =
        "{\"op\": \"create_sketch\", \"plane\": \"Front\"}\n" +
        "{\"op\": \"draw_centerline\", \"x1\": 0, \"y1\": 0, \"x2\": 50, \"y2\": 0}\n" +
        "{\"op\": \"draw_rectangle\", \"x1\": 20, \"y1\": 3, \"x2\": 22, \"y2\": 5}\n" +
        "{\"op\": \"close_sketch\"}\n" +
        "{\"op\": \"cut_revolve\", \"angle\": 360}";
}
