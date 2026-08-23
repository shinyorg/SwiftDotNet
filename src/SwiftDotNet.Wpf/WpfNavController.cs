using System.Windows;
using System.Windows.Controls;
// Core declares `Grid` and `Button` in this same namespace — see the note in WpfStyle.cs.
using WpfButton = System.Windows.Controls.Button;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace SwiftDotNet;

/// <summary>Lightweight WPF navigation stack (header + content) for NavigationStack/Link.</summary>
sealed class WpfNavController
{
    ContentControl _content = null!;
    TextBlock _title = null!;
    WpfButton _back = null!;
    readonly List<(FrameworkElement element, string title)> _stack = new();

    public FrameworkElement Build(FrameworkElement root)
    {
        var grid = new WpfGrid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        _back = new WpfButton { Content = "‹ Back", Visibility = Visibility.Collapsed, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
        _back.Click += (_, _) => Pop();
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = WpfVerticalAlignment.Center };
        header.Children.Add(_back);
        header.Children.Add(_title);
        WpfGrid.SetRow(header, 0);
        grid.Children.Add(header);

        _content = new ContentControl
        {
            Content = root,
            HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            VerticalContentAlignment = WpfVerticalAlignment.Stretch,
        };
        WpfGrid.SetRow(_content, 1);
        grid.Children.Add(_content);

        _stack.Add((root, ""));
        return grid;
    }

    public void Push(FrameworkElement destination, string title)
    {
        // The destination was built as a child of the NavigationLink and may still be parented there;
        // WPF forbids two logical parents, so it is unhooked before the ContentControl adopts it.
        Detach(destination);
        _content.Content = destination;
        _stack.Add((destination, title));
        _title.Text = title;
        _back.Visibility = Visibility.Visible;
    }

    void Pop()
    {
        if (_stack.Count <= 1) return;
        _stack.RemoveAt(_stack.Count - 1);
        var prev = _stack[^1];
        _content.Content = null;         // release the popped page before re-adopting the previous one
        Detach(prev.element);
        _content.Content = prev.element;
        _title.Text = prev.title;
        _back.Visibility = _stack.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Border border: border.Child = null; break;
            case ContentControl cc when ReferenceEquals(cc.Content, element): cc.Content = null; break;
        }
    }
}
