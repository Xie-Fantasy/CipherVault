// Lucide icons converted to WPF Geometry. ISC License. https://lucide.dev

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CipherVault.UI.Helper;

/// <summary>
/// Maps icon names to WPF Geometry objects. Populated from Lucide SVG icons.
/// </summary>
public static class Icon
{
    private static readonly Dictionary<string, Geometry> Map = new(StringComparer.OrdinalIgnoreCase);

    public const string AlertTriangle = "AlertTriangle";
    public const string ArrowUpDown = "ArrowUpDown";
    public const string Check = "Check";
    public const string CheckCircle = "CheckCircle";
    public const string Clock = "Clock";
    public const string Copy = "Copy";
    public const string Download = "Download";
    public const string ExternalLink = "ExternalLink";
    public const string Eye = "Eye";
    public const string EyeOff = "EyeOff";
    public const string FileText = "FileText";
    public const string FileUp = "FileUp";
    public const string Fingerprint = "Fingerprint";
    public const string Folder = "Folder";
    public const string HardDrive = "HardDrive";
    public const string History = "History";
    public const string Info = "Info";
    public const string Keyboard = "Keyboard";
    public const string Lock = "Lock";
    public const string LogOut = "LogOut";
    public const string Minus = "Minus";
    public const string Palette = "Palette";
    public const string Pencil = "Pencil";
    public const string Plus = "Plus";
    public const string Search = "Search";
    public const string Settings = "Settings";
    public const string Shield = "Shield";
    public const string Square = "Square";
    public const string Star = "Star";
    public const string Trash2 = "Trash2";
    public const string Undo2 = "Undo2";
    public const string Unlock = "Unlock";
    public const string Upload = "Upload";
    public const string User = "User";
    public const string X = "X";

    private static readonly Dictionary<string, string> SvgPaths = new()
    {
        ["AlertTriangle"] = "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9v4 M12 17h.01",
        ["ArrowUpDown"] = "m21 16-4 4-4-4 M17 20V4 m3 8 4-4 4 4 M7 4v16",
        ["Check"] = "M20 6 9 17l-5-5",
        ["CheckCircle"] = "M2,12 A10,10 0 1 1 22,12 A10,10 0 1 1 2,12Z m9 12 2 2 4-4",
        ["Clock"] = "M2,12 A10,10 0 1 1 22,12 A10,10 0 1 1 2,12Z M12 6v6l4 2",
        ["Copy"] = "M10,8 L20,8 A2,2 0 0 1 22,10 L22,20 A2,2 0 0 1 20,22 L10,22 A2,2 0 0 1 8,20 L8,10 A2,2 0 0 1 10,8Z M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",
        ["Download"] = "M12 15V3 M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 m7 10 5 5 5-5",
        ["ExternalLink"] = "M15 3h6v6 M10 14 21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
        ["Eye"] = "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0 M9,12 A3,3 0 1 1 15,12 A3,3 0 1 1 9,12Z",
        ["EyeOff"] = "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49 M14.084 14.158a3 3 0 0 1-4.242-4.242 M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143 m2 2 20 20",
        ["FileText"] = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z M14 2v5a1 1 0 0 0 1 1h5 M10 9H8 M16 13H8 M16 17H8",
        ["FileUp"] = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z M14 2v5a1 1 0 0 0 1 1h5 M12 12v6 m15 15-3-3-3 3",
        ["Fingerprint"] = "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M8 14s1.5 2 4 2 4-2 4-2 M9 9h.01 M15 9h.01",
        ["Folder"] = "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        ["HardDrive"] = "M10 16h.01 M2.212 11.577a2 2 0 0 0-.212.896V18a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-5.527a2 2 0 0 0-.212-.896L18.55 5.11A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z M21.946 12.013H2.054 M6 16h.01",
        ["History"] = "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M12 7v5l4 2",
        ["Info"] = "M2,12 A10,10 0 1 1 22,12 A10,10 0 1 1 2,12Z M12 16v-4 M12 8h.01",
        ["Keyboard"] = "M10 8h.01 M12 12h.01 M14 8h.01 M16 12h.01 M18 8h.01 M6 8h.01 M7 16h10 M8 12h.01 M4,4 L20,4 A2,2 0 0 1 22,6 L22,18 A2,2 0 0 1 20,20 L4,20 A2,2 0 0 1 2,18 L2,6 A2,2 0 0 1 4,4Z",
        ["Lock"] = "M5,11 L19,11 A2,2 0 0 1 21,13 L21,20 A2,2 0 0 1 19,22 L5,22 A2,2 0 0 1 3,20 L3,13 A2,2 0 0 1 5,11Z M7 11V7a5 5 0 0 1 10 0v4",
        ["LogOut"] = "m16 17 5-5-5-5 M21 12H9 M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4",
        ["Minus"] = "M5 12h14",
        ["Palette"] = "M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z M13,6.5 A0.5,0.5 0 1 1 14,6.5 A0.5,0.5 0 1 1 13,6.5Z M17,10.5 A0.5,0.5 0 1 1 18,10.5 A0.5,0.5 0 1 1 17,10.5Z M6,12.5 A0.5,0.5 0 1 1 7,12.5 A0.5,0.5 0 1 1 6,12.5Z M8,7.5 A0.5,0.5 0 1 1 9,7.5 A0.5,0.5 0 1 1 8,7.5Z",
        ["Pencil"] = "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z m15 5 4 4",
        ["Plus"] = "M5 12h14 M12 5v14",
        ["Search"] = "m21 21-4.34-4.34 M3,11 A8,8 0 1 1 19,11 A8,8 0 1 1 3,11Z",
        ["Settings"] = "M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915 M9,12 A3,3 0 1 1 15,12 A3,3 0 1 1 9,12Z",
        ["Shield"] = "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
        ["Square"] = "M5,3 L19,3 A2,2 0 0 1 21,5 L21,19 A2,2 0 0 1 19,21 L5,21 A2,2 0 0 1 3,19 L3,5 A2,2 0 0 1 5,3Z",
        ["Star"] = "M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z",
        ["Trash2"] = "M10 11v6 M14 11v6 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
        ["Undo2"] = "M9 14 4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11",
        ["Unlock"] = "M5,11 L19,11 A2,2 0 0 1 21,13 L21,20 A2,2 0 0 1 19,22 L5,22 A2,2 0 0 1 3,20 L3,13 A2,2 0 0 1 5,11Z M7 11V7a5 5 0 0 1 9.9-1",
        ["Upload"] = "M12 3v12 m17 8-5-5-5 5 M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4",
        ["User"] = "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2 M8,7 A4,4 0 1 1 16,7 A4,4 0 1 1 8,7Z",
        ["X"] = "M18 6 6 18 m6 6 12 12",
    };

