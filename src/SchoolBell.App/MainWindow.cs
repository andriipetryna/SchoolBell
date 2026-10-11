using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SchoolBell.Core;

namespace SchoolBell.App;

/// <summary>Головне вікно. Інтерфейс зібрано в коді (без XAML).</summary>
internal sealed class MainWindow : Window
{
    private static readonly CultureInfo Uk = new("uk-UA");
    private static readonly SoundKind[] SoundKinds =
        { SoundKind.BellStart, SoundKind.BellEnd, SoundKind.Silence, SoundKind.AirAlert, SoundKind.AllClear };

    private readonly AppHost _host;
    private bool _loading;
    private bool _dirty;
    private bool _allowClose;
    private bool _trayHintShown;
    private DateTime _lastBannerRefresh;

    // ── Шапка ──
    private readonly TextBlock _clock = Ui.Text("", 28, bold: true);
    private readonly TextBlock _date = Ui.Text("", 13, color: Ui.Muted);
    private readonly Border _statusBadge = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusText = Ui.Text("", 14, bold: true);
    private readonly Button _toggleBtn;

    // ── Головна ──
    private readonly StackPanel _banners = new();
    // Рядок стану: «Зараз звучить» або «Наступне» з таймером.
    private readonly Border _strip = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _stripLabel = Ui.Text("", 12, bold: true);
    private readonly TextBlock _stripTitle = Ui.Text("", 18, bold: true);
    private readonly TextBlock _stripWhen = Ui.Text("", 13.5, color: Ui.Muted);
    private readonly TextBlock _countdown = Ui.Text("", 32, bold: true, color: Ui.Accent);
    private readonly Button _stopBtn;
    // Стрічка подій дня.
    private readonly StackPanel _timeline = new();
    private readonly ScrollViewer _timelineScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private string? _timelineSig;
    private int _settingsVersion;
    private DateTime _userScrolledAt = DateTime.MinValue;
    private bool _autoScrolling;
    private readonly Border _alertCard;
    private readonly TextBlock _alertTitle = Ui.Text("", 18, bold: true);
    private readonly TextBlock _alertDetails = Ui.Text("", 13, color: Ui.Muted);
    // Кнопки ручного запуску — сітка з двох колонок, що розтягується на ширину картки.
    private readonly System.Windows.Controls.Primitives.UniformGrid _manualPanel = new() { Columns = 2 };

    // ── Розклад ──
    private sealed class LessonRow
    {
        public TextBox Start = null!;
        public TextBox End = null!;
    }
    private readonly List<LessonRow> _lessonRows = new();
    private readonly StackPanel _lessonsPanel = new();
    private readonly DayPicker _bellDays = new();
    private readonly CheckBox _silenceEnabled = Ui.Check("Відтворювати хвилину мовчання", true);
    private readonly TextBox _silenceTime = Ui.TimeBox("09:00");
    private readonly DayPicker _silenceDays = new();

    // ── Звуки ──
    private readonly CheckBox _separateBells = Ui.Check("Різні звуки для дзвінка на урок і з уроку", false);
    private readonly Dictionary<SoundKind, SoundFile?> _draftSounds = new();
    private readonly Dictionary<SoundKind, TextBlock> _soundNames = new();
    private readonly Dictionary<SoundKind, TextBlock> _soundCaptions = new();
    private readonly Dictionary<SoundKind, FrameworkElement> _soundRows = new();

    // ── Тривога ──
    private readonly CheckBox _monitor = Ui.Check("Відстежувати повітряну тривогу", false);
    private readonly CheckBox _autoPlay = Ui.Check("Автоматично відтворювати сигнали тривоги та відбою", true);
    private readonly ComboBox _cbState = RegionCombo();
    private readonly ComboBox _cbDistrict = RegionCombo();
    private readonly ComboBox _cbCommunity = RegionCombo();
    private readonly TextBlock _regionInfo = Ui.Text("", 14, bold: true);
    private readonly TextBlock _regionsStatus = Ui.Hint("");
    private readonly TextBox _apiKey = new() { Width = 380, FontSize = 14, Padding = new Thickness(4, 3, 4, 3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _pollSeconds = new() { Width = 60, FontSize = 14, Padding = new Thickness(4, 3, 4, 3), HorizontalContentAlignment = HorizontalAlignment.Center };
    private Button _loadRegionsBtn = null!;
    private List<RegionNode>? _regions;

    // ── Неактивність ──
    private sealed class InactRow
    {
        public TextBox Name = null!;
        public DayPicker Days = null!;
        public TextBox From = null!;
        public TextBox To = null!;
    }
    private readonly List<InactRow> _inactRows = new();
    private readonly StackPanel _inactPanel = new();

    // ── Додатково ──
    private readonly CheckBox _startMinimized = Ui.Check("Запускати згорнутим у трей (без відкриття вікна)", false);

    // ── Панель збереження ──
    private readonly Border _saveBar;

    public MainWindow(AppHost host)
    {
        _host = host;

        Title = "Шкільний дзвінок";
        Width = 940;
        Height = 720;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Page;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        // Іконку вікна WPF бере з .exe (ApplicationIcon у .csproj).

        _toggleBtn = Ui.Btn("", (_, _) => ToggleEnabled(), minWidth: 210);
        _toggleBtn.Margin = new Thickness(0);
        _stopBtn = Ui.Btn("⏹  Зупинити звук", (_, _) => _host.Player.StopAll());

        _alertCard = Ui.Card(Ui.Column(_alertTitle, _alertDetails), "Повітряна тривога");

        _saveBar = BuildSaveBar();

        var tabs = new TabControl { Margin = new Thickness(12, 0, 12, 0), FontSize = 14, Background = Ui.Page, BorderThickness = new Thickness(0) };
        tabs.Items.Add(new TabItem { Header = "Головна", Content = BuildMainTab() });
        tabs.Items.Add(new TabItem { Header = "Розклад", Content = Ui.Scroll(BuildScheduleTab()) });
        tabs.Items.Add(new TabItem { Header = "Звуки", Content = Ui.Scroll(BuildSoundsTab()) });
        tabs.Items.Add(new TabItem { Header = "Тривога", Content = Ui.Scroll(BuildAlertsTab()) });
        tabs.Items.Add(new TabItem { Header = "Неактивність", Content = Ui.Scroll(BuildInactivityTab()) });
        tabs.Items.Add(new TabItem { Header = "Додатково", Content = Ui.Scroll(BuildOtherTab()) });

        var root = new DockPanel();
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_saveBar, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(_saveBar);
        root.Children.Add(tabs);
        Content = root;

        LoadFromSettings();
        LoadRegionsFromCache();

        _host.Tick += OnTick;
        _host.SettingsChanged += () => { _settingsVersion++; RebuildManualButtons(); RefreshAll(); };
        _host.Journal.Changed += () => { if (IsVisible) RefreshTimeline(); };
        _host.AlertChanged += () => { RefreshAlert(); RefreshBanners(); };
        _host.WarningChanged += RefreshBanners;
        _host.Player.StateChanged += (_, _) => RefreshPlaying();

        RebuildManualButtons();
        RefreshAll();
    }

    // ═════════════════════════ Побудова інтерфейсу ═════════════════════════

    private FrameworkElement BuildHeader()
    {
        var left = Ui.Column(Ui.Text("Шкільний дзвінок", 20, bold: true), _date);
        left.VerticalAlignment = VerticalAlignment.Center;

        _statusBadge.Child = _statusText;
        var right = Ui.Row(_statusBadge, _toggleBtn);
        right.VerticalAlignment = VerticalAlignment.Center;

        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.Margin = new Thickness(24, 0, 24, 0);

        var dock = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(_clock, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(left);
        dock.Children.Add(_clock);
        dock.Children.Add(right);

        return new Border
        {
            Background = Ui.CardBg,
            BorderBrush = Ui.CardBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 12, 20, 12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = dock
        };
    }

    private FrameworkElement BuildMainTab()
    {
        // Рядок стану
        _countdown.VerticalAlignment = VerticalAlignment.Center;
        _countdown.TextWrapping = TextWrapping.NoWrap;
        _stopBtn.Margin = new Thickness(12, 0, 0, 0);
        _stopBtn.VerticalAlignment = VerticalAlignment.Center;
        var stripRight = Ui.Row(_countdown, _stopBtn);
        stripRight.VerticalAlignment = VerticalAlignment.Center;
        var stripLeft = Ui.Column(_stripLabel, _stripTitle, _stripWhen);
        var stripDock = new DockPanel();
        DockPanel.SetDock(stripRight, Dock.Right);
        stripDock.Children.Add(stripRight);
        stripDock.Children.Add(stripLeft);
        _strip.Child = stripDock;

        // Стрічка подій
        _timelineScroll.Content = _timeline;
        _timelineScroll.ScrollChanged += (_, e) =>
        {
            if (!_autoScrolling && e.VerticalChange != 0 && e.ExtentHeightChange == 0)
                _userScrolledAt = DateTime.Now;
        };

        // «Сьогодні» тягнеться до самого низу вікна; гортається лише сама стрічка.
        var todayTitle = Ui.Text("Сьогодні", 15, bold: true);
        todayTitle.Margin = new Thickness(0, 0, 0, 10);
        var todayGrid = new Grid();
        todayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        todayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        todayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(todayTitle, 0);
        Grid.SetRow(_strip, 1);
        Grid.SetRow(_timelineScroll, 2);
        todayGrid.Children.Add(todayTitle);
        todayGrid.Children.Add(_strip);
        todayGrid.Children.Add(_timelineScroll);
        var today = new Border
        {
            Background = Ui.CardBg,
            BorderBrush = Ui.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 18, 12),
            Child = todayGrid
        };

        // Права колонка: тривога + ручний запуск (гортається лише якщо вікно дуже низьке).
        var manual = Ui.Card(Ui.Column(_manualPanel,
            Ui.Hint("Працюють завжди — навіть коли розклад вимкнено або діє період неактивності. Кожен запуск з'являється у стрічці «Сьогодні».")),
            "Ручний запуск");
        var right = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = Ui.Column(_alertCard, manual)
        };

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 300 });
        Grid.SetColumn(today, 0);
        Grid.SetColumn(right, 2);
        columns.Children.Add(today);
        columns.Children.Add(right);

