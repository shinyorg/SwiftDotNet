using System.Windows.Forms;
using SwiftDotNet.Hosting;
// This file lives in the SwiftDotNet namespace, where `Form` is the *DSL* Form view (and it is sealed) —
// a simple name binds to the enclosing namespace's member before any using-imported one, so the WinForms
// type is reached through a distinctly-named alias (a same-name alias is itself CS0576).
using WinFormsForm = System.Windows.Forms.Form;

namespace SwiftDotNet;

/// <summary>
/// A ready-made Windows Forms <see cref="WinFormsForm"/> whose entire client area is a SwiftDotNet view tree
/// drawn by the Skia backend:
/// <code>
/// [STAThread]
/// static void Main()
/// {
///     ApplicationConfiguration.Initialize();
///     System.Windows.Forms.Application.Run(new SwiftDotNetSkiaForm(SwiftProgram.CreateSwiftApp()));
/// }
/// </code>
/// </summary>
public class SwiftDotNetSkiaForm : WinFormsForm
{
    /// <summary>Hosts <paramref name="root"/>, resolving injections from <paramref name="services"/>.</summary>
    public SwiftDotNetSkiaForm(View root, IServiceProvider? services = null)
    {
        Text = "SwiftDotNet";
        ClientSize = new System.Drawing.Size(440, 820);
        Surface = new SwiftDotNetSkiaControl(root, services) { Dock = DockStyle.Fill };
        Controls.Add(Surface);
        // The canvas owns keyboard input; without this the form starts with nothing focused and the
        // first keystroke goes nowhere.
        ActiveControl = Surface;
    }

    /// <summary>Hosts the app's root view and shares its DI container.</summary>
    public SwiftDotNetSkiaForm(SwiftDotNetApp app)
        : this((app ?? throw new ArgumentNullException(nameof(app))).CreateRoot(), app.Services)
    {
    }

    /// <summary>The canvas control, for reaching the bridge or the gesture router.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public SwiftDotNetSkiaControl Surface { get; }
}
