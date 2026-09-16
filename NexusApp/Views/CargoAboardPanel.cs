using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusApp.Models.Cargo;
using NexusApp.Services;

namespace NexusApp.Views;

/// <summary>
/// Manual editor for cargo believed to be aboard the active hangar ship.
/// Add a 0 SCU lot, then name it, set Destination as a note, and pick crate counts.
/// Off-Grid and Contracted are lot-row pills. Counts clamp to cargo already aboard.
/// Does not write hauling contracts. COMMITTED on this page stays obligation math.
/// </summary>
internal sealed class CargoAboardPanel : UserControl
{

    private readonly TextBlock _sub = new();
    private readonly TextBlock _capacity = new();
    private readonly StackPanel _lots = new();
    private readonly TextBlock _empty = new();
    private readonly Button _addBtn = new();
    private readonly Button _clearBtn = new();
    private readonly StackPanel _form = new();
    private readonly List<LotRow> _rows = new();
    private int _lotGen;
    private bool _keepRows;

    public CargoAboardPanel()
    {
        Build();
        Refresh();
        App.GameState.CargoChanged += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Refresh(); });
        App.GameState.ActiveShipChanged += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Refresh(); });
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    public void Refresh()
    {
        var cargo = App.GameState.Cargo;
        var ship = App.GameState.ActiveShip;

        if (!ship.HasShip)
        {
            _lotGen++;
            _sub.Text = "set an active ship in My Hangar";
            _capacity.Text = "No active ship. Confirm a hull in Ships, then add what is aboard.";
            _capacity.Foreground = Hud.Br("FgDimBrush");
            _lots.Children.Clear();
            _rows.Clear();
            _empty.Visibility = Visibility.Collapsed;
            _form.IsEnabled = false;
            _clearBtn.Visibility = Visibility.Collapsed;
            _keepRows = false;
            return;
        }

        var (grids, trade) = Hull();
        _sub.Text = cargo.DisplayName ?? ship.DisplayName ?? ship.ShipId ?? "";
        _capacity.Text = CapacityText(cargo);
        _capacity.Foreground = cargo.IsOverCapacity
            ? Hud.Br("WarnBrush")
            : Hud.Br("FgBrush");
        _form.IsEnabled = true;
        _clearBtn.Visibility = cargo.HasLots ? Visibility.Visible : Visibility.Collapsed;

        if (_keepRows)
        {
            _keepRows = false;
            ReclampRows(cargo);
            return;
        }

        _lotGen++;
        _lots.Children.Clear();
        _rows.Clear();
        if (!cargo.HasLots)
        {
            _empty.Text = "Nothing recorded aboard. Add a lot, then set the commodity and crate counts.";
            _empty.Visibility = Visibility.Visible;
        }
        else
        {
            _empty.Visibility = Visibility.Collapsed;
            foreach (var group in GroupLots(cargo.Lots))
            {
                var row = CommodityRow(group, cargo.Lots, grids, trade, _lotGen);
                _rows.Add(row);
                _lots.Children.Add(row.Root);
            }
        }

        FillPickers();
    }

    private void Build()
    {
        var stack = new StackPanel();
        stack.Children.Add(HeaderBar());

        var body = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
        _capacity.FontFamily = Hud.Font("DisplayFont");
        _capacity.FontSize = 22;
        _capacity.Foreground = Hud.Br("FgBrush");
        _capacity.Margin = new Thickness(0, 0, 0, 10);
        _capacity.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(_capacity);

        _empty.FontSize = 12;
        _empty.Foreground = Hud.Br("FgDimBrush");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(_empty);
        body.Children.Add(_lots);
        body.Children.Add(BuildForm());
        stack.Children.Add(body);

        var panel = Hud.Panel(stack, padding: new Thickness(0));
        panel.Margin = new Thickness(0, 0, 0, 14);
        Content = panel;
    }

    private UIElement HeaderBar()
    {
        var wrap = new StackPanel();
        var bar = new Grid { Margin = new Thickness(14, 11, 14, 9) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "ABOARD", FontFamily = Hud.Font("HeadFont"), FontSize = 12.5,
            FontWeight = FontWeights.Bold, Foreground = Hud.Br("FgBrush"),
        });
        _sub.FontSize = 10.5;
        _sub.Foreground = Hud.Br("FgDimBrush");
        title.Children.Add(_sub);
        Grid.SetColumn(title, 0);
        bar.Children.Add(title);

        _clearBtn.Content = "Clear aboard";
        _clearBtn.Style = (Style)Application.Current.FindResource("NexusButton");
        _clearBtn.Padding = new Thickness(12, 6, 12, 6);
        _clearBtn.FontWeight = FontWeights.SemiBold;
        _clearBtn.FontSize = 12;
        _clearBtn.Click += (_, _) => ClearAboard();
        Grid.SetColumn(_clearBtn, 1);
        bar.Children.Add(_clearBtn);

        wrap.Children.Add(bar);
        wrap.Children.Add(new Border { Height = 1, Background = Hud.Br("NavBorderBrush") });
        return wrap;
    }

    private UIElement BuildForm()
    {
        _form.Margin = new Thickness(0, 8, 0, 0);
        _addBtn.Content = "Add lot";
        _addBtn.Style = (Style)Application.Current.FindResource("NexusButton");
        _addBtn.Padding = new Thickness(14, 6, 14, 6);
        _addBtn.FontWeight = FontWeights.SemiBold;
        _addBtn.FontSize = 12;
        _addBtn.HorizontalAlignment = HorizontalAlignment.Left;
        _addBtn.Click += (_, _) => AddLot();
        _form.Children.Add(_addBtn);
        _form.Children.Add(new TextBlock
        {
            Text = "Add a 0 SCU lot, then set the commodity and crate counts. Destination is a note for now; it is not linked to a stop. Off-Grid and Contracted sit on the lot row. On-grid counts clamp to space left with cargo already aboard. Off-Grid is if-it-fits-it-ships: holds are often larger than the cargo grid, so those counts are not clamped to the grid. Crates on-grid lie flat only (32 / 24 / 16 / 8 / 4 / 2 / 1 SCU). Contract cards below are obligations, not proof of cargo.",
            FontSize = 11, Foreground = Hud.Br("FgDimBrush"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return _form;
    }

    private LotRow CommodityRow(
        IGrouping<string, GameCargoLot> group,
        IReadOnlyList<GameCargoLot> all,
        ShipCargoDef? grids,
        TradeShip? trade,
        int gen)
    {
        var lots = group.ToList();
        var box = new Border
        {
            BorderBrush = Hud.Br("NavBorderBrush"), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8),
        };
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var picker = new CommodityPickerBox
        {
            Width = 176, MaxWidth = 176, MinWidth = 140,
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        picker.Text = group.Key;
        Grid.SetColumn(picker, 0);
        head.Children.Add(picker);

        var offGrid = new FlagPill(
            "Off-Grid",
            CargoLots.AnyOffGrid(lots),
            "If it fits, it ships: record crates in the hold even when they are not on the cargo grid. Holds are often larger than the grid for maneuvering.");
        Grid.SetColumn(offGrid.Root, 1);
        head.Children.Add(offGrid.Root);

        var contracted = new FlagPill(
            "Contracted",
            CargoLots.AnyContracted(lots),
            "This lot is for a haul. Does not create or bind a haul card.");
        Grid.SetColumn(contracted.Root, 2);
        head.Children.Add(contracted.Root);

        var scu = lots.Sum(l => l.Scu);
        var remove = new Button
        {
            Content = "x", FontFamily = Hud.Font("MonoFont"), FontSize = 14, FontWeight = FontWeights.Bold,
            Style = (Style)Application.Current.FindResource("NexusButton"),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2), Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Remove this lot", VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += (_, _) =>
        {
            if (gen != _lotGen) return;
            var shipId = App.GameState.ActiveShip.ShipId;
            if (string.IsNullOrWhiteSpace(shipId)) return;
            App.Data.RemoveCommodity(shipId, group.Key);
            Logger.Info($"[UI] cargo aboard remove: {scu} SCU {group.Key}");
        };
        Grid.SetColumn(remove, 4);
        head.Children.Add(remove);
        stack.Children.Add(head);

        var meta = new Grid { Margin = new Thickness(0, 8, 0, 6) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var destBox = new TextBox
        {
            Style = (Style)Application.Current.FindResource("NexusTextBox"),
            Tag = "Destination",
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "A note for where this lot is going. Not linked to a contract stop yet.",
        };
        destBox.Text = CargoLots.FirstDestination(lots);
        Grid.SetColumn(destBox, 0);
        meta.Children.Add(destBox);

        var scuLabel = new TextBlock
        {
            FontFamily = Hud.Font("MonoFont"), FontSize = 10,
            Foreground = Hud.Br("FgDimBrush"), TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        scuLabel.Text = ScuLabelText(lots, scu);
        Grid.SetColumn(scuLabel, 1);
        meta.Children.Add(scuLabel);
        stack.Children.Add(meta);

        var (packed, volume) = OthersOccupancy(all, group.Key);
        var strip = new CargoCrateStrip { Margin = new Thickness(0, 0, 0, 0) };
        strip.Bind(grids, trade, CargoLots.CountsBySize(lots), CargoLots.AnyOffGrid(lots), packed, volume);
        var row = new LotRow(group.Key, box, picker, destBox, scuLabel, strip, offGrid, contracted);

        picker.Committed += name => RenameRow(row, gen, name);
        picker.InteractionEnded += () => RenameRow(row, gen, picker.Text);

        void Save()
        {
            if (gen != _lotGen) return;
            var shipId = App.GameState.ActiveShip.ShipId;
            if (string.IsNullOrWhiteSpace(shipId)) return;
            _keepRows = true;
            App.Data.SetCommodityCrates(
                shipId, row.Commodity, strip.Counts(), row.OffGrid, row.Contracted, row.Destination);
            scuLabel.Text = ScuLabelText(Array.Empty<GameCargoLot>(), strip.TotalScu());
        }

        offGrid.Changed += () =>
        {
            strip.SetOffGrid(offGrid.IsOn);
            Save();
        };
        contracted.Changed += Save;
        destBox.LostKeyboardFocus += (_, _) => Save();
        destBox.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            Save();
            e.Handled = true;
        };
        strip.Changed += Save;
        stack.Children.Add(strip);
        box.Child = stack;
        return row;
    }

    private void RenameRow(LotRow row, int gen, string? name)
    {
        if (gen != _lotGen) return;
        var shipId = App.GameState.ActiveShip.ShipId;
        if (string.IsNullOrWhiteSpace(shipId)) return;
        var next = (name ?? "").Trim();
        if (CargoLots.SameCommodity(row.Commodity, next)) return;
        App.Data.RenameCommodity(shipId, row.Commodity, next);
        Logger.Info($"[UI] cargo aboard rename: '{row.Commodity}' -> '{next}'");
    }

    private void AddLot()
    {
        var ship = App.GameState.ActiveShip;
        if (!ship.HasShip || string.IsNullOrWhiteSpace(ship.ShipId)) return;
        App.Data.AddEmptyLot(ship.ShipId);
        Logger.Info($"[UI] cargo aboard add lot: {ship.ShipId}");
    }

    private void ClearAboard()
    {
        var ship = App.GameState.ActiveShip;
        if (!ship.HasShip || string.IsNullOrWhiteSpace(ship.ShipId)) return;
        App.Data.ClearCargoForShip(ship.ShipId);
        Logger.Info($"[UI] cargo aboard clear: {ship.ShipId}");
    }

    private void ReclampRows(GameCargoState cargo)
    {
        foreach (var row in _rows)
        {
            var (packed, volume) = OthersOccupancy(cargo.Lots, row.Commodity);
            row.Strip.Reclamp(packed, volume);
            row.ScuLabel.Text = ScuLabelText(
                cargo.Lots.Where(l => CargoLots.SameCommodity(l.Commodity, row.Commodity)).ToList(),
                row.Strip.TotalScu());
        }
        FillPickers();
    }

    private void FillPickers()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in App.Data.GetAllResources())
        {
            if (!string.IsNullOrWhiteSpace(resource.Name))
                names.Add(resource.Name);
        }
        foreach (var row in App.Market.Commodities.Items)
        {
            if (!string.IsNullOrWhiteSpace(row.Name))
                names.Add(row.Name);
        }
        var list = names.ToList();
        foreach (var row in _rows)
            row.Picker.SetItems(list);
    }

    private static (Dictionary<int, int> Packed, int Volume) OthersOccupancy(
        IEnumerable<GameCargoLot> lots, string commodity)
    {
        var others = CargoLots.Others(lots, commodity).ToList();
        return (CargoLots.PackedCounts(others), others.Sum(l => l.Scu));
    }

    private static (ShipCargoDef? Grids, TradeShip? Trade) Hull()
    {
        var id = App.GameState.ActiveShip.ShipId;
        return (ShipCatalogIds.ToCargoShip(id), ShipCatalogIds.ToTradeShip(id));
    }

    private static IEnumerable<IGrouping<string, GameCargoLot>> GroupLots(IReadOnlyList<GameCargoLot> lots) =>
        lots.GroupBy(l => l.Commodity, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => string.IsNullOrWhiteSpace(g.Key) ? "" : g.Key, StringComparer.OrdinalIgnoreCase);

    private static string ScuLabelText(IReadOnlyList<GameCargoLot> lots, int scu)
    {
        var extra = lots.Count > 0 ? CargoLots.UnspecifiedScu(lots) : 0;
        if (extra > 0)
            return $"{extra} SCU recorded without crate sizes. Pick crate counts below to replace it.";
        if (scu <= 0)
            return "0 SCU · pick a commodity, then the crates on board.";
        return $"{scu:N0} SCU";
    }

    private static string CapacityText(GameCargoState cargo)
    {
        if (cargo.UsableScu is int usable)
        {
            var free = cargo.FreeScu ?? usable - cargo.UsedScu;
            var over = cargo.IsOverCapacity
                ? (cargo.Lots.Any(l => l.OffGrid) ? " · over grid rating (off-grid)" : " · over capacity")
                : "";
            return $"{cargo.UsedScu:N0} / {usable:N0} SCU used · {free:N0} free{over}";
        }
        return cargo.UsedScu > 0
            ? $"{cargo.UsedScu:N0} SCU used · usable capacity unknown"
            : "Usable capacity unknown";
    }

    private sealed class FlagPill
    {
        public FlagPill(string label, bool on, string tip)
        {
            IsOn = on;
            var text = new TextBlock
            {
                Text = label, FontFamily = Hud.Font("UiFont"), FontSize = 10.5, FontWeight = FontWeights.Bold,
            };
            Root = new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tip, Child = text,
            };
            Paint();
            Root.MouseLeftButtonUp += (_, _) =>
            {
                IsOn = !IsOn;
                Paint();
                Changed?.Invoke();
            };
        }

        public Border Root { get; }
        public bool IsOn { get; private set; }
        public event Action? Changed;

        private void Paint()
        {
            var text = (TextBlock)Root.Child;
            if (IsOn)
            {
                text.Foreground = Hud.Br("OnCyanBrush");
                Root.BorderBrush = Hud.Br("CyanBrush");
                Root.Background = Hud.Br("CyanBrush");
            }
            else
            {
                text.Foreground = Hud.Br("CyanBrush");
                Root.BorderBrush = Hud.Br("CyanStrongBrush");
                Root.Background = Hud.Br("CyanDimBrush");
            }
        }
    }

    private sealed class LotRow
    {
        public LotRow(
            string commodity,
            UIElement root,
            CommodityPickerBox picker,
            TextBox destination,
            TextBlock scuLabel,
            CargoCrateStrip strip,
            FlagPill offGrid,
            FlagPill contracted)
        {
            Commodity = commodity;
            Root = root;
            Picker = picker;
            DestinationBox = destination;
            ScuLabel = scuLabel;
            Strip = strip;
            OffGridPill = offGrid;
            ContractedPill = contracted;
        }

        public string Commodity { get; set; }
        public UIElement Root { get; }
        public CommodityPickerBox Picker { get; }
        public TextBox DestinationBox { get; }
        public TextBlock ScuLabel { get; }
        public CargoCrateStrip Strip { get; }
        public FlagPill OffGridPill { get; }
        public FlagPill ContractedPill { get; }
        public bool OffGrid => OffGridPill.IsOn;
        public bool Contracted => ContractedPill.IsOn;
        public string Destination => DestinationBox.Text ?? "";
    }
}