        var root = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_banners, 0);
        Grid.SetRow(columns, 1);
        root.Children.Add(_banners);
        root.Children.Add(columns);
        return root;
    }

    private FrameworkElement BuildScheduleTab()
    {
        var addLesson = Ui.Btn("+ Додати урок", (_, _) => AddLesson());
        addLesson.Margin = new Thickness(0, 8, 8, 0);

        var lessons = Ui.Card(Ui.Column(
            Ui.Hint("Час у форматі 08:30. Дзвінок на урок лунає на початку уроку, дзвінок з уроку — в кінці."),
            new Border { Height = 8 },
            _lessonsPanel,
            addLesson), "Уроки");

        var days = Ui.Card(Ui.Column(_bellDays,
            Ui.Hint("Розклад однаковий для всіх обраних днів.")), "Дні, коли лунають дзвінки");

        var silence = Ui.Card(Ui.Column(
            _silenceEnabled,
            Ui.Row(Label("Час:", 60), _silenceTime),
            Ui.Row(Label("Дні:", 60), _silenceDays),
            Ui.Hint("Хвилина мовчання має пріоритет: якщо вона збігається з дзвінком, спершу звучить вона, а дзвінок — одразу після неї.")),
            "Хвилина мовчання");

        Watch(_silenceEnabled);
        Watch(_silenceTime);
        _bellDays.Changed += MarkDirty;
        _silenceDays.Changed += MarkDirty;

        return Ui.Column(lessons, days, silence);
    }

    private FrameworkElement BuildSoundsTab()
    {
        var rows = new StackPanel();
        foreach (var kind in SoundKinds)
        {
            var caption = Ui.Text(SoundKindNames.Title(kind), 14, bold: true);
            caption.Width = 220;
            caption.VerticalAlignment = VerticalAlignment.Center;
            var name = Ui.Text("", 14, color: Ui.Muted);
            name.Width = 260;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;

            var k = kind;
            var choose = Ui.Btn("Обрати файл…", (_, _) => ChooseSound(k));
            var play = Ui.Btn("▶  Прослухати", (_, _) => PreviewSound(k));
            choose.Margin = play.Margin = new Thickness(8, 4, 0, 4);

            var row = Ui.Row(caption, name, choose, play);
            row.Margin = new Thickness(0, 2, 0, 2);
            rows.Children.Add(row);

            _soundCaptions[kind] = caption;
            _soundNames[kind] = name;
            _soundRows[kind] = row;
        }

        _separateBells.Checked += (_, _) => UpdateSoundRows();
        _separateBells.Unchecked += (_, _) => UpdateSoundRows();
        Watch(_separateBells);

        var stop = Ui.Btn("⏹  Зупинити звук", (_, _) => _host.Player.StopAll());
        stop.Margin = new Thickness(0, 12, 0, 0);
        stop.HorizontalAlignment = HorizontalAlignment.Left;

        return Ui.Column(Ui.Card(Ui.Column(
            _separateBells,
            new Border { Height = 8 },
            rows,
            Ui.Hint("Обраний файл копіюється в папку програми, тож оригінал можна видаляти або виймати флешку. " +
                    "Прослуховування працює ще до збереження змін. Гучність налаштовується в Windows."),
            stop), "Звукові файли"));
    }

    private FrameworkElement BuildAlertsTab()
    {
        Watch(_monitor);
        Watch(_autoPlay);
        Watch(_apiKey);
        Watch(_pollSeconds);

        _cbState.SelectionChanged += (_, _) => OnStateSelected();
        _cbDistrict.SelectionChanged += (_, _) => OnDistrictSelected();
        _cbCommunity.SelectionChanged += (_, _) => { UpdateRegionInfo(); MarkDirty(); };

        _loadRegionsBtn = Ui.Btn("Оновити список регіонів", async (_, _) => await LoadRegionsFromNetworkAsync());
        _loadRegionsBtn.Margin = new Thickness(0, 10, 8, 0);

        var main = Ui.Card(Ui.Column(_monitor, _autoPlay,
            Ui.Hint("Під час тривоги дзвінки лунають як звичайно. Головний вимикач і періоди неактивності вимикають також автоматичні сигнали тривоги. " +
                    "Кнопки «Тривога» і «Відбій» на головній вкладці працюють завжди."),
            Ui.Hint("Якщо сервіс повідомляє рівень загрози, картка тривоги на головній вкладці стає червоною або жовтою. " +
                    "Сигнал тривоги лунає за будь-якого рівня; зміна рівня під час тривоги сигнал не повторює.")),
            "Сповіщення про тривогу");

        var region = Ui.Card(Ui.Column(
            Ui.Row(Label("Область:", 90), _cbState),
            Ui.Row(Label("Район:", 90), _cbDistrict),
            Ui.Row(Label("Громада:", 90), _cbCommunity),
            Ui.Row(_loadRegionsBtn),
            _regionsStatus,
            new Border { Height = 8 },
            _regionInfo,
            Ui.Hint("Оберіть громаду. Якщо громаду не обрано — відстежується весь район. " +
                    "Тривога, оголошена на всю область або район, також враховується.")),
            "Регіон");

        var test = Ui.Btn("Перевірити зараз", async (_, _) => await TestAlertsAsync());
        test.Margin = new Thickness(0, 10, 0, 0);
        test.HorizontalAlignment = HorizontalAlignment.Left;

        var source = Ui.Card(Ui.Column(
            Ui.Text("Ключ API (необов'язково):", 14),
            _apiKey,
            Ui.Hint("Без ключа використовується публічне дзеркало siren.pp.ua з даними офіційного API «Повітряна тривога». " +
                    "Для більшої надійності отримайте власний ключ на api.ukrainealarm.com і вставте його сюди."),
            new Border { Height = 8 },
            Ui.Row(Label("Перевіряти кожні", 130), _pollSeconds, Label("  секунд", 80)),
            test), "Джерело даних");

        return Ui.Column(main, region, source);
    }

    private FrameworkElement BuildInactivityTab()
    {
        var add = Ui.Btn("+ Додати період", (_, _) => { AddInactivityRow(new InactivityPeriod { Name = "", From = new(16, 0), To = new(7, 30) }); MarkDirty(); });
        add.Margin = new Thickness(0, 8, 0, 0);
        add.HorizontalAlignment = HorizontalAlignment.Left;

        return Ui.Column(Ui.Card(Ui.Column(
            Ui.Hint("У ці періоди автоматично не лунає нічого: ні дзвінки, ні хвилина мовчання, ні сигнали тривоги. Ручні кнопки працюють завжди."),
            Ui.Hint("• «з» і «до» однакові (напр. 00:00–00:00) — неактивний увесь день."),
            Ui.Hint("• «з» пізніше за «до» (напр. 16:00–07:30) — період триває до ранку наступного дня."),
            Ui.Hint("Для канікул і свят зручніше головний вимикач угорі вікна."),
            new Border { Height = 10 },
            _inactPanel,
            add), "Періоди неактивності"));
    }

    private FrameworkElement BuildOtherTab()
    {
        Watch(_startMinimized);
        var openFolder = Ui.Btn("Відкрити папку даних", (_, _) => OpenDataFolder());
        openFolder.HorizontalAlignment = HorizontalAlignment.Left;
        openFolder.Margin = new Thickness(0, 8, 0, 0);

        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        return Ui.Column(
            Ui.Card(Ui.Column(_startMinimized,
                Ui.Hint("Програма завжди працює у фоні: закриття вікна лише ховає його в трей. Повністю вийти можна через меню іконки в треї.")),
                "Запуск"),
            Ui.Card(Ui.Column(
                Ui.Text($"Налаштування і звуки: {AppPaths.DataDir}", 13),
                Ui.Hint("Автозапуск після входу в Windows і перезапуск після збою налаштовує інсталятор (Планувальник завдань, завдання «SchoolBell»)."),
                openFolder), "Дані програми"),
            Ui.Card(Ui.Text($"Шкільний дзвінок, версія {version}", 13, color: Ui.Muted), "Про програму"));
    }

    private Border BuildSaveBar()
    {
        var save = Ui.Btn("Зберегти зміни", (_, _) => Save(), primary: true, minWidth: 160);
        var cancel = Ui.Btn("Скасувати", (_, _) => { LoadFromSettings(); LoadRegionsFromCache(); });
        save.Margin = cancel.Margin = new Thickness(8, 0, 0, 0);

        var text = Ui.Text("Є незбережені зміни в налаштуваннях", 14, bold: true, color: Ui.Warn);
        text.VerticalAlignment = VerticalAlignment.Center;

        var dock = new DockPanel();
        var buttons = Ui.Row(cancel, save);
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        dock.Children.Add(text);

        return new Border
        {
            Background = Ui.WarnSoft,
            BorderBrush = Ui.CardBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 10, 20, 10),
            Child = dock,
            Visibility = Visibility.Collapsed
        };
    }

    private static ComboBox RegionCombo() => new()
    {
        Width = 420,
        FontSize = 14,
        Margin = new Thickness(0, 3, 0, 3),
        DisplayMemberPath = nameof(RegionNode.Name),
        IsEditable = false
    };

    private static TextBlock Label(string text, double width)
    {
        var t = Ui.Text(text, 14);
        t.Width = width;
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    private void Watch(TextBox box) => box.TextChanged += (_, _) => MarkDirty();

    private void Watch(CheckBox box)
    {
        box.Checked += (_, _) => MarkDirty();
        box.Unchecked += (_, _) => MarkDirty();
    }

    // ═════════════════════════ Уроки та періоди ═════════════════════════

    private void AddLesson()
    {
        var start = new TimeOnly(8, 30);
        if (_lessonRows.Count > 0 && TimeParsing.TryParse(_lessonRows[^1].End.Text, out var lastEnd))
            start = lastEnd.AddMinutes(10);
        AddLessonRow(new Lesson(start, start.AddMinutes(45)));
        MarkDirty();
    }

    private void AddLessonRow(Lesson lesson)
    {
        var row = new LessonRow
        {
            Start = Ui.TimeBox(TimeParsing.Format(lesson.Start)),
            End = Ui.TimeBox(TimeParsing.Format(lesson.End))
        };
        Watch(row.Start);
        Watch(row.End);
        _lessonRows.Add(row);
        RebuildLessonsPanel();
    }

    private void RebuildLessonsPanel()
    {
        _lessonsPanel.Children.Clear();
        for (int i = 0; i < _lessonRows.Count; i++)
        {
            var row = _lessonRows[i];
            // TextBox можна вставити лише в одного батька — від'єднуємо від старого рядка.
            (row.Start.Parent as Panel)?.Children.Remove(row.Start);
            (row.End.Parent as Panel)?.Children.Remove(row.End);

            var remove = Ui.Btn("✕", (_, _) =>
            {
                _lessonRows.Remove(row);
                RebuildLessonsPanel();
                MarkDirty();
            });
            remove.Margin = new Thickness(12, 2, 0, 2);
            remove.Padding = new Thickness(8, 2, 8, 2);
            remove.ToolTip = "Видалити урок";

            var line = Ui.Row(Label($"{i + 1}-й урок", 90), row.Start, Ui.Text("  –  ", 14), row.End, remove);
            line.Margin = new Thickness(0, 3, 0, 3);
            _lessonsPanel.Children.Add(line);
        }
        if (_lessonRows.Count == 0)
            _lessonsPanel.Children.Add(Ui.Hint("Уроків немає. Натисніть «Додати урок»."));
    }

    private void AddInactivityRow(InactivityPeriod p)
    {
        var row = new InactRow
        {
            Name = new TextBox { Text = p.Name, Width = 220, FontSize = 14, Padding = new Thickness(4, 3, 4, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Назва (необов'язково)" },
            Days = new DayPicker { Selected = p.Days },
            From = Ui.TimeBox(TimeParsing.Format(p.From)),
            To = Ui.TimeBox(TimeParsing.Format(p.To))
        };
        Watch(row.Name);
        Watch(row.From);
        Watch(row.To);
        row.Days.Changed += MarkDirty;
        _inactRows.Add(row);
        RebuildInactivityPanel();
    }

    private void RebuildInactivityPanel()
    {
        _inactPanel.Children.Clear();
        foreach (var row in _inactRows)
        {
            foreach (var el in new FrameworkElement[] { row.Name, row.From, row.To, row.Days })
                (el.Parent as Panel)?.Children.Remove(el);

            var r = row;
            var remove = Ui.Btn("✕", (_, _) =>
            {
                _inactRows.Remove(r);
                RebuildInactivityPanel();
                MarkDirty();
            });
            remove.Margin = new Thickness(12, 0, 0, 0);
            remove.Padding = new Thickness(8, 2, 8, 2);
            remove.ToolTip = "Видалити період";

            var top = Ui.Row(row.Name, Ui.Text("   з  ", 14), row.From, Ui.Text("  до  ", 14), row.To, remove);
            var box = new Border
            {
                BorderBrush = Ui.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = Ui.Column(top, row.Days)
            };
            _inactPanel.Children.Add(box);
        }
        if (_inactRows.Count == 0)
            _inactPanel.Children.Add(Ui.Hint("Періодів немає."));
    }

    // ═════════════════════════ Звуки ═════════════════════════

    private void UpdateSoundRows()
    {
        bool separate = _separateBells.IsChecked == true;
        _soundCaptions[SoundKind.BellStart].Text = separate ? "Дзвінок на урок" : "Дзвінок (на урок і з уроку)";
        _soundRows[SoundKind.BellEnd].Visibility = separate ? Visibility.Visible : Visibility.Collapsed;
        foreach (var kind in SoundKinds)
        {
            var f = _draftSounds.GetValueOrDefault(kind);
            var label = _soundNames[kind];
            if (f is null)
            {
                label.Text = "не обрано";
                label.Foreground = Ui.Bad;
            }
            else if (!File.Exists(f.StoredPath))
            {
                label.Text = $"{f.OriginalName} (файл зник!)";
                label.Foreground = Ui.Bad;
            }
            else
            {
                label.Text = f.OriginalName;
                label.Foreground = Ui.Ink;
            }
            label.ToolTip = f?.StoredPath;
        }
    }

    private void ChooseSound(SoundKind kind)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Оберіть звуковий файл: " + _soundCaptions[kind].Text,
            Filter = "Аудіофайли (*.mp3;*.wav;*.wma;*.m4a;*.aac)|*.mp3;*.wav;*.wma;*.m4a;*.aac|Усі файли (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            _draftSounds[kind] = SettingsStore.ImportSound(dlg.FileName, kind);
            UpdateSoundRows();
            MarkDirty();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Імпорт звуку", ex);
            MessageBox.Show(this, "Не вдалося скопіювати файл: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PreviewSound(SoundKind kind)
    {
        var f = _draftSounds.GetValueOrDefault(kind);
        if (f is null)
        {
            MessageBox.Show(this, "Спочатку оберіть файл.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _host.Player.RequestManual(kind, f.StoredPath, "Прослуховування: " + _soundCaptions[kind].Text);
    }

    // ═════════════════════════ Регіони ═════════════════════════

    private static string RegionsCacheFile => Path.Combine(AppPaths.DataDir, "regions.json");

    private static RegionNode Placeholder(string text) => new() { Id = "", Name = text };

    private void LoadRegionsFromCache()
    {
        try
        {
            if (File.Exists(RegionsCacheFile))
            {
                var roots = AlertParsing.ParseRegions(File.ReadAllText(RegionsCacheFile));
                if (roots.Count > 0)
                {
                    FillRegions(roots);
                    _regionsStatus.Text = $"Список регіонів збережено {File.GetLastWriteTime(RegionsCacheFile):dd.MM.yyyy}.";
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Читання кешу регіонів", ex);
        }

        _regions = null;
        _cbState.ItemsSource = null;
        _cbDistrict.ItemsSource = null;
        _cbCommunity.ItemsSource = null;
        _regionsStatus.Text = "Список регіонів ще не завантажено. Натисніть «Оновити список регіонів» (потрібен інтернет).";
        UpdateRegionInfo();
    }

    private async System.Threading.Tasks.Task LoadRegionsFromNetworkAsync()
    {
        _loadRegionsBtn.IsEnabled = false;
        _regionsStatus.Text = "Завантаження…";
        try
        {
            var client = new UkraineAlarmClient(_host.Http, _apiKey.Text);
            var json = await client.GetRegionsJsonAsync();
            var roots = AlertParsing.ParseRegions(json);
            if (roots.Count == 0) throw new InvalidOperationException("сервіс повернув порожній список");
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(RegionsCacheFile, json);
            bool wasDirty = _dirty;
            FillRegions(roots);
            if (!wasDirty) SetDirty(false);
            _regionsStatus.Text = $"Завантажено: {roots.Count} областей ({client.SourceName}).";
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Завантаження регіонів", ex);
            _regionsStatus.Text = "Не вдалося завантажити: " + ex.Message;
        }
        finally
        {
            _loadRegionsBtn.IsEnabled = true;
        }
    }

    private void FillRegions(List<RegionNode> roots)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _regions = roots;
            var states = new List<RegionNode> { Placeholder("— не обрано —") };
            states.AddRange(roots.OrderBy(r => r.Name, StringComparer.Create(Uk, true)));
            _cbState.ItemsSource = states;
            _cbState.SelectedIndex = 0;
            SelectRegionChain(_host.Settings.Alerts.RegionId);
        }
        finally
        {
            _loading = wasLoading;
        }
        UpdateRegionInfo();
    }

    private void SelectRegionChain(string? regionId)
    {
        if (_regions is null || string.IsNullOrEmpty(regionId)) return;
        var path = RegionTree.FindPath(_regions, regionId);
        var combos = new[] { _cbState, _cbDistrict, _cbCommunity };
        for (int i = 0; i < path.Count && i < combos.Length; i++)
        {
            if (combos[i].ItemsSource is not List<RegionNode> items) break;
            var match = items.FirstOrDefault(n => n.Id == path[i].Id);
            if (match is null) break;
            combos[i].SelectedItem = match; // SelectionChanged заповнює наступний список
        }
    }

    private void OnStateSelected()
    {
        var state = _cbState.SelectedItem as RegionNode;
        if (state is null || state.Id == "")
        {
            _cbDistrict.ItemsSource = null;
            _cbCommunity.ItemsSource = null;
        }
        else
        {
            var list = new List<RegionNode> { Placeholder("— уся область —") };
            list.AddRange(state.Children);
            _cbDistrict.ItemsSource = list;
            _cbDistrict.SelectedIndex = 0;
        }
        UpdateRegionInfo();
        MarkDirty();
    }

    private void OnDistrictSelected()
    {
        var district = _cbDistrict.SelectedItem as RegionNode;
        if (district is null || district.Id == "" || district.Children.Count == 0)
        {
            _cbCommunity.ItemsSource = null;
        }
        else
        {
            var list = new List<RegionNode> { Placeholder("— увесь район —") };
            list.AddRange(district.Children);
            _cbCommunity.ItemsSource = list;
            _cbCommunity.SelectedIndex = 0;
        }
        UpdateRegionInfo();
        MarkDirty();
    }

    /// <summary>Обраний у списках шлях (область → район → громада) без заглушок.</summary>
    private List<RegionNode> SelectedRegionPath()
    {
        var path = new List<RegionNode>();
        foreach (var cb in new[] { _cbState, _cbDistrict, _cbCommunity })
        {
            if (cb.SelectedItem is RegionNode n && n.Id != "") path.Add(n);
            else break;
        }
        return path;
    }

    private static string DescribePath(List<RegionNode> path)
        => path.Count == 0 ? "" : path[^1].Name + (path.Count > 1 ? " (" + string.Join(", ", path.Take(path.Count - 1).Reverse().Select(p => p.Name)) + ")" : "");

    private void UpdateRegionInfo()
    {
        if (_regions is null)
        {
            var saved = _host.Settings.Alerts.RegionName;
            _regionInfo.Text = string.IsNullOrEmpty(saved) ? "Регіон не обрано" : "Обрано: " + saved;
            return;
        }
        var path = SelectedRegionPath();
        _regionInfo.Text = path.Count == 0 ? "Регіон не обрано" : "Обрано: " + DescribePath(path);
    }

    private async System.Threading.Tasks.Task TestAlertsAsync()
    {
        List<string> chain;
        string name;
        if (_regions is not null)
        {
            var path = SelectedRegionPath();
            chain = path.Select(p => p.Id).ToList();
            name = DescribePath(path);
        }
        else
        {
            chain = _host.Settings.Alerts.RegionChain;
            name = _host.Settings.Alerts.RegionName ?? "";
        }

        if (chain.Count == 0)
        {
            MessageBox.Show(this, "Спочатку оберіть регіон.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var client = new UkraineAlarmClient(_host.Http, _apiKey.Text);
        try
        {
            var all = await client.GetAirAlertsAsync();
            var status = AlertParsing.StatusFor(all, chain);
            string text = status is null
                ? "тривоги немає"
                : "ДІЄ ПОВІТРЯНА ТРИВОГА"
                  + (status.Level == AlertLevel.Unspecified ? "" : $" ({status.LevelTitle})")
                  + (string.IsNullOrEmpty(status.Reason) ? "" : "\n" + status.Reason);
            MessageBox.Show(this,
                $"Зв'язок із сервісом є ({client.SourceName}).\n\n{name}:\n{text}",
                Title, MessageBoxButton.OK, status is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не вдалося отримати дані ({client.SourceName}):\n{ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═════════════════════════ Завантаження / збереження ═════════════════════════

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            var s = _host.Settings;

            _lessonRows.Clear();
            foreach (var l in s.Lessons) AddLessonRow(l);
            RebuildLessonsPanel();
            _bellDays.Selected = s.BellDays;
            _silenceEnabled.IsChecked = s.SilenceEnabled;
            _silenceTime.Text = TimeParsing.Format(s.SilenceTime);
            _silenceDays.Selected = s.SilenceDays;

            _separateBells.IsChecked = s.SeparateBellFiles;
            foreach (var kind in SoundKinds)
                _draftSounds[kind] = kind switch
                {
                    SoundKind.BellStart => s.BellStartSound,
                    SoundKind.BellEnd => s.BellEndSound,
                    SoundKind.Silence => s.SilenceSound,
                    SoundKind.AirAlert => s.AirAlertSound,
                    SoundKind.AllClear => s.AllClearSound,
                    _ => null
                };
            UpdateSoundRows();

            _monitor.IsChecked = s.Alerts.MonitorEnabled;
            _autoPlay.IsChecked = s.Alerts.AutoPlay;
            _apiKey.Text = s.Alerts.ApiKey ?? "";
            _pollSeconds.Text = s.Alerts.PollSeconds.ToString(CultureInfo.InvariantCulture);

            _inactRows.Clear();
            foreach (var p in s.InactivityPeriods) AddInactivityRow(p);
            RebuildInactivityPanel();

            _startMinimized.IsChecked = s.StartMinimized;
        }
        finally
        {
            _loading = false;
        }
        SetDirty(false);
    }

    private bool TryCollect(out AppSettings result, out List<string> errors)
    {
        errors = new List<string>();
        var s = SettingsStore.Clone(_host.Settings); // Enabled та інше, чого немає у формі, беремо з поточних

        s.Lessons = new List<Lesson>();
        for (int i = 0; i < _lessonRows.Count; i++)
        {
            var row = _lessonRows[i];
            bool ok1 = TimeParsing.TryParse(row.Start.Text, out var start);
            bool ok2 = TimeParsing.TryParse(row.End.Text, out var end);
            if (!ok1 || !ok2) errors.Add($"Урок {i + 1}: невірний формат часу (потрібно, наприклад, 08:30).");
            else s.Lessons.Add(new Lesson(start, end));
        }
        s.BellDays = _bellDays.Selected;

        s.SilenceEnabled = _silenceEnabled.IsChecked == true;
        if (TimeParsing.TryParse(_silenceTime.Text, out var silence)) s.SilenceTime = silence;
        else errors.Add("Хвилина мовчання: невірний формат часу.");
        s.SilenceDays = _silenceDays.Selected;

        s.SeparateBellFiles = _separateBells.IsChecked == true;
        foreach (var kind in SoundKinds) s.SetSound(kind, _draftSounds.GetValueOrDefault(kind));

        s.Alerts.MonitorEnabled = _monitor.IsChecked == true;
        s.Alerts.AutoPlay = _autoPlay.IsChecked == true;
        s.Alerts.ApiKey = string.IsNullOrWhiteSpace(_apiKey.Text) ? null : _apiKey.Text.Trim();
        if (int.TryParse(_pollSeconds.Text.Trim(), out var poll)) s.Alerts.PollSeconds = poll;
        else errors.Add("Інтервал перевірки тривоги має бути цілим числом секунд.");

        if (_regions is not null) // якщо список регіонів не завантажено — залишаємо збережений регіон
        {
            var path = SelectedRegionPath();
            s.Alerts.RegionChain = path.Select(p => p.Id).ToList();
            s.Alerts.RegionId = path.Count > 0 ? path[^1].Id : null;
            s.Alerts.RegionName = path.Count > 0 ? DescribePath(path) : null;
        }

        s.InactivityPeriods = new List<InactivityPeriod>();
        for (int i = 0; i < _inactRows.Count; i++)
        {
            var row = _inactRows[i];
            var name = string.IsNullOrWhiteSpace(row.Name.Text) ? $"Період {i + 1}" : row.Name.Text.Trim();
            bool ok1 = TimeParsing.TryParse(row.From.Text, out var from);
            bool ok2 = TimeParsing.TryParse(row.To.Text, out var to);
            if (!ok1 || !ok2) errors.Add($"Період «{name}»: невірний формат часу.");
            else s.InactivityPeriods.Add(new InactivityPeriod { Name = name, Days = row.Days.Selected, From = from, To = to });
        }

        s.StartMinimized = _startMinimized.IsChecked == true;

        if (errors.Count == 0) errors.AddRange(s.Validate());
        result = s;
        return errors.Count == 0;
    }

    private bool Save()
    {
        if (!TryCollect(out var s, out var errors))
        {
            MessageBox.Show(this, "Зміни не збережено:\n\n• " + string.Join("\n• ", errors), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try
        {
            _host.ApplySettings(s);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Збереження налаштувань", ex);
            MessageBox.Show(this, "Не вдалося записати налаштування: " + ex.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        LoadFromSettings();
        if (_regions is not null) FillRegions(_regions);
        return true;
    }

    private void MarkDirty()
    {
        if (_loading) return;
        SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _saveBar.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═════════════════════════ Оновлення стану ═════════════════════════

    private void OnTick()
    {
        if (!IsVisible) return;
        RefreshClockAndNext();
        RefreshTimeline();
        RefreshAlert();
        if ((DateTime.Now - _lastBannerRefresh).TotalSeconds >= 10) RefreshBanners();
    }

    private void RefreshAll()
    {
        RefreshClockAndNext();
        RefreshTimeline(force: true);
        RefreshAlert();
        RefreshBanners();
    }

    private void RefreshClockAndNext()
    {
        var now = DateTime.Now;
        var s = _host.Settings;
        _clock.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _date.Text = now.ToString("dddd, d MMMM yyyy", Uk);

        var inactive = Schedule.FindInactivePeriod(s, now);
        if (!s.Enabled)
        {
            _statusBadge.Background = Ui.BadSoft;
            _statusText.Foreground = Ui.Bad;
            _statusText.Text = "Розклад вимкнено";
            _toggleBtn.Content = "Увімкнути розклад";
        }
        else if (inactive is not null)
        {
            _statusBadge.Background = Ui.WarnSoft;
            _statusText.Foreground = Ui.Warn;
            _statusText.Text = $"Неактивний період: {inactive.Name}";
            _toggleBtn.Content = "Вимкнути розклад (канікули)";
        }
        else
        {
            _statusBadge.Background = Ui.GoodSoft;
            _statusText.Foreground = Ui.Good;
            _statusText.Text = "Розклад працює";
            _toggleBtn.Content = "Вимкнути розклад (канікули)";
        }

        RefreshStrip(now);
    }

    /// <summary>Рядок стану над стрічкою: що звучить зараз або що буде далі.</summary>
    private void RefreshStrip(DateTime now)
    {
        var s = _host.Settings;
        var cur = _host.Player.Current;
        int queued = _host.Player.Queue.Count;

        if (cur is not null)
        {
            _strip.Background = Ui.GoodSoft;
            _stripLabel.Text = "▶  ЗАРАЗ ЗВУЧИТЬ";
            _stripLabel.Foreground = Ui.Good;
            _stripTitle.Text = cur.Title;
            SetStripWhen(queued > 0 ? $"у черзі ще {queued}" : "");
            _countdown.Visibility = Visibility.Collapsed;
            _stopBtn.Visibility = Visibility.Visible;
            return;
        }

        _stopBtn.Visibility = queued > 0 ? Visibility.Visible : Visibility.Collapsed;
        _countdown.Visibility = Visibility.Visible;
        _strip.Background = Ui.NextBg;
        _stripLabel.Text = "НАСТУПНЕ";
        _stripLabel.Foreground = Ui.Accent;

        var next = Schedule.Next(s, now);
        if (!s.Enabled)
        {
            _strip.Background = Ui.BadSoft;
            _stripLabel.Foreground = Ui.Bad;
            _stripTitle.Text = "Розклад вимкнено";
            SetStripWhen("Автоматичні дзвінки, хвилина мовчання і сигнали тривоги не лунають.");
            _countdown.Text = "—";
        }
        else if (next is null)
        {
            _stripTitle.Text = "Немає запланованих подій";
            SetStripWhen("На найближчі два тижні нічого не заплановано.");
            _countdown.Text = "—";
        }
        else
        {
            _stripTitle.Text = next.Title;
            string day = next.At.Date == now.Date ? "сьогодні"
                : next.At.Date == now.Date.AddDays(1) ? "завтра"
                : next.At.ToString("dddd, d MMMM", Uk);
            SetStripWhen($"{day} о {next.At:HH:mm}");
            _countdown.Text = FormatSpan(next.At - now);
        }
    }

    private void SetStripWhen(string text)
    {
        _stripWhen.Text = text;
        _stripWhen.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ═════════════════════════ Стрічка подій дня ═════════════════════════

    private void RefreshTimeline(bool force = false)
    {
        var now = DateTime.Now;
        _host.Journal.EnsureDay(now);
        var tag = _host.Player.Current?.Tag;
        var sig = $"{_host.Journal.Day}|{_host.Journal.Version}|{now:HHmm}|{_settingsVersion}|{tag}";
        if (!force && sig == _timelineSig) return;
        _timelineSig = sig;

        var items = Timeline.Build(_host.Settings, DateOnly.FromDateTime(now), _host.Journal.Entries, now, tag);
        _timeline.Children.Clear();
        if (items.All(i => i.Kind == TimelineKind.Now))
            _timeline.Children.Add(Ui.Hint("На сьогодні подій за розкладом немає."));

        FrameworkElement? nowRow = null;
        foreach (var item in items)
        {
            var row = BuildTimelineRow(item, now);
            _timeline.Children.Add(row);
            if (item.Kind == TimelineKind.Now) nowRow = row;
        }

        // Тримаємо позначку «зараз» у полі зору, якщо користувач сам не гортав стрічку останні 30 секунд.
        if (nowRow is not null && (DateTime.Now - _userScrolledAt).TotalSeconds > 30)
            Dispatcher.InvokeAsync(() => ScrollTimelineTo(nowRow), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollTimelineTo(FrameworkElement row)
    {
        try
        {
            if (!row.IsLoaded) return;
            var y = row.TranslatePoint(new Point(0, 0), _timeline).Y;
            _autoScrolling = true;
            _timelineScroll.ScrollToVerticalOffset(Math.Max(0, y - _timelineScroll.ViewportHeight / 3));
        }
        catch
        {
            // Позиціювання не критичне.
        }
        finally
        {
            Dispatcher.InvokeAsync(() => _autoScrolling = false, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private FrameworkElement BuildTimelineRow(TimelineItem item, DateTime now)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Вертикальна лінія «рейки» — суцільна, бо рядки йдуть впритул.
        var rail = new Border { Width = 2, Background = Ui.CardBorder, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(rail, 1);
        grid.Children.Add(rail);

        if (item.Kind == TimelineKind.Now)
        {
            var time = Ui.Text(item.At.ToString("HH:mm"), 13, bold: true, color: Ui.NowInk);
            time.Margin = new Thickness(0, 4, 0, 4);
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = Ui.NowInk,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var label = Ui.Text("зараз", 12, bold: true, color: Ui.NowInk);
            label.Margin = new Thickness(10, 0, 8, 0);
            var bar = new Border { Height = 2, Background = Ui.NowInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var line = new DockPanel();
            DockPanel.SetDock(label, Dock.Left);
            line.Children.Add(label);
            line.Children.Add(bar);
            Grid.SetColumn(time, 0);
            Grid.SetColumn(dot, 1);
            Grid.SetColumn(line, 2);
            grid.Children.Add(time);
            grid.Children.Add(dot);
            grid.Children.Add(line);
            return grid;
        }

        bool highlight = item.State is TimelineState.Next or TimelineState.Playing;
        bool muted = item.State is TimelineState.Missed or TimelineState.Off;
        bool past = item.At <= now && item.State != TimelineState.Playing;

        Brush accent = item.Kind switch
        {
            TimelineKind.Manual => Ui.ManualInk,
            TimelineKind.Alert => item.Level == AlertLevel.Yellow ? Ui.AlertYellowDot : Ui.AlertRedInk,
            TimelineKind.AllClear => Ui.Good,
            TimelineKind.System => Ui.Muted,
            _ => item.State switch
            {
                TimelineState.Done or TimelineState.Playing => Ui.Good,
                TimelineState.Next or TimelineState.Upcoming => Ui.Accent,
                _ => Ui.Muted
            }
        };
        bool hollow = item.State is TimelineState.Upcoming or TimelineState.Missed or TimelineState.Off;

        var dotShape = new System.Windows.Shapes.Ellipse
        {
            Width = 12, Height = 12,
            Fill = hollow ? Ui.CardBg : accent,
            Stroke = accent,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var timeText = Ui.Text(item.At.ToString("HH:mm"), 14, bold: highlight, color: muted || past ? Ui.Muted : Ui.Ink);
        timeText.VerticalAlignment = VerticalAlignment.Top;
        timeText.Margin = new Thickness(0, 8, 0, 0);

        string icon = item.Kind switch
        {
            TimelineKind.Bell => "🔔",
            TimelineKind.Silence => "🕯",
            TimelineKind.Manual => "✋",
            TimelineKind.Alert => "⚠",
            TimelineKind.AllClear => "✔",
            _ => "⏻"
        };
        var title = Ui.Text($"{icon}  {item.Title}", 14,
            bold: highlight || item.Kind is TimelineKind.Alert or TimelineKind.AllClear,
            color: muted ? Ui.Muted : item.Kind == TimelineKind.Alert ? accent : Ui.Ink);
        var body = Ui.Column(title);

        string? detail = item.State == TimelineState.Next ? "наступне · " + MinutesLeft(item.At - now) : item.Detail;
        if (item.State == TimelineState.Playing) detail = "▶  звучить зараз";
        if (!string.IsNullOrEmpty(detail))
        {
            var d = Ui.Text(detail!, 12.5, color: item.State == TimelineState.Missed ? Ui.Warn
                : item.State == TimelineState.Playing ? Ui.Good
                : item.State == TimelineState.Next ? Ui.Accent
                : Ui.Muted);
            d.Margin = new Thickness(0, 1, 0, 0);
            body.Children.Add(d);
        }

        var box = new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(4, 2, 4, 2),
            Background = item.State switch
            {
                TimelineState.Playing => Ui.GoodSoft,
                TimelineState.Next => Ui.NextBg,
                _ when item.Kind == TimelineKind.Alert => item.Level == AlertLevel.Yellow ? Ui.AlertYellowSoft : Ui.AlertRedSoft,
                _ => Brushes.Transparent
            }
        };

        Grid.SetColumn(timeText, 0);
        Grid.SetColumn(dotShape, 1);
        Grid.SetColumn(box, 2);
        grid.Children.Add(timeText);
        grid.Children.Add(dotShape);
        grid.Children.Add(box);
        if (past && item.State == TimelineState.Done) grid.Opacity = 0.8;
        return grid;
    }

    /// <summary>«через 12 хв» / «через 1 год 05 хв» — стрічка оновлюється раз на хвилину, тож без секунд.</summary>
    private static string MinutesLeft(TimeSpan t)
    {
        int minutes = (int)Math.Ceiling(Math.Max(0, t.TotalMinutes));
        if (minutes <= 1) return "менше ніж за хвилину";
        return minutes < 60 ? $"через {minutes} хв" : $"через {minutes / 60} год {minutes % 60:00} хв";
    }

    private static string FormatSpan(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalDays >= 1
            ? $"{(int)t.TotalDays} дн {t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private void RefreshPlaying()
    {
        RefreshStrip(DateTime.Now);
        RefreshTimeline();
    }

    private void RefreshAlert()
    {
        var snap = _host.LastAlert;
        var s = _host.Settings;

        if (!s.Alerts.MonitorEnabled || string.IsNullOrEmpty(s.Alerts.RegionId))
        {
            SetAlertCard(Ui.CardBg, "Відстеження вимкнено", Ui.Muted,
                "Увімкніть на вкладці «Тривога» та оберіть громаду або район.");
            return;
        }

        var lines = new List<string> { s.Alerts.RegionName ?? "" };

        if (snap is not null && snap.State == AlertState.Active && !string.IsNullOrEmpty(snap.Info?.Reason))
            lines.Add(snap.Info!.Reason!);

        if (snap is { State: AlertState.Unknown, LastError: { } err })
            lines.Add("Помилка: " + err);

        if (snap is { ConnectionLost: true, LastSuccess: { } ok })
            lines.Add($"Немає зв'язку з сервісом з {ok:HH:mm} — стан може бути застарілим.");
        if (!s.Alerts.AutoPlay)
            lines.Add("Автоматичне відтворення сигналів вимкнено.");

        var details = string.Join("\n", lines.Where(l => l != ""));

        if (snap is null || snap.State == AlertState.Unknown)
        {
            SetAlertCard(Ui.CardBg, snap?.LastError is null ? "Отримання даних…" : "Немає даних", Ui.Muted, details);
            return;
        }

        if (snap.State == AlertState.Clear)
        {
            SetAlertCard(Ui.GoodSoft, "Тривоги немає", Ui.Good, details);
            return;
        }

        var level = snap.Info?.Level ?? AlertLevel.Unspecified;
        var title = "ПОВІТРЯНА ТРИВОГА" + (level == AlertLevel.Unspecified ? "" : " — " + snap.Info!.LevelTitle);
        if (level == AlertLevel.Yellow)
            SetAlertCard(Ui.AlertYellowBg, title, Ui.AlertYellowInk, details);
        else
            SetAlertCard(Ui.AlertRedBg, title, Ui.AlertRedInk, details);
    }

    private void SetAlertCard(Brush background, string title, Brush titleColor, string details)
    {
        _alertCard.Background = background;
        _alertTitle.Text = title;
        _alertTitle.Foreground = titleColor;
        _alertDetails.Text = details;
    }

    private void RefreshBanners()
    {
        _lastBannerRefresh = DateTime.Now;
        _banners.Children.Clear();
        var s = _host.Settings;

        if (_host.Warning is { } warning)
            AddBanner(warning, Ui.WarnSoft, Ui.Warn, () => _host.SetWarning(null));

        var missing = new List<string>();
        void Need(SoundKind kind, bool required)
        {
            if (!required) return;
            var f = s.ResolveSound(kind);
            if (f is null || !File.Exists(f.StoredPath)) missing.Add(SoundKindNames.Title(kind).ToLower(Uk));
        }
        bool bells = s.Lessons.Count > 0 && s.BellDays.Count > 0;
        Need(SoundKind.BellStart, bells);
        if (s.SeparateBellFiles) Need(SoundKind.BellEnd, bells);
        Need(SoundKind.Silence, s.SilenceEnabled && s.SilenceDays.Count > 0);
        Need(SoundKind.AirAlert, s.Alerts.MonitorEnabled && s.Alerts.AutoPlay);
        Need(SoundKind.AllClear, s.Alerts.MonitorEnabled && s.Alerts.AutoPlay);
        if (missing.Count > 0)
            AddBanner($"Не обрано звуковий файл: {string.Join(", ", missing)}. Замість нього пролунає резервний сигнал. Оберіть файли на вкладці «Звуки».",
                Ui.BadSoft, Ui.Bad, null);

        if (_host.LastAlert is { ConnectionLost: true } snap && s.Alerts.MonitorEnabled)
            AddBanner($"Немає зв'язку з сервісом тривог ({snap.Source}). Перевірте інтернет. {snap.LastError}",
                Ui.WarnSoft, Ui.Warn, null);
    }

    private void AddBanner(string text, Brush background, Brush foreground, Action? onClose)
    {
        var t = Ui.Text(text, 14, bold: true, color: foreground);
        t.VerticalAlignment = VerticalAlignment.Center;
        var dock = new DockPanel();
        if (onClose is not null)
        {
            var close = Ui.Btn("✕", (_, _) => onClose());
            close.Margin = new Thickness(12, 0, 0, 0);
            close.Padding = new Thickness(8, 2, 8, 2);
            DockPanel.SetDock(close, Dock.Right);
            dock.Children.Add(close);
        }
        dock.Children.Add(t);
        _banners.Children.Add(new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = dock
        });
    }

    private void RebuildManualButtons()
    {
        _manualPanel.Children.Clear();
        var p = _host.Player;
        if (_host.Settings.SeparateBellFiles)
        {
            _manualPanel.Children.Add(Ui.BigButton("🔔  Дзвінок на урок", (_, _) => _host.PlayManual(SoundKind.BellStart), Ui.AccentSoft));
            _manualPanel.Children.Add(Ui.BigButton("🔔  Дзвінок з уроку", (_, _) => _host.PlayManual(SoundKind.BellEnd), Ui.AccentSoft));
        }
        else
        {
            _manualPanel.Children.Add(Ui.BigButton("🔔  Дзвінок", (_, _) => _host.PlayManual(SoundKind.BellStart), Ui.AccentSoft));
        }
        _manualPanel.Children.Add(Ui.BigButton("🕯  Хвилина мовчання", (_, _) => _host.PlayManual(SoundKind.Silence)));
        _manualPanel.Children.Add(Ui.BigButton("⚠  Повітряна тривога", (_, _) => _host.PlayManual(SoundKind.AirAlert), Ui.BadSoft, Ui.Bad));
        _manualPanel.Children.Add(Ui.BigButton("✔  Відбій тривоги", (_, _) => _host.PlayManual(SoundKind.AllClear), Ui.GoodSoft, Ui.Good));
        _manualPanel.Children.Add(Ui.BigButton("⏹  Зупинити звук", (_, _) => p.StopAll()));

        foreach (var child in _manualPanel.Children)
        {
            if (child is not Button b) continue;
            b.Width = double.NaN; // розтягнути на всю ширину колонки
            b.MinHeight = 56;
            b.Margin = new Thickness(0, 0, 8, 8);
        }
    }

    // ═════════════════════════ Дії ═════════════════════════

    private void ToggleEnabled()
    {
        var s = _host.Settings;
        if (s.Enabled)
        {
            var answer = MessageBox.Show(this,
                "Вимкнути розклад?\n\nНе лунатимуть автоматичні дзвінки, хвилина мовчання і сигнали тривоги, доки ви не увімкнете його знову. Ручні кнопки працюватимуть.",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        try
        {
            _host.SetEnabled(!s.Enabled);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Перемикання розкладу", ex);
            MessageBox.Show(this, "Не вдалося зберегти: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.DataDir + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // виводимо поверх інших вікон
        Topmost = false;
        RefreshAll();
    }

    /// <summary>Дозволити справжнє закриття (при виході з програми).</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Чи є незбережені зміни (для попередження при виході).</summary>
    public bool HasUnsavedChanges => _dirty;

    public event Action? HiddenToTray;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // Закриття вікна лише ховає його — програма продовжує працювати.
            e.Cancel = true;
            Hide();
            if (!_trayHintShown || _dirty)
            {
                _trayHintShown = true;
                HiddenToTray?.Invoke();
            }
            return;
        }
        base.OnClosing(e);
    }
}
