using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Stawn.Model;

namespace Stawn;

public partial class App : Application
{
    public App() => InitializeComponent();

    /// <summary>Opens the part given on the command line, otherwise the Welcome window.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
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
}