    static Icon()
    {
        foreach (var (name, pathData) in SvgPaths)
        {
            // Lucide icons often consist of several independent SVG <path> nodes.
            // Their `d` attributes can start with a relative `m`, whose origin must be
            // (0,0). Concatenating those attributes into one Geometry makes every later
            // relative path continue from the preceding endpoint and produces the stray
            // strokes seen in toolbar icons. Parse each original subpath independently.
            string[] subpaths = pathData
                .Replace(" M", "|M", StringComparison.Ordinal)
                .Replace(" m", "|m", StringComparison.Ordinal)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (subpaths.Length == 1)
            {
                var geometry = Geometry.Parse(subpaths[0]);
                geometry.Freeze();
                Map[name] = geometry;
                continue;
            }

            var group = new GeometryGroup();
            foreach (string subpath in subpaths)
                group.Children.Add(Geometry.Parse(subpath));
            group.Freeze();
            Map[name] = group;
        }
    }

    public static IReadOnlyCollection<string> Names => SvgPaths.Keys;

    public static Geometry? Get(string name)
        => Map.TryGetValue(name, out var g) ? g : null;
}

/// <summary>
/// A lightweight WPF icon control that renders Lucide SVG geometry.
/// Usage: &lt;local:AppIcon Icon="Lock" Size="16" /&gt;
/// Color is inherited from the parent control's Foreground property.
/// </summary>
public class AppIcon : Canvas
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(AppIcon),
            new PropertyMetadata(null, OnPropertyChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(AppIcon),
            new PropertyMetadata(16.0, OnPropertyChanged));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(System.Windows.Media.Brush), typeof(AppIcon),
            new PropertyMetadata(null, OnPropertyChanged));

    public static readonly DependencyProperty IsFilledProperty =
        DependencyProperty.Register(nameof(IsFilled), typeof(bool), typeof(AppIcon),
            new PropertyMetadata(false, OnPropertyChanged));

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public System.Windows.Media.Brush? Foreground
    {
        get => (System.Windows.Media.Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AppIcon icon) icon.Render();
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Render();
    }

    private void Render()
    {
        Children.Clear();

        // Canvas does not include positioned children in its desired size. Without
        // an explicit size, horizontal panels place the following label on top of
        // the icon even though the icon itself is still painted.
        Width = Size;
        Height = Size;

        if (string.IsNullOrEmpty(Icon)) return;

        var geometry = CipherVault.UI.Helper.Icon.Get(Icon);
        if (geometry == null) return;

        var path = new Path
        {
            Data = geometry,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeDashCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        // Use explicit Foreground if set, otherwise inherit from nearest Control ancestor
        if (Foreground != null)
        {
            path.Stroke = Foreground;
            if (IsFilled) path.Fill = Foreground;
        }
        else
        {
            path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Control), 1)
            });
            if (IsFilled)
            {
                path.SetBinding(Shape.FillProperty, new System.Windows.Data.Binding("Foreground")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Control), 1)
                });
            }
        }

        var viewbox = new Viewbox
        {
            Width = Size,
            Height = Size,
            Stretch = Stretch.Uniform,
            Child = new Canvas { Width = 24, Height = 24, Children = { path } }
        };

        Children.Add(viewbox);
    }
}
