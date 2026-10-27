using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Stawn.Model;
using Stawn.Native;

namespace Stawn;

public partial class App : Application
{
    /// <summary>Where a startup failure or crash is written, so it can be reported.</summary>
    public static readonly string CrashLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STAWN CAD Pro", "crash.log");

    public App()
    {
        // An unpackaged WinUI app that throws during startup just disappears; say why instead.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail(e.ExceptionObject as Exception);
        UnhandledException += (_, e) =>
        {
            Fail(e.Exception);
            e.Handled = false;
        };
        InitializeComponent();
    }

    /// <summary>Opens the part given on the command line, otherwise the Welcome window.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Load the core first so a missing DLL is reported by name, not as a vanished window.
            StawnNative.stawn_doc_free(StawnNative.stawn_doc_new());
        }
        catch (Exception e)
        {
            Fail(e, "The STAWN CAD Pro core (stawn.dll) could not be loaded. Keep all the files from the download " +
                    "together in one folder, and use the ARM64 build on ARM PCs (Parallels on a Mac) and x64 otherwise.");
            Exit();
            return;
        }

        // --demo part | --demo sketch: opens the sample maximised (and sketches on it), for CI screenshots.
        var cli = Environment.GetCommandLineArgs();
        int demo = Array.IndexOf(cli, "--demo");
        if (demo > 0)
        {
            var window = new MainWindow(PartModel.SampleShaft());
            window.Activate();
            window.RunDemo(demo + 1 < cli.Length ? cli[demo + 1] : "part");
            return;
        }

        var path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a.EndsWith(".stprt", StringComparison.OrdinalIgnoreCase));
        if (path != null)
        {
            try
            {
                new MainWindow(PartModel.Open(path)).Activate();
                return;
            }
            catch (Exception)
            {
                // Not a readable part: start on the Welcome window instead.
            }
        }
        new MainWindow(null).Activate();
    }

    static bool failed;

    /// <summary>Writes the error to <see cref="CrashLog"/> and shows it in a message box (once).</summary>
    static void Fail(Exception? e, string? summary = null)
    {
        if (failed) return;
        failed = true;
        var text = $"{summary ?? "STAWN CAD Pro stopped because of an error."}\n\n{e}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            File.AppendAllText(CrashLog, $"[{DateTime.Now:u}] {RuntimeInformation.ProcessArchitecture} " +
                                         $"{RuntimeInformation.OSDescription}\n{text}\n\n");
            text += $"\n\nThis was saved to {CrashLog}";
        }
        catch (Exception)
        {
            // The message box below still tells the user.
        }
        MessageBoxW(IntPtr.Zero, text, "STAWN CAD Pro", 0x10 /* MB_ICONERROR */);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
