using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using NexusApp.Services;

namespace NexusApp.Views;

/// <summary>
/// Ships module: Browser, My Hangar, Loadout skeleton. Code-built like TradePage.
/// Click a Browser card or Hangar row to open a read-only hull detail pane.
/// </summary>
public sealed class ShipsPage : UserControl
{
    private static readonly string[] TabLabels = ["Browser", "My Hangar", "Loadout"];
    private const double CardWidth = 248;
    private const double ThumbHeight = 130;
    private const int ThumbDecodeWidth = 400;
    private const int DetailDecodeWidth = 800;
    private const double DetailHeroHeight = 220;

    private readonly Border[] _tabButtons = new Border[3];
    private readonly TextBlock[] _tabLabels = new TextBlock[3];
    private readonly FrameworkElement[] _panes = new FrameworkElement[3];
    private readonly TranslateTransform _underlineT = new();
    private Grid _stripHost = null!;
    private Grid _paneHost = null!;
    private Border _underline = null!;
    private int _activeIndex = -1;

    private readonly Grid _browserHost = new() { Name = "ShipsBrowser" };
    private readonly Grid _hangarHost = new() { Name = "ShipsHangar" };
    private readonly Grid _loadoutHost = new() { Name = "ShipsLoadout" };
    private readonly Grid _detailHost = new() { Name = "ShipsDetail" };

    private TextBox _searchBox = null!;
    private CheckBox _includeConcept = null!;
    private CheckBox _includeGround = null!;
    private WrapPanel _mfrChips = null!;
    private WrapPanel _roleChips = null!;
    private WrapPanel _cardGrid = null!;
    private TextBlock _browserBanner = null!;
    private StackPanel _hangarList = null!;
    private TextBlock _hangarBanner = null!;
    private StackPanel _detailBody = null!;

    private ShipCatalogQuery _query = new();
    private CancellationTokenSource? _thumbCts;
    private CancellationTokenSource? _detailCts;
    private string? _detailId;
    private int _hangarGen;
    private int _detailGen;

