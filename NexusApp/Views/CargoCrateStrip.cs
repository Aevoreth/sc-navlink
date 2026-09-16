using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusApp.Models.Cargo;
using NexusApp.Services;

namespace NexusApp.Views;

/// <summary>
/// Count dropdowns for the seven hauling crate sizes. On-grid sizes that cannot lie
/// flat stay disabled unless Off-Grid is on. Off-Grid does not clamp to the cargo grid.
/// The Off-Grid flag lives on the lot row; this strip only applies it to fit.
/// </summary>
internal sealed class CargoCrateStrip : StackPanel
{
    public event Action? Changed;

    private readonly Dictionary<int, ComboBox> _boxes = new();
    private bool _suppress;
    private ShipCargoDef? _grids;
    private TradeShip? _trade;
    private Dictionary<int, int> _othersPacked = CargoCrateFit.EmptyCounts();
    private int _othersVolume;

    public CargoCrateStrip()
    {
        Orientation = Orientation.Vertical;
        var row = new WrapPanel();
        foreach (var size in CargoCrateFit.Sizes)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 6) };
            cell.Children.Add(new TextBlock
            {
                Text = $"{size}", FontFamily = Hud.Font("MonoFont"), FontSize = 10,
                Foreground = Hud.Br("FgDimBrush"), HorizontalAlignment = HorizontalAlignment.Center,
            });
            var combo = new ComboBox
            {
                MinWidth = 52, FontFamily = Hud.Font("MonoFont"), FontSize = 12,
            };
            if (Application.Current.TryFindResource("NexusComboBox") is Style style)
                combo.Style = style;
            combo.SelectionChanged += (_, _) =>
            {
                if (!_suppress) Changed?.Invoke();
            };
            _boxes[size] = combo;
            cell.Children.Add(combo);
            row.Children.Add(cell);
        }
        Children.Add(row);
    }

    public bool OffGrid { get; private set; }

    public void SetOffGrid(bool on)
    {
        if (OffGrid == on) return;
        OffGrid = on;
        RebuildBoxes();
    }

    public Dictionary<int, int> Counts()
    {
        var counts = CargoCrateFit.EmptyCounts();
        foreach (var size in CargoCrateFit.Sizes)
        {
            if (_boxes[size].SelectedItem is int n && n > 0)
                counts[size] = n;
        }
        return counts;
    }

    public int TotalScu()
    {
        var total = 0;
        foreach (var (size, n) in Counts())
            total += size * n;
        return total;
    }

    public void Bind(
        ShipCargoDef? grids,
        TradeShip? trade,
        IReadOnlyDictionary<int, int> counts,
        bool offGrid,
        IReadOnlyDictionary<int, int>? othersPacked = null,
        int othersVolume = 0)
    {
        _suppress = true;
        _grids = grids;
        _trade = trade;
        _othersPacked = CopyCounts(othersPacked);
        _othersVolume = othersVolume;
        OffGrid = offGrid;
        var mine = CopyCounts(counts);
        foreach (var size in CargoCrateFit.Sizes)
        {
            mine.TryGetValue(size, out var n);
            FillBox(size, n, mine);
        }
        _suppress = false;
    }

    public void Reclamp(IReadOnlyDictionary<int, int>? othersPacked, int othersVolume)
    {
        _othersPacked = CopyCounts(othersPacked);
        _othersVolume = othersVolume;
        var before = Counts();
        _suppress = true;
        foreach (var size in CargoCrateFit.Sizes)
        {
            before.TryGetValue(size, out var n);
            FillBox(size, n, before);
        }
        var after = Counts();
        _suppress = false;
        if (!CountsEqual(before, after))
            Changed?.Invoke();
    }

    private void RebuildBoxes()
    {
        var current = Counts();
        _suppress = true;
        foreach (var size in CargoCrateFit.Sizes)
        {
            current.TryGetValue(size, out var n);
            FillBox(size, n, current);
        }
        _suppress = false;
    }

    private void FillBox(int size, int selected, IReadOnlyDictionary<int, int> mine)
    {
        var hullFits = CargoCrateFit.SizeFits(_grids, _trade, size);
        var packed = OffGrid
            ? CargoCrateFit.EmptyCounts()
            : CargoCrateFit.WithoutSize(Merge(_othersPacked, mine), size);
        var volumeUsed = OffGrid
            ? 0
            : _othersVolume + CargoCrateFit.VolumeOf(CargoCrateFit.WithoutSize(mine, size));
        var max = CargoCrateFit.MaxCount(_grids, _trade, size, OffGrid, packed, volumeUsed);
        if (selected < 0) selected = 0;
        if (OffGrid)
        {
            if (selected > max) max = selected;
        }
        else if (selected > max)
        {
            selected = max;
        }

        var enabled = OffGrid || (hullFits && max > 0) || selected > 0;
        var combo = _boxes[size];
        if (combo.Items.Count == max + 1
            && combo.SelectedItem is int cur && cur == selected
            && combo.IsEnabled == enabled)
        {
            combo.Opacity = enabled ? 1 : 0.45;
            combo.ToolTip = Tip(size, max, hullFits, enabled);
            return;
        }

        combo.Items.Clear();
        for (var i = 0; i <= max; i++)
            combo.Items.Add(i);
        combo.SelectedItem = selected;
        combo.IsEnabled = enabled;
        combo.Opacity = enabled ? 1 : 0.45;
        combo.ToolTip = Tip(size, max, hullFits, enabled);
    }

    private string Tip(int size, int max, bool hullFits, bool enabled)
    {
        if (!enabled && !hullFits)
            return $"{size} SCU crates do not lie flat on this hull's cargo grid. Turn on Off-Grid to record them in the hold.";
        if (!enabled)
            return $"{size} SCU crates do not fit on-grid with cargo already aboard. Turn on Off-Grid to record them in the hold.";
        if (OffGrid)
            return $"{size} SCU crates · Off-Grid (if it fits, it ships). Not clamped to the cargo grid.";
        return $"{size} SCU crates · {max} fit on-grid with cargo already aboard";
    }

    private static Dictionary<int, int> CopyCounts(IReadOnlyDictionary<int, int>? counts)
    {
        var copy = CargoCrateFit.EmptyCounts();
        if (counts is null) return copy;
        foreach (var (size, n) in counts)
        {
            if (n > 0 && copy.ContainsKey(size)) copy[size] = n;
        }
        return copy;
    }

    private static Dictionary<int, int> Merge(IReadOnlyDictionary<int, int> a, IReadOnlyDictionary<int, int> b)
    {
        var copy = CopyCounts(a);
        foreach (var (size, n) in b)
        {
            if (n > 0 && copy.ContainsKey(size)) copy[size] += n;
        }
        return copy;
    }

    private static bool CountsEqual(IReadOnlyDictionary<int, int> a, IReadOnlyDictionary<int, int> b)
    {
        foreach (var size in CargoCrateFit.Sizes)
        {
            a.TryGetValue(size, out var x);
            b.TryGetValue(size, out var y);
            if (x != y) return false;
        }
        return true;
    }
}
