using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SchoolBell.App;

/// <summary>Кольори та фабрики елементів інтерфейсу (інтерфейс зібрано в коді, без XAML).</summary>
internal static class Ui
{
    public static readonly Brush Page = Make("#F3F5F8");
    public static readonly Brush CardBg = Make("#FFFFFF");
    public static readonly Brush CardBorder = Make("#E2E6EC");
    public static readonly Brush Ink = Make("#1F2937");
    public static readonly Brush Muted = Make("#6B7280");
    public static readonly Brush Accent = Make("#2563EB");
    public static readonly Brush AccentSoft = Make("#DBEAFE");
    public static readonly Brush Good = Make("#15803D");
    public static readonly Brush GoodSoft = Make("#DCFCE7");
    public static readonly Brush Bad = Make("#B91C1C");
    public static readonly Brush BadSoft = Make("#FEE2E2");
    public static readonly Brush Warn = Make("#92400E");
    public static readonly Brush WarnSoft = Make("#FEF3C7");
    public static readonly Brush White = Brushes.White;

    // Картка повітряної тривоги: червоний і жовтий рівні.
    public static readonly Brush AlertRedBg = Make("#FECACA");
    public static readonly Brush AlertRedInk = Make("#991B1B");
    public static readonly Brush AlertYellowBg = Make("#FDE68A");
    public static readonly Brush AlertYellowInk = Make("#78350F");
    public static readonly Brush AlertRedSoft = Make("#FEF2F2");
    public static readonly Brush AlertYellowSoft = Make("#FEFCE8");
    public static readonly Brush AlertYellowDot = Make("#CA8A04");

    // Стрічка подій дня.
    public static readonly Brush NextBg = Make("#EFF6FF");
    public static readonly Brush NowInk = Make("#DC2626");
    public static readonly Brush ManualInk = Make("#7C3AED");

    private static Brush Make(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static TextBlock Text(string text, double size = 14, bool bold = false, Brush? color = null)
        => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = color ?? Ink,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0)
    };

    public static Border Card(UIElement content, string? title = null, Brush? background = null)
    {
        var panel = new StackPanel();
        if (title is not null)
        {
            var header = Text(title, 15, bold: true);
            header.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(header);
        }
        panel.Children.Add(content);
        return new Border
        {
            Background = background ?? CardBg,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 18, 16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = panel
        };
    }

    public static Button Btn(string text, RoutedEventHandler onClick, bool primary = false, double minWidth = 0)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 8),
            FontSize = 14,
            MinWidth = minWidth,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        if (primary)
        {
            b.Background = Accent;
            b.Foreground = White;
            b.BorderBrush = Accent;
        }
        b.Click += onClick;
        return b;
    }

    /// <summary>Велика кнопка ручного запуску.</summary>
    public static Button BigButton(string text, RoutedEventHandler onClick, Brush? background = null, Brush? foreground = null)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Padding = new Thickness(12, 12, 12, 12),
            Margin = new Thickness(0, 0, 10, 10),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Width = 170,
            MinHeight = 64,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        if (background is not null) { b.Background = background; b.BorderBrush = background; }
        if (foreground is not null) b.Foreground = foreground;
        b.Click += onClick;
        return b;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static StackPanel Column(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static TextBox TimeBox(string text) => new()
    {
        Text = text,
        Width = 64,
        FontSize = 14,
        Padding = new Thickness(4, 3, 4, 3),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = "Формат: 08:30"
    };

    public static CheckBox Check(string text, bool value) => new()
    {
        Content = text,
        IsChecked = value,
        FontSize = 14,
        Margin = new Thickness(0, 4, 0, 4),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static FrameworkElement Spacer(double width) => new Border { Width = width };

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(16, 14, 16, 4),
        Content = content
    };
}

/// <summary>Вибір днів тижня (Пн … Нд).</summary>
internal sealed class DayPicker : WrapPanel
{
    private static readonly (DayOfWeek Day, string Label)[] Days =
    {
        (DayOfWeek.Monday, "Пн"), (DayOfWeek.Tuesday, "Вт"), (DayOfWeek.Wednesday, "Ср"),
        (DayOfWeek.Thursday, "Чт"), (DayOfWeek.Friday, "Пт"), (DayOfWeek.Saturday, "Сб"), (DayOfWeek.Sunday, "Нд")
    };

    private readonly Dictionary<DayOfWeek, CheckBox> _boxes = new();

    public event Action? Changed;

    public DayPicker()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        foreach (var (day, label) in Days)
        {
            var cb = new CheckBox
            {
                Content = label,
                FontSize = 14,
                Margin = new Thickness(0, 4, 12, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            cb.Checked += (_, _) => Changed?.Invoke();
            cb.Unchecked += (_, _) => Changed?.Invoke();
            _boxes[day] = cb;
            Children.Add(cb);
        }
    }

    public List<DayOfWeek> Selected
    {
        get => Days.Where(d => _boxes[d.Day].IsChecked == true).Select(d => d.Day).ToList();
        set
        {
            foreach (var (day, _) in Days)
                _boxes[day].IsChecked = value.Contains(day);
        }
    }
}