    public ShipsPage()
    {
        Focusable = true;
        var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = "HANGAR", Style = (Style)Application.Current.FindResource("Eyebrow") });
        header.Children.Add(new TextBlock { Text = "Ships", Style = (Style)Application.Current.FindResource("PageTitle") });
        header.Children.Add(new TextBlock
        {
            Text = "Browse the catalog, record what you own, and confirm the active ship.",
            Style = (Style)Application.Current.FindResource("PageSubtitle"),
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        BuildStrip();
        Grid.SetRow(_stripHost, 1);
        root.Children.Add(_stripHost);

        _paneHost = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        _panes[0] = _browserHost;
        _panes[1] = _hangarHost;
        _panes[2] = _loadoutHost;
        BuildBrowser();
        BuildHangar();
        BuildLoadout();
        foreach (var pane in _panes)
        {
            pane.Visibility = Visibility.Collapsed;
            _paneHost.Children.Add(pane);
        }
        Grid.SetRow(_paneHost, 2);
        root.Children.Add(_paneHost);

        BuildDetail();
        Grid.SetRow(_detailHost, 2);
        root.Children.Add(_detailHost);
        Content = root;

        int restore = Array.IndexOf(ShipsFlows.Ids, ShipsFlows.NormalizeForRestore(App.Settings.Current.ShipsActiveFlow));
        SwitchTab(restore < 0 ? 0 : restore, persist: false);

        _stripHost.Loaded += (_, _) => MoveUnderline(_activeIndex);
        _stripHost.SizeChanged += (_, _) => MoveUnderline(_activeIndex);

        App.Market.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Refresh(); });
        App.GameState.ActiveShipChanged += () => Dispatcher.BeginInvoke(() => { if (IsVisible) RefreshHangar(); });
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
            else
            {
                _thumbCts?.Cancel();
                _detailCts?.Cancel();
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _detailId is null) return;
            e.Handled = true;
            CloseDetails();
        };
    }

    public void Refresh()
    {
        if (_detailId is not null) RefreshDetails();
        else RefreshBrowser();
        RefreshHangar();
    }

    private void BuildStrip()
    {
        _stripHost = new Grid { Height = 42 };
        var hairline = new Border { Height = 1, Background = Hud.Br("NavBorderBrush"), VerticalAlignment = VerticalAlignment.Bottom };
        _stripHost.Children.Add(hairline);
        var cluster = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        for (int i = 0; i < TabLabels.Length; i++) cluster.Children.Add(MakeTab(i, TabLabels[i]));
        _stripHost.Children.Add(cluster);
        _underline = new Border
        {
            Height = 2, Width = 0, CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Hud.Br("GoldBrush"), RenderTransform = _underlineT,
            Effect = new DropShadowEffect { Color = Hud.Col("AccentColor"), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5 },
        };
        _stripHost.Children.Add(_underline);
    }

    private Border MakeTab(int index, string label)
    {
        var text = new TextBlock
        {
            Text = label.ToUpperInvariant(), FontFamily = Hud.Font("UiFont"), FontSize = 12,
            FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Hud.Br("FgDimBrush"),
        };
        _tabLabels[index] = text;
        var btn = new Border
        {
            Background = Brushes.Transparent, CornerRadius = new CornerRadius(3, 3, 0, 0),
            Padding = new Thickness(15, 9, 15, 9), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Bottom, Child = text,
        };
        _tabButtons[index] = btn;
        btn.MouseEnter += (_, _) => { text.Foreground = Hud.Br("FgBrush"); btn.Background = Hud.Br("AccentFaintBrush"); };
        btn.MouseLeave += (_, _) => { text.Foreground = TabColor(index); btn.Background = Brushes.Transparent; };
        btn.MouseLeftButtonUp += (_, _) => SwitchTab(index);
        return btn;
    }

    private Brush TabColor(int index) => index == _activeIndex ? Hud.Br("GoldBrush") : Hud.Br("FgDimBrush");

    private void SwitchTab(int index, bool persist = true)
    {
        if (index == _activeIndex && _panes[index].Visibility == Visibility.Visible) return;
        _activeIndex = index;
        for (int i = 0; i < _panes.Length; i++) _panes[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _tabLabels.Length; i++) _tabLabels[i].Foreground = TabColor(i);
        MoveUnderline(index);
        if (persist)
        {
            App.Settings.Current.ShipsActiveFlow = ShipsFlows.Ids[index];
            App.Settings.Save();
        }
        if (index == 0) RefreshBrowser();
        if (index == 1) RefreshHangar();
    }

    private void MoveUnderline(int index)
    {
        if (_stripHost is null || !_stripHost.IsLoaded || index < 0) return;
        var t = _tabButtons[index];
        double x = t.TransformToAncestor(_stripHost).Transform(default).X;
        _underline.Width = t.ActualWidth;
        _underlineT.X = x;
    }

    private void BuildBrowser()
    {
        _browserHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _browserHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var filters = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _searchBox = new TextBox
        {
            Style = (Style)Application.Current.FindResource("NexusTextBox"),
            Width = 220, Tag = "Search ships…",
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _query = _query with { Search = _searchBox.Text };
            RefreshBrowser();
        };
        searchRow.Children.Add(_searchBox);
        _includeConcept = FilterCheck("Include concept", false);
        _includeConcept.Checked += (_, _) => { _query = _query with { IncludeConcept = true }; RefreshBrowser(); };
        _includeConcept.Unchecked += (_, _) => { _query = _query with { IncludeConcept = false }; RefreshBrowser(); };
        _includeGround = FilterCheck("Include ground", false);
        _includeGround.Checked += (_, _) => { _query = _query with { IncludeGround = true }; RefreshBrowser(); };
        _includeGround.Unchecked += (_, _) => { _query = _query with { IncludeGround = false }; RefreshBrowser(); };
        searchRow.Children.Add(_includeConcept);
        searchRow.Children.Add(_includeGround);
        filters.Children.Add(searchRow);
        filters.Children.Add(ChipRow("Manufacturer", out _mfrChips));
        filters.Children.Add(ChipRow("Role", out _roleChips));
        Grid.SetRow(filters, 0);
        _browserHost.Children.Add(filters);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel();
        _browserBanner = new TextBlock
        {
            FontFamily = Hud.Font("UiFont"), FontSize = 12, Foreground = Hud.Br("FgDimBrush"),
            Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(_browserBanner);
        _cardGrid = new WrapPanel();
        body.Children.Add(_cardGrid);
        scroll.Content = body;
        Grid.SetRow(scroll, 1);
        _browserHost.Children.Add(scroll);
    }

    private static CheckBox FilterCheck(string label, bool on) =>
        new()
        {
            Content = label, IsChecked = on, Margin = new Thickness(16, 0, 0, 0),
            Foreground = Hud.Br("FgDimBrush"), VerticalAlignment = VerticalAlignment.Center,
        };

    private static FrameworkElement ChipRow(string label, out WrapPanel chips)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        col.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(), FontFamily = Hud.Font("UiFont"), FontSize = 10,
            FontWeight = FontWeights.Bold, Foreground = Hud.Br("FgDimBrush"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        chips = new WrapPanel();
        col.Children.Add(chips);
        return col;
    }

    private void RebuildFilterChips()
    {
        var (manufacturers, roles) = ShipCatalogQueries.Facets(
            App.Market.Vehicles.Items, _query.IncludeConcept, _query.IncludeGround);
        FillChipRow(_mfrChips, manufacturers, _query.Manufacturer, SetManufacturer);
        FillChipRow(_roleChips, roles, _query.Role, SetRole);
    }

    private void FillChipRow(WrapPanel host, IReadOnlyList<string> values, string? selected, Action<string?> set)
    {
        host.Children.Clear();
        host.Children.Add(FilterChip("All", string.IsNullOrEmpty(selected), () => set(null)));
        foreach (var value in values)
        {
            var captured = value;
            var on = string.Equals(selected, captured, StringComparison.OrdinalIgnoreCase);
            host.Children.Add(FilterChip(captured, on, () => set(on ? null : captured)));
        }
    }

    private static Border FilterChip(string label, bool on, Action click)
    {
        var text = new TextBlock
        {
            Text = label, FontFamily = Hud.Font("UiFont"), FontSize = 10.5, FontWeight = FontWeights.Bold,
            Foreground = on ? Hud.Br("AccentBrush") : Hud.Br("FgDimBrush"),
        };
        var chip = new Border
        {
            Background = on ? Hud.Br("AccentFaintBrush") : Hud.Br("Bg2NavBrush"),
            BorderBrush = on ? Hud.Br("AccentStrongBrush") : Hud.Br("NavBorderBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 6), Child = text, VerticalAlignment = VerticalAlignment.Center,
        };
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return chip;
    }

    private void SetManufacturer(string? value)
    {
        var next = string.IsNullOrWhiteSpace(value) ? null : value;
        if (string.Equals(_query.Manufacturer, next, StringComparison.OrdinalIgnoreCase)) return;
        _query = _query with { Manufacturer = next };
        RefreshBrowser();
    }

    private void SetRole(string? value)
    {
        var next = string.IsNullOrWhiteSpace(value) ? null : value;
        if (string.Equals(_query.Role, next, StringComparison.OrdinalIgnoreCase)) return;
        _query = _query with { Role = next };
        RefreshBrowser();
    }

    private void BuildHangar()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel();
        _hangarBanner = new TextBlock
        {
            FontFamily = Hud.Font("UiFont"), FontSize = 12, Foreground = Hud.Br("FgDimBrush"),
            Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(_hangarBanner);
        _hangarList = new StackPanel();
        body.Children.Add(_hangarList);
        scroll.Content = body;
        _hangarHost.Children.Add(scroll);
    }

    private void BuildLoadout()
    {
        _loadoutHost.Children.Add(new TextBlock
        {
            Text = "Loadout Calculator is reserved here. Full component simulation lands in 0.6 (#38).",
            FontFamily = Hud.Font("UiFont"), FontSize = 13, Foreground = Hud.Br("FgDimBrush"),
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top,
        });
    }

    private void BuildDetail()
    {
        _detailHost.Visibility = Visibility.Collapsed;
        _detailHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _detailHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var back = new TextBlock
        {
            Text = "←  Back", FontFamily = Hud.Font("UiFont"), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Hud.Br("AccentBrush"), Cursor = Cursors.Hand, Margin = new Thickness(0, 16, 0, 12),
        };
        back.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseDetails(); };
        Grid.SetRow(back, 0);
        _detailHost.Children.Add(back);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _detailBody = new StackPanel();
        scroll.Content = _detailBody;
        Grid.SetRow(scroll, 1);
        _detailHost.Children.Add(scroll);
    }

    private void OpenDetails(string catalogId)
    {
        if (string.IsNullOrWhiteSpace(catalogId)) return;
        _detailId = catalogId;
        _stripHost.Visibility = Visibility.Collapsed;
        _paneHost.Visibility = Visibility.Collapsed;
        _detailHost.Visibility = Visibility.Visible;
        RefreshDetails();
        Focus();
        Logger.Info($"[UI] ships: detail {catalogId}");
    }

    private void CloseDetails()
    {
        _detailCts?.Cancel();
        _detailId = null;
        _detailHost.Visibility = Visibility.Collapsed;
        _stripHost.Visibility = Visibility.Visible;
        _paneHost.Visibility = Visibility.Visible;
        MoveUnderline(_activeIndex);
        Refresh();
    }

    private void RefreshDetails()
    {
        if (_detailId is null) return;
        _detailCts?.Cancel();
        _detailCts = new CancellationTokenSource();
        _detailGen++;
        _detailBody.Children.Clear();

        var ship = App.Market.ById(_detailId);
        if (ship is null)
        {
            _detailBody.Children.Add(Dim($"No catalog row for {_detailId}. Enable market data, then refresh."));
            return;
        }

        var ct = _detailCts.Token;
        _detailBody.Children.Add(BuildDetailHero(ship, ct));
        _detailBody.Children.Add(new TextBlock
        {
            Text = ship.DisplayName, FontFamily = Hud.Font("UiFont"), FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = Hud.Br("FgBrush"), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        _detailBody.Children.Add(new TextBlock
        {
            Text = ship.Manufacturer, FontFamily = Hud.Font("UiFont"), FontSize = 13,
            Foreground = Hud.Br("FgDimBrush"), Margin = new Thickness(0, 4, 0, 0),
        });
        _detailBody.Children.Add(new TextBlock
        {
            Text = $"{ShipDetailCopy.StatusBadge(ship)}  ·  {ShipDetailCopy.Cargo(ship)}  ·  {ShipDetailCopy.RoleLine(ship)}",
            FontFamily = Hud.Font("MonoFont"), FontSize = 11, Foreground = Hud.Br("AccentBrush"),
            Margin = new Thickness(0, 8, 0, 12),
        });
        BindBlurb(ship, ct);

        AddSpec(_detailBody, "Crew", ShipDetailCopy.Crew(ship));
        AddSpec(_detailBody, "Mass", ShipDetailCopy.Mass(ship));
        AddSpec(_detailBody, "Size", ShipDetailCopy.Dimensions(ship));

        if (ShipDetailCopy.SafeStoreUrl(ship.StoreUrl) is { } storeUrl)
            _detailBody.Children.Add(StoreLink(storeUrl));

        _detailBody.Children.Add(BuildDetailActions(ship));
        if (App.Data.GetHangarShipByCatalogId(ship.Id) is { } hangar)
            _detailBody.Children.Add(BuildHangarEditors(hangar, _detailGen, fromDetails: true));
        AddListings(_detailBody, "Buy in-game",
            ShipDetailCopy.RankPurchases(App.Market.PurchasesFor(ship.Id))
                .Select(p => ShipDetailCopy.ListingLine(p.TerminalName, p.PriceBuy)));
        AddListings(_detailBody, "Rent in-game",
            ShipDetailCopy.RankRentals(App.Market.RentalsFor(ship.Id))
                .Select(r => ShipDetailCopy.ListingLine(r.TerminalName, r.PriceRent)));
    }

    private void BindBlurb(ShipCatalogEntry ship, CancellationToken ct)
    {
        var blurb = new TextBlock
        {
            FontFamily = Hud.Font("UiFont"), FontSize = 13, Foreground = Hud.Br("FgBrush"),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left, TextAlignment = TextAlignment.Left,
            Visibility = Visibility.Collapsed,
        };
        var credit = new TextBlock
        {
            Text = ShipDetailCopy.WikiCredit, FontFamily = Hud.Font("UiFont"), FontSize = 10,
            Foreground = Hud.Br("FgDimBrush"), Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left, TextAlignment = TextAlignment.Left,
            Visibility = Visibility.Collapsed,
        };
        _detailBody.Children.Add(blurb);
        _detailBody.Children.Add(credit);

        var cached = App.ShipImages.LocalBlurbIfPresent(ship.Id);
        if (cached is not null)
        {
            ShowBlurb(blurb, credit, cached);
            return;
        }
        _ = LoadBlurbAsync(blurb, credit, ship, ct);
    }

    private async Task LoadBlurbAsync(TextBlock blurb, TextBlock credit, ShipCatalogEntry ship, CancellationToken ct)
    {
        try
        {
            var text = await App.ShipImages.EnsureBlurbAsync(ship.Id, ship.DisplayName, ct)
                .ConfigureAwait(false);
            if (ct.IsCancellationRequested || text is null) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (_detailId != ship.Id) return;
                ShowBlurb(blurb, credit, text);
            });
        }
        catch (OperationCanceledException)
        {
            // Leaving details cancels the extract fetch.
        }
    }

    private static void ShowBlurb(TextBlock blurb, TextBlock credit, string text)
    {
        blurb.Text = text;
        blurb.Visibility = Visibility.Visible;
        credit.Visibility = Visibility.Visible;
    }

    private FrameworkElement BuildDetailHero(ShipCatalogEntry ship, CancellationToken ct)
    {
        var thumbHost = new Grid
        {
            Height = DetailHeroHeight, ClipToBounds = true, Background = Hud.Br("BgBrush"),
            MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var image = new Image { Stretch = Stretch.Uniform, Tag = ship.Id };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var placeholder = new TextBlock
        {
            Text = "No preview", FontFamily = Hud.Font("UiFont"), FontSize = 12,
            Foreground = Hud.Br("FgDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        thumbHost.Children.Add(placeholder);
        thumbHost.Children.Add(image);
        BindThumb(image, placeholder, ship, ct, DetailDecodeWidth);
        return new Border
        {
            BorderBrush = Hud.Br("NavBorderBrush"), BorderThickness = new Thickness(1),
            Child = thumbHost, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 640,
        };
    }

    private FrameworkElement BuildDetailActions(ShipCatalogEntry ship)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 8) };
        var inHangar = App.Data.GetHangarShipByCatalogId(ship.Id) is not null;
        var add = new Button
        {
            Content = inHangar ? "In hangar" : "Add to hangar",
            Style = (Style)Application.Current.FindResource("NexusButton"),
            IsEnabled = !inHangar, Margin = new Thickness(0, 0, 8, 0),
        };
        if (!inHangar)
            add.Click += (_, _) => AddToHangar(ship);
        row.Children.Add(add);

        var isActive = string.Equals(App.Settings.Current.ActiveShipId, ship.Id, StringComparison.OrdinalIgnoreCase);
        var set = new Button
        {
            Content = isActive ? "Active ship" : "Set active",
            Style = (Style)Application.Current.FindResource("NexusButton"),
            IsEnabled = inHangar && !isActive,
        };
        if (inHangar && !isActive)
        {
            set.Click += (_, _) =>
            {
                App.Settings.SetActiveShipId(ship.Id);
                App.PublishActiveShip();
                Refresh();
            };
        }
        row.Children.Add(set);
        return row;
    }

    private static FrameworkElement StoreLink(string url)
    {
        var link = new Hyperlink { NavigateUri = new Uri(url) };
        link.Inlines.Add("Open RSI store");
        link.Foreground = Hud.Br("AccentBrush");
        link.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        var text = new TextBlock { FontFamily = Hud.Font("UiFont"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
        text.Inlines.Add(link);
        return text;
    }

    private static void AddSpec(Panel parent, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var key = new TextBlock
        {
            Text = label, FontFamily = Hud.Font("UiFont"), FontSize = 12, Foreground = Hud.Br("FgDimBrush"),
        };
        var val = new TextBlock
        {
            Text = value, FontFamily = Hud.Font("MonoFont"), FontSize = 12, Foreground = Hud.Br("FgBrush"),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(val, 1);
        row.Children.Add(key);
        row.Children.Add(val);
        parent.Children.Add(row);
    }

    private static void AddListings(Panel parent, string heading, IEnumerable<string> lines)
    {
        parent.Children.Add(new TextBlock
        {
            Text = heading.ToUpperInvariant(), FontFamily = Hud.Font("UiFont"), FontSize = 11,
            FontWeight = FontWeights.Bold, Foreground = Hud.Br("FgDimBrush"),
            Margin = new Thickness(0, 18, 0, 6),
        });
        var list = lines.ToList();
        if (list.Count == 0)
        {
            parent.Children.Add(Dim("No cached listings for this hull."));
            return;
        }
        foreach (var line in list)
        {
            parent.Children.Add(new TextBlock
            {
                Text = line, FontFamily = Hud.Font("MonoFont"), FontSize = 11, Foreground = Hud.Br("FgBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
    }

    private static TextBlock Dim(string text) => new()
    {
        Text = text, FontFamily = Hud.Font("UiFont"), FontSize = 12, Foreground = Hud.Br("FgDimBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    private void RefreshBrowser()
    {
        _thumbCts?.Cancel();
        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;
        var slice = App.Market.Vehicles;
        RebuildFilterChips();
        var rows = App.Market.Query(_query);
        _cardGrid.Children.Clear();

        if (slice.Items.Count == 0)
        {
            _browserBanner.Text = App.Settings.Current.MarketDataEnabled == true
                ? "No ship catalog yet. Market data will fill this list on the next refresh."
                : "Enable market data in Settings to load the ship catalog. Cached listings appear after the first successful fetch.";
            return;
        }

        _browserBanner.Text = rows.Count == 0
            ? "No ships match these filters."
            : slice.FetchedUtc is { } fetched
                ? $"{rows.Count} hulls · {MarketNotice.FormatAge(DateTime.UtcNow - fetched)}"
                : $"{rows.Count} hulls";

        foreach (var ship in rows)
            _cardGrid.Children.Add(BuildCard(ship, ct));
    }

    private FrameworkElement BuildCard(ShipCatalogEntry ship, CancellationToken ct)
    {
        var inner = new StackPanel();
        var thumbHost = new Grid { Height = ThumbHeight, ClipToBounds = true, Background = Hud.Br("BgBrush") };
        var image = new Image { Stretch = Stretch.UniformToFill, Tag = ship.Id };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var placeholder = new TextBlock
        {
            Text = "No preview", FontFamily = Hud.Font("UiFont"), FontSize = 11,
            Foreground = Hud.Br("FgDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        thumbHost.Children.Add(placeholder);
        thumbHost.Children.Add(image);
        BindThumb(image, placeholder, ship, ct, ThumbDecodeWidth);
        inner.Children.Add(thumbHost);

        var meta = new StackPanel { Margin = new Thickness(8, 8, 8, 8) };
        meta.Children.Add(new TextBlock
        {
            Text = ship.DisplayName, FontFamily = Hud.Font("UiFont"), FontSize = 13,
            FontWeight = FontWeights.SemiBold, Foreground = Hud.Br("FgBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        meta.Children.Add(new TextBlock
        {
            Text = ship.Manufacturer, FontFamily = Hud.Font("UiFont"), FontSize = 11,
            Foreground = Hud.Br("FgDimBrush"), Margin = new Thickness(0, 2, 0, 0),
        });
        var scu = ShipCatalogIds.UsableScu(ship);
        var badge = ShipDetailCopy.StatusBadge(ship);
        meta.Children.Add(new TextBlock
        {
            Text = $"{badge}  ·  {(scu is > 0 ? $"{scu} SCU" : "—")}  ·  {ship.Role}",
            FontFamily = Hud.Font("MonoFont"), FontSize = 10, Foreground = Hud.Br("AccentBrush"),
            Margin = new Thickness(0, 6, 0, 0),
        });

        var inHangar = App.Data.GetHangarShipByCatalogId(ship.Id) is not null;
        var add = new Button
        {
            Content = inHangar ? "In hangar" : "Add to hangar",
            Style = (Style)Application.Current.FindResource("NexusButton"),
            Margin = new Thickness(0, 8, 0, 0), IsEnabled = !inHangar,
            ToolTip = inHangar ? "Already in My Hangar" : "Add this hull to My Hangar",
        };
        if (!inHangar)
            add.Click += (_, _) => AddToHangar(ship);
        add.MouseLeftButtonUp += (_, e) => e.Handled = true;
        meta.Children.Add(add);
        inner.Children.Add(meta);

        var card = new Border
        {
            Width = CardWidth, Margin = new Thickness(0, 0, 12, 12),
            BorderBrush = Hud.Br("NavBorderBrush"), BorderThickness = new Thickness(1),
            Child = inner, Cursor = Cursors.Hand, ToolTip = "View details",
        };
        card.MouseEnter += (_, _) => card.BorderBrush = Hud.Br("AccentBrush");
        card.MouseLeave += (_, _) => card.BorderBrush = Hud.Br("NavBorderBrush");
        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenDetails(ship.Id);
        };
        return card;
    }

    private void BindThumb(Image image, TextBlock placeholder, ShipCatalogEntry ship, CancellationToken ct, int decodeWidth)
    {
        var local = App.ShipImages.LocalPathIfPresent(ship.Id);
        if (local is not null)
        {
            image.Source = LoadThumb(local, decodeWidth);
            placeholder.Visibility = Visibility.Collapsed;
            return;
        }
        if (!IsVisible) return;
        _ = LoadThumbAsync(image, placeholder, ship, ct, decodeWidth);
    }

    private async Task LoadThumbAsync(Image image, TextBlock placeholder, ShipCatalogEntry ship, CancellationToken ct, int decodeWidth)
    {
        try
        {
            var path = await App.ShipImages.EnsureLocalAsync(ship.Id, ship.PhotoUrl, ship.DisplayName, ct)
                .ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(image.Tag, ship.Id) || path is null) return;
                image.Source = LoadThumb(path, decodeWidth);
                placeholder.Visibility = Visibility.Collapsed;
            });
        }
        catch (OperationCanceledException)
        {
            // Leaving the page cancels in-flight thumbs.
        }
    }

    private static BitmapImage LoadThumb(string path, int decodeWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = decodeWidth;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void AddToHangar(ShipCatalogEntry ship)
    {
        if (App.Data.GetHangarShipByCatalogId(ship.Id) is not null) return;
        App.Data.SaveHangarShip(new HangarEntry(
            Guid.NewGuid().ToString("N"), ship.Id, HangarAcquisition.Pledged,
            null, null, "manual", DateTime.UtcNow));
        if (string.IsNullOrEmpty(App.Settings.Current.ActiveShipId))
            App.Settings.SetActiveShipId(ship.Id);
        App.PublishActiveShip();
        Refresh();
    }

    private void RefreshHangar()
    {
        _hangarGen++;
        _hangarList.Children.Clear();
        var rows = App.Data.GetHangarShips();
        var active = App.Settings.Current.ActiveShipId;
        if (rows.Count == 0)
        {
            _hangarBanner.Text = "My Hangar is empty. Add a hull from the Browser.";
            return;
        }

        var live = App.GameState.ActiveShip;
        _hangarBanner.Text = live.HasShip
            ? $"Active: {live.DisplayName} · {live.UsableCargoScu?.ToString() ?? "—"} SCU usable"
            : "No active ship. Confirm one below.";

        foreach (var row in rows)
        {
            var catalog = App.Market.ById(row.CatalogId);
            var name = catalog?.DisplayName ?? row.CatalogId;
            var isActive = string.Equals(row.CatalogId, active, StringComparison.OrdinalIgnoreCase);
            var card = new Border
            {
                BorderBrush = Hud.Br("NavBorderBrush"), BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 10, 12, 10),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            var nameBlock = new TextBlock
            {
                Text = name, FontFamily = Hud.Font("UiFont"), FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Hud.Br("FgBrush"), Cursor = Cursors.Hand, ToolTip = "View details",
            };
            info.Children.Add(nameBlock);
            info.Children.Add(new TextBlock
            {
                Text = HangarMetadata.Summary(row),
                FontFamily = Hud.Font("MonoFont"), FontSize = 10, Foreground = Hud.Br("FgDimBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var set = new Button
            {
                Content = isActive ? "Active" : "Set active",
                Style = (Style)Application.Current.FindResource("NexusButton"),
                IsEnabled = !isActive, Margin = new Thickness(0, 0, 8, 0),
            };
            if (!isActive)
            {
                set.Click += (_, _) =>
                {
                    App.Settings.SetActiveShipId(row.CatalogId);
                    App.PublishActiveShip();
                    Refresh();
                };
            }
            var remove = new Button
            {
                Content = "Remove",
                Style = (Style)Application.Current.FindResource("NexusButton"),
            };
            remove.Click += (_, _) =>
            {
                App.Data.DeleteHangarShip(row.Id);
                if (string.Equals(App.Settings.Current.ActiveShipId, row.CatalogId, StringComparison.OrdinalIgnoreCase))
                    App.Settings.SetActiveShipId("");
                App.PublishActiveShip();
                Refresh();
            };
            actions.Children.Add(set);
            actions.Children.Add(remove);
            actions.MouseLeftButtonUp += (_, e) => e.Handled = true;
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            var body = new StackPanel();
            body.Children.Add(grid);
            body.Children.Add(BuildHangarEditors(row, _hangarGen, fromDetails: false));
            card.Child = body;
            card.MouseEnter += (_, _) => card.BorderBrush = Hud.Br("AccentBrush");
            card.MouseLeave += (_, _) => card.BorderBrush = Hud.Br("NavBorderBrush");
            var catalogId = row.CatalogId;
            nameBlock.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                OpenDetails(catalogId);
            };
            _hangarList.Children.Add(card);
        }
    }

    private FrameworkElement BuildHangarEditors(HangarEntry row, int gen, bool fromDetails)
    {
        bool Live() => gen == (fromDetails ? _detailGen : _hangarGen);

        var box = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        box.MouseLeftButtonUp += (_, e) => e.Handled = true;

        var acq = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (HangarAcquisition kind in Enum.GetValues<HangarAcquisition>())
        {
            var captured = kind;
            var on = row.Acquisition == captured;
            acq.Children.Add(FilterChip(captured.ToString(), on, () =>
            {
                if (!Live() || row.Acquisition == captured) return;
                SaveHangar(HangarMetadata.WithAcquisition(row, captured));
            }));
        }
        box.Children.Add(acq);

        var fields = new StackPanel { Orientation = Orientation.Horizontal };
        var location = new TextBox
        {
            Style = (Style)Application.Current.FindResource("NexusTextBox"),
            Width = 220, Tag = "Last known location", Text = row.LastKnownLocation ?? "",
            FontSize = 12, Padding = new Thickness(8, 4, 8, 4),
        };
        location.LostFocus += (_, _) =>
        {
            if (!Live()) return;
            var next = HangarMetadata.WithLocation(row, location.Text);
            if (next.LastKnownLocation == row.LastKnownLocation) return;
            SaveHangar(next);
        };
        fields.Children.Add(location);

        if (row.Acquisition == HangarAcquisition.Rented)
        {
            var expiry = new TextBox
            {
                Style = (Style)Application.Current.FindResource("NexusTextBox"),
                Width = 140, Tag = "Expires yyyy-MM-dd", Text = HangarMetadata.ExpiryText(row.RentalExpiresUtc),
                FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0),
            };
            expiry.LostFocus += (_, _) =>
            {
                if (!Live()) return;
                if (!HangarMetadata.TryParseExpiry(expiry.Text, out var day))
                {
                    expiry.Text = HangarMetadata.ExpiryText(row.RentalExpiresUtc);
                    return;
                }
                if (HangarMetadata.ExpiryText(day) == HangarMetadata.ExpiryText(row.RentalExpiresUtc)) return;
                SaveHangar(HangarMetadata.WithExpiry(row, day));
            };
            fields.Children.Add(expiry);
        }
        box.Children.Add(fields);
        return box;
    }

    private void SaveHangar(HangarEntry row)
    {
        App.Data.SaveHangarShip(row);
        Refresh();
    }
}
