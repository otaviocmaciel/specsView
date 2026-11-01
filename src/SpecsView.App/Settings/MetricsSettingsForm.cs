using SpecsView.Core.Config;
using SpecsView.Sensors.Providers;

namespace SpecsView.App.Settings;

/// <summary>Checkboxes for what the widget shows; applying writes config.json through the callback.</summary>
internal sealed class MetricsSettingsForm : Form
{
    private readonly List<(CheckBox Box, string MetricId)> _boxes = [];
    private readonly Action<IReadOnlySet<string>> _apply;
    private readonly Font _boldFont;

    public MetricsSettingsForm(AppConfig config, Action<IReadOnlySet<string>> apply)
    {
        _apply = apply;

        Text = "SpecsView — métricas exibidas";
        Font = SystemFonts.MessageBoxFont;
        _boldFont = new Font(Font, FontStyle.Bold);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        root.Controls.Add(Note("Escolha o que aparece na barra de tarefas."));
        root.Controls.Add(BuildGrid(ItemPresets.Selected(config)));

        if (HardwareProvider.AccessProblem is { } problem)
        {
            root.Controls.Add(Note($"Temperatura e potência da CPU aparecem como \"—\" até isso ser resolvido:\n{problem}"));
        }

        root.Controls.Add(Note("Itens adicionados à mão no config.json são mantidos."));
        root.Controls.Add(BuildButtons());
        Controls.Add(root);
    }

    private TableLayoutPanel BuildGrid(IReadOnlySet<string> selected)
    {
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 8, 0, 8) };
        // Presets sharing a name (download and upload are two items) share a row.
        foreach (var group in ItemPresets.All.GroupBy(p => p.Name))
        {
            grid.Controls.Add(new Label
            {
                Text = group.Key,
                AutoSize = true,
                Font = _boldFont,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 3, 16, 3),
            });

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            foreach (var option in group.SelectMany(p => p.Options))
            {
                var box = new CheckBox
                {
                    Text = option.Name,
                    AutoSize = true,
                    Checked = selected.Contains(option.MetricId),
                };
                _boxes.Add((box, option.MetricId));
                row.Controls.Add(box);
            }

            grid.Controls.Add(row);
        }

        return grid;
    }

    private FlowLayoutPanel BuildButtons()
    {
        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancelar", AutoSize = true };
        var apply = new Button { Text = "Aplicar", AutoSize = true };

        // Shown modeless (the app has no main window), so the buttons close it themselves.
        ok.Click += (_, _) =>
        {
            Apply();
            Close();
        };
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) => Apply();
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.AddRange([apply, cancel, ok]);
        return buttons;
    }

    private void Apply() =>
        _apply(_boxes.Where(b => b.Box.Checked).Select(b => b.MetricId).ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(420, 0) };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
