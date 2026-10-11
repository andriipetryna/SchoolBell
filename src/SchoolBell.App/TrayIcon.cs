using System;
using SchoolBell.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SchoolBell.App;

/// <summary>Іконка в області сповіщень з коротким меню.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly AppHost _host;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _bell;
    private readonly Forms.ToolStripMenuItem _bellStart;
    private readonly Forms.ToolStripMenuItem _bellEnd;
    private readonly Forms.ToolStripMenuItem _toggle;

    public TrayIcon(AppHost host, Action showWindow, Action exit)
    {
        _host = host;

        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Відкрити", null, (_, _) => showWindow());
        open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
        _bell = new Forms.ToolStripMenuItem("Дати дзвінок зараз", null, (_, _) => _host.PlayManual(SoundKind.BellStart));
        _bellStart = new Forms.ToolStripMenuItem("Дзвінок на урок", null, (_, _) => _host.PlayManual(SoundKind.BellStart));
        _bellEnd = new Forms.ToolStripMenuItem("Дзвінок з уроку", null, (_, _) => _host.PlayManual(SoundKind.BellEnd));
        var silence = new Forms.ToolStripMenuItem("Хвилина мовчання", null, (_, _) => _host.PlayManual(SoundKind.Silence));
        var stop = new Forms.ToolStripMenuItem("Зупинити звук", null, (_, _) => _host.Player.StopAll());
        _toggle = new Forms.ToolStripMenuItem("", null, (_, _) => ToggleEnabled());
        var quit = new Forms.ToolStripMenuItem("Вийти", null, (_, _) => exit());

        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_bell);
        menu.Items.Add(_bellStart);
        menu.Items.Add(_bellEnd);
        menu.Items.Add(silence);
        menu.Items.Add(stop);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_toggle);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(quit);
        menu.Opening += (_, _) => UpdateMenu();

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Шкільний дзвінок",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) showWindow();
        };
        _icon.BalloonTipClicked += (_, _) => showWindow();

        UpdateMenu();
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon is not null) return icon;
            }
        }
        catch { /* ignore */ }
        return Drawing.SystemIcons.Application;
    }

    private void ToggleEnabled()
    {
        try
        {
            _host.SetEnabled(!_host.Settings.Enabled);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Перемикання розкладу з трею", ex);
        }
    }

    private void UpdateMenu()
    {
        var s = _host.Settings;
        _bell.Visible = !s.SeparateBellFiles;
        _bellStart.Visible = s.SeparateBellFiles;
        _bellEnd.Visible = s.SeparateBellFiles;
        _toggle.Text = s.Enabled ? "Вимкнути розклад (канікули)" : "Увімкнути розклад";
    }

    public void SetTooltip(string text)
    {
        // Windows обмежує підказку 127 символами.
        if (text.Length > 120) text = text[..117] + "...";
        if (_icon.Text != text) _icon.Text = text;
    }

    public void ShowBalloon(string title, string text, bool warning)
    {
        try
        {
            _icon.ShowBalloonTip(10000, title, string.IsNullOrEmpty(text) ? " " : text,
                warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
