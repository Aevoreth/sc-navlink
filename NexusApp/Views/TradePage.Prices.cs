using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NexusApp.Services;

namespace NexusApp.Views;

public sealed partial class TradePage
{
    // PriceRowItem (exactly one of Uex/Sct populated per row) now lives in Services/PriceSort.cs,
    // alongside the sort helper itself - moved out of this class (live-pass item 2, 2026-07-30)
    // so PriceSort.SortRows and its unit tests can build rows without a WPF UserControl.

    private CommodityPickerBox _pricesCommodityPicker = null!;   // shared type-or-browse field (issue #41), replaces the plain ComboBox
    private readonly bool[] _priceCols = { true, true, true, false };   // STOCK, STATUS, AGE, +WEEK AVG - session only, not persisted (task brief: "persist nothing new")
    private static readonly string[] PriceColLabels = { "STOCK", "STATUS", "AGE", "+WEEK AVG" };
    private static readonly (MarketCatalogSide Side, string Label)[] PriceSides =
    {
        (MarketCatalogSide.Both, "BOTH"),
        (MarketCatalogSide.Buy, "BUY"),
        (MarketCatalogSide.Sell, "SELL"),
    };
    private string? _pricesSelectedCommodity;
    private bool _pricesSeeded;
    private string _pricesLocation = "";
    private MarketCatalogSide _pricesSide = MarketCatalogSide.Both;

    // MAP tab terminal filter (Task 8): set only by ShowPricesForTerminal, session-only (same
    // reasoning as _priceCols/_pricesSortColumn above - not part of AppSettings' fixed contract
    // and not something a "last state" restore should reapply on its own). Coexists with a chosen
    // commodity (both filters AND together); when no commodity is chosen while this or a location
    // filter is set, the results show every matching catalog row instead of the usual "one
    // commodity, every terminal" view - see RefreshPricesCommodityBox and RebuildPrices below.
    private int? _pricesTerminalFilter;

    // Sort state (live-pass item 2, 2026-07-30): session-only, not persisted - same
    // reasoning as _priceCols above. Default Sell descending is the pre-existing behavior, with
    // the Sell header visually marked active from first paint.
    private PriceSortColumn _pricesSortColumn = PriceSortColumn.Sell;
    private bool _pricesSortDescending = true;

    // Input area (commodity picker + the four column-toggle chips) built ONCE, results the only
    // thing rebuilt - same reasoning as the other two flows. The chips' visuals and the picker's
    // items are updated in place from here on, so a column toggle or an hourly refresh no longer
    // rebuilds the control the user is interacting with (an open dropdown included).
    private StackPanel _pricesInputs = null!;
    private StackPanel _pricesResults = null!;
    private List<string>? _pricesCommodityNames;   // the list currently pushed into the picker
    private FrameworkElement _pricesChromeRow = null!;
    private Button _pricesRefreshBtn = null!;
    private TextBox _pricesLocationBox = null!;
    private TextBlock _pricesBanner = null!;

    // FILTER CHIP BAR (concept A, approved 2026-08-10). Replaced the collapsible FILTERS shelf:
    // one chip per setting, each opening a popover holding that setting's own control.
    private FilterChipBar _pricesChips = null!;

    private void BuildPricesChrome()
    {
        if (_pricesInputs is not null) return;

        _pricesInputs = new StackPanel();

        // Live-pass finding (2026-07-30, round 3): the CONTROL itself must anchor to the pane's
        // left edge, under the ORIGIN pill. An explicit-Width child of a vertical StackPanel keeps
        // its default Stretch alignment and is therefore CENTERED in the available width - the
        // Planner/Sell inputs dodge this only because they live in horizontal rows. Pin it Left.
        var pickerGrp = new StackPanel { Width = 220, Margin = new Thickness(0, 0, 0, 16), HorizontalAlignment = HorizontalAlignment.Left };
        pickerGrp.Children.Add(FieldLabel("Commodity"));
        // Shared type-or-browse picker (issue #41): replaces the plain NexusComboBox, which had no
        // typable area at all (that style's template declares no PART_EditableTextBox, so
        // IsEditable would render nothing to type into - and the style is shared with other pages,
        // so it stays untouched; this also retires the old left-alignment template workarounds,
        // since the TextBox renders its own text left-aligned natively). Committed is the ONLY
        // path that changes the selection: typing just filters the popup, so clearing the text
        // never silently drops the active commodity, and the terminal-browse null state (Task 8)
        // survives because nothing here forces a pick.
        _pricesCommodityPicker = new CommodityPickerBox { PinnedFirst = "ALL" };
        _pricesCommodityPicker.Opened += () => Logger.Info("[UI] Trade prices: commodity list opened");
        _pricesCommodityPicker.Committed += name =>
        {
            var next = string.Equals(name, "ALL", StringComparison.OrdinalIgnoreCase) ? null : name;
            if (string.Equals(next, _pricesSelectedCommodity, StringComparison.Ordinal)) return;
            _pricesSelectedCommodity = next;
            Logger.Info($"[UI] Trade prices: commodity {next ?? "ALL"}");
            RebuildPrices();
        };
        _pricesCommodityPicker.InteractionEnded += () =>
        {
            var expect = _pricesSelectedCommodity ?? "ALL";
            if (!string.Equals(_pricesCommodityPicker.Text, expect, StringComparison.Ordinal))
                _pricesCommodityPicker.Text = expect;
        };
        pickerGrp.Children.Add(_pricesCommodityPicker);
        _pricesInputs.Children.Add(pickerGrp);

        var locationGrp = new StackPanel();
        locationGrp.Children.Add(FieldLabel("Location"));
        _pricesLocationBox = new TextBox
        {
            Style = (Style)Application.Current.FindResource("NexusTextBox"),
            Tag = "Terminal or place",
        };
        _pricesLocationBox.TextChanged += (_, _) =>
        {
            var next = _pricesLocationBox.Text?.Trim() ?? "";
            if (string.Equals(next, _pricesLocation, StringComparison.Ordinal)) return;
            _pricesLocation = next;
            Logger.Info($"[UI] Trade prices: location {(_pricesLocation.Length == 0 ? "ANY" : _pricesLocation)}");
            RebuildPrices();
        };
        locationGrp.Children.Add(_pricesLocationBox);

        var sideGrp = new StackPanel();
        sideGrp.Children.Add(FieldLabel("Side"));
        var sideRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (side, label) in PriceSides)
        {
            var pill = ScopePill(label);
            pill.MouseLeftButtonUp += (_, _) =>
            {
                if (_pricesSide == side) return;
                _pricesSide = side;
                Logger.Info($"[UI] Trade prices: side {label}");
                RefreshPricesSidePills(sideRow);
                RebuildPrices();
            };
            sideRow.Children.Add(pill);
        }
        RefreshPricesSidePills(sideRow);
        sideGrp.Children.Add(sideRow);

        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < PriceColLabels.Length; i++)
        {
            int idx = i;
            var chip = ColumnToggleChip(PriceColLabels[i], _priceCols[i]);
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _priceCols[idx] = !_priceCols[idx];
                SetColumnChipOn(chip, _priceCols[idx]);   // the chip itself lives on: retint it in place
                Logger.Info($"[UI] Trade prices: column {PriceColLabels[idx]} {(_priceCols[idx] ? "ON" : "OFF")}");
                RebuildPrices();
            };
            toggles.Children.Add(chip);
        }
        _pricesResults = new StackPanel();

        // FILTER CHIP BAR (concept A, approved 2026-08-10). Commodity, location, and buy/sell side
        // live in chips. System stays on the shared Trade context row (ALL/STANTON/PYRO/NYX).
        // Column toggles stay OUT of a popover and ride beside the chips, because they are view
        // options with no single value a chip could name.
        DetachFromParent(pickerGrp);
        DetachFromParent(toggles);
        pickerGrp.Margin = new Thickness(0);
        pickerGrp.Width = double.NaN;
        pickerGrp.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggles.Margin = new Thickness(4, 0, 0, 6);

        _pricesChips = new FilterChipBar(new List<FilterChipDef>
        {
            new() { Key = "COMMODITY", Content = pickerGrp, PopoverWidth = 250,
                    Value = () => string.IsNullOrWhiteSpace(_pricesSelectedCommodity) ? "ALL" : _pricesSelectedCommodity!,
                    IsSet = () => !string.IsNullOrWhiteSpace(_pricesSelectedCommodity) },
            new() { Key = "LOCATION", Content = locationGrp, PopoverWidth = 250,
                    Value = () => string.IsNullOrWhiteSpace(_pricesLocation) ? "ANY" : _pricesLocation,
                    IsSet = () => !string.IsNullOrWhiteSpace(_pricesLocation) },
            new() { Key = "SIDE", Content = sideGrp, PopoverWidth = 220,
                    Value = () => _pricesSide.ToString().ToUpperInvariant(),
                    IsSet = () => _pricesSide != MarketCatalogSide.Both },
        }, "prices");

        _pricesRefreshBtn = new Button
        {
            Content = MarketCatalogNotice.Refresh,
            Style = (Style)Application.Current.FindResource("NexusButton"),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(8, 0, 0, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pricesRefreshBtn.Click += (_, _) =>
        {
            if (App.Market.FetchInProgress) return;
            _pricesRefreshBtn.IsEnabled = false;
            Logger.Info("[UI] Trade prices: refresh");
            _ = App.Market.RefreshAsync(manual: true);
        };

        var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
        chipRow.Children.Add(_pricesChips);
        chipRow.Children.Add(toggles);
        chipRow.Children.Add(_pricesRefreshBtn);

        _pricesBanner = new TextBlock
        {
            FontFamily = Hud.Font("UiFont"), FontSize = 12.5, Foreground = Hud.Br("AccentBrush"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed,
        };

        var chrome = new StackPanel();
        chrome.Children.Add(chipRow);
        chrome.Children.Add(_pricesBanner);
        _pricesChromeRow = chrome;

        PricesHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        PricesHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_pricesChromeRow, 0);
        PricesHost.Children.Add(_pricesChromeRow);
        var resultsScroll = new ScrollViewer
        {
            Content = _pricesResults,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(resultsScroll, 1);
        PricesHost.Children.Add(resultsScroll);
    }

    // Re-validate on every rebuild (Task 14 rule, and the Sell flow now does the same): an hourly
    // snapshot refresh can drop the previously selected commodity, and a one-time seed would leave
    // the field stuck on a name no longer in `commodities`, producing a blank selection and a
    // permanent "0 terminals" render. The picker is updated in place afterwards so the box visibly
    // shows the commodity that was actually rendered - the control suppresses its own programmatic
    // writes (never a user pick, never a popup reopen), and the push is skipped entirely when
    // nothing changed.
    //
    // Task 8 / catalog fold: a location or terminal constraint may keep commodity at ALL (null).
    // First visit still seeds the first catalog commodity. After that, ALL stays ALL unless a
    // previously chosen name disappeared, in which case the first remaining name is picked.
    private void RefreshPricesCommodityBox(List<string> commodities)
    {
        bool stillValid = _pricesSelectedCommodity is not null
            && commodities.Any(c => string.Equals(c, _pricesSelectedCommodity, StringComparison.OrdinalIgnoreCase));
        if (!stillValid)
        {
            bool otherConstraint = _pricesTerminalFilter is not null || !string.IsNullOrWhiteSpace(_pricesLocation);
            if (otherConstraint)
                _pricesSelectedCommodity = null;
            else if (_pricesSelectedCommodity is not null || !_pricesSeeded)
                _pricesSelectedCommodity = commodities.FirstOrDefault();
            else
                _pricesSelectedCommodity = null;
        }
        if (_pricesSelectedCommodity is not null) _pricesSeeded = true;

        // The backing list is pushed even mid-interaction: SetItems never touches an open popup's
        // rows or the box text (its own doc), and skipping it here would leave the next chevron
        // browse or typed query filtering against commodities the snapshot no longer prices (the
        // Sell flow already pushes unconditionally every rebuild).
        if (_pricesCommodityNames is null || !_pricesCommodityNames.SequenceEqual(commodities, StringComparer.Ordinal))
        {
            _pricesCommodityNames = commodities;
            _pricesCommodityPicker.SetItems(commodities);
        }

        // Never during typing (issue #41): while the box has keyboard focus or the suggestion
        // popup is open, a text write-back would stomp the query mid-keystroke. The selection
        // above still revalidates (the results below always render something real); the box
        // catches up when the interaction ends (InteractionEnded, wired in BuildPricesChrome)
        // or on the next non-interacting rebuild.
        if (_pricesCommodityPicker.IsInteracting) return;

        var expect = _pricesSelectedCommodity ?? "ALL";
        if (!string.Equals(_pricesCommodityPicker.Text, expect, StringComparison.Ordinal))
            _pricesCommodityPicker.Text = expect;
    }

    // FILTERS shelf summary (task B2): "{commodity}, {visible column names}". Commodity falls back
    // to ALL for the terminal-browse mode (_pricesSelectedCommodity null, ShowPricesForTerminal),
    // matching the fallback RebuildPrices' own log line already uses. Column names in their fixed
    // STOCK/STATUS/AGE/+WEEK AVG order, only the ones currently toggled on.
    private void RefreshPricesFilterSummary() => _pricesChips?.Refresh();

    private void RebuildPrices()
    {
        BuildPricesChrome();
        if (!EnsureMarketConsent(_pricesResults, _pricesChromeRow)) return;
        _pricesResults.Children.Clear();
        _pricesRefreshBtn.IsEnabled = !App.Market.FetchInProgress;
        RefreshPricesBanner();

        var prices = App.Market.TradePrices;
        var commodities = MarketCatalogQueries.CommodityNames(prices.Items).ToList();

        RefreshPricesCommodityBox(commodities);
        // FILTERS shelf summary (task B2): after the correction above, so a commodity the hourly
        // refresh dropped is named correctly rather than one rebuild late.
        RefreshPricesFilterSummary();

        if (prices.Items.Count == 0)
        {
            _pricesResults.Children.Add(PricesEmptyNote(MarketCatalogNotice.NoCachedRows));
            return;
        }

        var filter = new MarketCatalogFilter(
            _pricesSelectedCommodity,
            App.Settings.Current.TradeScope,
            _pricesLocation,
            _pricesSide,
            _pricesTerminalFilter);
        if (!filter.HasBrowseConstraint)
        {
            _pricesResults.Children.Add(PricesEmptyNote(MarketCatalogNotice.NeedFilter));
            return;
        }

        var catalogRows = MarketCatalogQueries.Query(prices.Items, App.Market.Terminals.Items, filter);
        var uexRows = catalogRows.Select(ToTradePrice).ToList();
        if (uexRows.Count == 0)
        {
            _pricesResults.Children.Add(PricesEmptyNote(MarketCatalogNotice.NoMatchingRows));
            return;
        }

        // Terminal lookup, built once per rebuild: TerminalId -> MarketTerminal, so each row's
        // System tag is a dictionary read rather than a linear scan of Terminals.Rows.
        var terminals = CatalogTerminals();

        // SCT-only rows, merged into the same list (never a separate section). Only meaningful for
        // a single selected commodity - SctOnlyBuyers takes one CommodityId, and ALL-commodity
        // browse has no one id to key it off, so that mode shows catalog rows only. App.Sct.SctOnlyBuyers
        // self-gates on the market consent; the outer check here keeps this call site's own trace
        // at zero while dark, same as the Sell flow.
        var sctOnly = App.Settings.Current.MarketDataEnabled == true && _pricesSelectedCommodity is not null && uexRows.Count > 0
            ? App.Sct.SctOnlyBuyers(uexRows[0].CommodityId).ToList()
            : new List<SctListing>();

        // The top-50 display cap applies AFTER sorting (unchanged rule) - PriceSort.SortRows sorts
        // the FULL merged list first, Take(50) below only trims what renders.
        var merged = PriceSort.SortRows(
            uexRows.Select(r => new PriceRowItem(r.Sell, r, null))
                .Concat(sctOnly.Select(s => new PriceRowItem(s.Price, null, s)))
                .ToList(),
            _pricesSortColumn, _pricesSortDescending);
        int totalTerminals = merged.Count;   // includes the SCT-only rows only when they render (sctOnly is empty while dark)
        var top = merged.Take(50).ToList();   // spartan by default; codex row idiom, house rule against clutter on price surfaces

        // Task 8: the dismissible "TERMINAL: <name> x" chip, dropped above the header/results the
        // instant the filter clears (mouse-only dismiss - a plain TextBlock click handler, same
        // idiom as the ORIGIN chip's Manual/Live links, TradePage.cs:704-711 - carries no keyboard
        // path at all: nothing here is a Tab stop or has a key binding).
        // Location-first display (2026-07-31): filterTerm.Name is a raw MarketTerminal
        // name (Shop-first), unlike the price rows' own TerminalName column below (TradePriceRow.
        // TerminalName is a different UEX vocabulary - see BuildPriceRow's comment). Display only:
        // _pricesTerminalFilter stores the terminal ID, never this label, so nothing here can leak
        // a flipped name into filtering or persistence.
        if (_pricesTerminalFilter is { } filterTid && terminals.TryGetValue(filterTid, out var filterTerm))
            _pricesResults.Children.Add(TerminalFilterChip(TradeOriginResolver.LocationFirst(filterTerm.Name)));

        var cols = new System.Collections.Generic.List<ColumnDefinition> { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(100) }, new() { Width = new GridLength(100) } };
        if (_priceCols[0]) cols.Add(new ColumnDefinition { Width = new GridLength(100) });
        if (_priceCols[1]) cols.Add(new ColumnDefinition { Width = new GridLength(100) });
        if (_priceCols[2]) cols.Add(new ColumnDefinition { Width = new GridLength(100) });
        if (_priceCols[3]) cols.Add(new ColumnDefinition { Width = new GridLength(100) });

        var header = new Grid { Margin = new Thickness(12, 0, 12, 5) };
        foreach (var c in cols) header.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width });
        int col = 0;
        header.Children.Add(HeaderCell("Terminal", col++, false));   // Terminal stays unsortable
        header.Children.Add(SortableHeaderCell("Sell (/SCU)", col++, PriceSortColumn.Sell));
        header.Children.Add(SortableHeaderCell("Buy (/SCU)", col++, PriceSortColumn.Buy));
        if (_priceCols[0]) header.Children.Add(SortableHeaderCell("Stock", col++, PriceSortColumn.Stock));
        if (_priceCols[1]) header.Children.Add(SortableHeaderCell("Status", col++, PriceSortColumn.Status));
        if (_priceCols[2]) header.Children.Add(SortableHeaderCell("Age", col++, PriceSortColumn.Age));
        if (_priceCols[3]) header.Children.Add(HeaderCell("Week avg (sell)", col++, true));   // not in the sortable set
        _pricesResults.Children.Add(header);

        for (int i = 0; i < top.Count; i++)
        {
            var row = BuildPriceRow(top[i], cols, terminals);
            CascadeIn(row, i);
            _pricesResults.Children.Add(row);
        }

        _pricesResults.Children.Add(new TextBlock
        {
            Text = $"{totalTerminals} terminals - showing top {top.Count} by price",   // mock:1039, verbatim format
            FontFamily = Hud.Font("UiFont"), FontSize = 10.5, Foreground = Hud.Br("FgDimBrush"), Margin = new Thickness(0, 2, 0, 0),
        });

        string sctSuffix = App.Settings.Current.MarketDataEnabled == true ? $", sctOnly {sctOnly.Count}" : "";
        string commodityPart = _pricesSelectedCommodity ?? "ALL";
        Logger.Info($"[UI] Trade prices run: {totalTerminals} terminals, commodity {commodityPart}, showing {top.Count}{sctSuffix}");
    }

    /// <summary>Called when the user picks "show prices here" on a MAP tab terminal pin: switches
    /// to the Prices flow and filters results down to that one terminal. Coexists with whatever
    /// commodity is already selected (both filters AND together) - this does not touch
    /// _pricesSelectedCommodity, so a prior single-commodity browse narrows further to "this
    /// commodity, at this terminal" rather than resetting.
    ///
    /// A terminal id that does not resolve (stale pin from a catalog that has since changed)
    /// mirrors PrefillPlannerOriginFromMap's own no-op-on-null rule: the filter is never set at
    /// all, only skipped - still switches to Prices (the user's click should land somewhere), but
    /// leaves _pricesTerminalFilter untouched rather than setting it to an id nothing can resolve.
    /// That matters here in a way it does not for the origin prefill: the dismiss chip
    /// (TerminalFilterChip below) is the ONLY reset path for this field, and it only renders once
    /// terminals.TryGetValue resolves the id (RebuildPrices) - an unresolved id would otherwise set
    /// a filter with no chip to ever clear it, locking the flow onto an invisible, un-dismissable
    /// filter for the rest of the session (review finding, task-8).</summary>
    internal void ShowPricesForTerminal(int terminalId)
    {
        var terminals = CatalogTerminals().Values.ToList();
        var name = TradeOriginResolver.OriginNameForTerminal(terminalId, terminals);
        SwitchTab(2);
        if (name is null)
        {
            Logger.Info("[UI] trade: prices filter skipped (terminal unresolved)");
        }
        else
        {
            _pricesTerminalFilter = terminalId;
            Logger.Info($"[UI] trade: prices filtered from map ({name})");
        }
        RebuildPrices();
    }

    // Dismissible terminal-filter chip (Task 8). "x" is a plain TextBlock, not a Button - the same
    // mouse-only-dismiss idiom the ORIGIN chip's Manual/Live links already use (TradePage.cs), so
    // it carries no keyboard path (not a Tab stop, no key binding) by construction rather than by
    // suppressing one on a focusable control.
    private Border TerminalFilterChip(string terminalName)
    {
        var label = new TextBlock
        {
            Text = $"TERMINAL: {terminalName.ToUpperInvariant()}", FontFamily = Hud.Font("UiFont"), FontSize = 10.5,
            FontWeight = FontWeights.Bold, Foreground = Hud.Br("AccentBrush"), VerticalAlignment = VerticalAlignment.Center,
        };
        var close = new TextBlock
        {
            Text = "x", FontFamily = Hud.Font("UiFont"), FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = Hud.Br("FgDimBrush"), Margin = new Thickness(9, 0, 0, 0), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _pricesTerminalFilter = null;
            Logger.Info("[UI] Trade prices: terminal filter cleared");
            RebuildPrices();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(close);
        return new Border
        {
            Background = Hud.Br("AccentFaintBrush"), BorderBrush = Hud.Br("AccentStrongBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 0, 10),
            Child = row, HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    private static TextBlock HeaderCell(string text, int column, bool right)
    {
        var tb = new TextBlock { Text = text.ToUpperInvariant(), FontFamily = Hud.Font("UiFont"), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Hud.Br("FgDimBrush"), HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        Grid.SetColumn(tb, column);
        return tb;
    }

    // Click-to-sort header (live-pass item 2, 2026-07-30): SELL/BUY/STOCK/STATUS/AGE
    // only - Terminal and Week avg stay plain HeaderCells above, never wrapped by this. Click an
    // inactive header: sort by that column, descending first. Click the already-active header:
    // flip direction. The chevron only appears on the active header; inactive headers show nothing.
    private FrameworkElement SortableHeaderCell(string text, int column, PriceSortColumn key)
    {
        bool active = _pricesSortColumn == key;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = text.ToUpperInvariant(), FontFamily = Hud.Font("UiFont"), FontSize = 9,
            FontWeight = FontWeights.Bold, Foreground = Hud.Br("FgDimBrush"),
        });
        if (active) row.Children.Add(SortChevron(_pricesSortDescending));

        var host = new Border
        {
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right, Child = row,
        };
        host.MouseLeftButtonUp += (_, _) =>
        {
            if (_pricesSortColumn == key) _pricesSortDescending = !_pricesSortDescending;
            else { _pricesSortColumn = key; _pricesSortDescending = true; }
            // ALL CAPS column name, matching this file's existing [UI] Trade log idiom (column
            // toggles, scope, flow - PriceColLabels/TradeFlows.Ids/Scopes are all upper already).
            Logger.Info($"[UI] Trade prices: sort {key.ToString().ToUpperInvariant()} {(_pricesSortDescending ? "desc" : "asc")}");
            RebuildPrices();
        };
        Grid.SetColumn(host, column);
        return host;
    }

    // Small rotated Path, the same house chevron idiom as ChevronGlyph/SetChevronOpen
    // (TradePage.Planner.cs) - not a unicode arrow, not an emoji. Reuses that method's exact glyph
    // data at a smaller size so it reads as the same visual language, rotated to point down
    // (descending) or up (ascending) instead of ChevronGlyph's closed/open 0/90.
    private static Path SortChevron(bool descending) => new()
    {
        Width = 8, Height = 8, Data = Geometry.Parse("M5,3 L11,8 L5,13"),
        Stroke = Hud.Br("FgDimBrush"), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Fill = Brushes.Transparent,
        Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5),
        Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        RenderTransform = new RotateTransform(descending ? 90 : -90),
    };

    private static Border ColumnToggleChip(string label, bool on)
    {
        var text = new TextBlock { Text = label, FontFamily = Hud.Font("UiFont"), FontSize = 10.5, FontWeight = FontWeights.Bold };
        var chip = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Child = text,
        };
        SetColumnChipOn(chip, on);
        return chip;
    }

    // The on/off dressing, applied both at build time and in place on every later toggle (the chip
    // is built once now, so the click has to retint the live control rather than rely on a rebuild).
    private static void SetColumnChipOn(Border chip, bool on)
    {
        ((TextBlock)chip.Child).Foreground = on ? Hud.Br("AccentBrush") : Hud.Br("FgDimBrush");
        chip.BorderBrush = on ? Hud.Br("AccentStrongBrush") : Hud.Br("BorderBrush");
        chip.Background = on ? Hud.Br("AccentFaintBrush") : Brushes.Transparent;
    }

    private FrameworkElement BuildPriceRow(PriceRowItem item, System.Collections.Generic.List<ColumnDefinition> colTemplate,
        System.Collections.Generic.Dictionary<int, MarketTerminal> terminals)
    {
        var grid = new Grid();
        foreach (var c in colTemplate) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width });
        int col = 0;

        if (item.Uex is { } r)
        {
            // Terminal name lives directly in a Grid cell (the Star column), not a StackPanel, so
            // name+tag are wrapped in a horizontal StackPanel within that same cell (house idiom,
            // matches the SCT-only branch's termPanel below) - this column is the widest of the row
            // and carries no trimming, so the tag never wraps or clips.
            //
            // Location-first display NOT applied here (2026-07-31 review): r.TerminalName
            // is TradePriceRow.TerminalName, a DIFFERENT UEX vocabulary from MarketTerminal.Name -
            // already documented in TradePage.cs's TerminalNames doc comment (e.g. "CBD Lorville" vs
            // "CBD - Central Business District - Lorville" for the same terminal, verified against a
            // real capture). TradeOriginResolver.LocationFirst's " - " split rule was verified only
            // against MarketTerminal.Name; applying it here unverified risks mangling names that don't
            // follow that vocabulary. Same reasoning applies to the Sell flow's buyer rows
            // (TradePage.Sell.cs, BuildBuyerRowCore's terminalName param, fed from SellLookup.Buyer.
            // Row.TerminalName) - left unchanged.
            var termPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (_pricesSelectedCommodity is null)
            {
                termPanel.Children.Add(new TextBlock
                {
                    Text = r.CommodityName, FontFamily = Hud.Font("UiFont"), FontSize = 13,
                    FontWeight = FontWeights.SemiBold, Foreground = Hud.Br("FgBrush"),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                });
            }
            var termName = new TextBlock
            {
                Text = r.TerminalName, FontFamily = Hud.Font("UiFont"), FontSize = 13,
                FontWeight = FontWeights.SemiBold, Foreground = Hud.Br("CyanBrush"),
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                ToolTip = "Show on Starmap",
            };
            termName.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                RaiseShowOnMapForTerminal(r.TerminalId, r.TerminalName);
            };
            termPanel.Children.Add(termName);
            string? system = terminals.TryGetValue(r.TerminalId, out var termInfo) ? termInfo.System : null;
            if (SystemTag(system) is { } tag) termPanel.Children.Add(tag);
            Grid.SetColumn(termPanel, col++); grid.Children.Add(termPanel);

            var sell = new TextBlock { Text = r.Sell.ToString("n0", CultureInfo.InvariantCulture), FontFamily = Hud.Font("MonoFont"), FontSize = 13, Foreground = Hud.Br("GoldBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sell, col++); grid.Children.Add(sell);

            var buy = new TextBlock { Text = r.Buy.ToString("n0", CultureInfo.InvariantCulture), FontFamily = Hud.Font("MonoFont"), FontSize = 13, Foreground = Hud.Br("CyanBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(buy, col++); grid.Children.Add(buy);

            bool outOfStock = r.BuyStockScu == 0;
            if (_priceCols[0])
            {
                var stock = new TextBlock { Text = $"{r.BuyStockScu:n0} SCU", FontFamily = Hud.Font("MonoFont"), FontSize = 12, Foreground = outOfStock ? Hud.Br("DangerBrush") : Hud.Br("FgBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(stock, col++); grid.Children.Add(stock);
            }
            if (_priceCols[1])
            {
                // Label from the real UEX inventory-state code (TradeFlows.BuyStatusLabel), not a
                // binary derived-from-stock guess (task-14 review finding 1). Color rule unchanged:
                // still keyed off BuyStockScu == 0, only the text source changed.
                var status = new TextBlock { Text = TradeFlows.BuyStatusLabel(r.StatusBuy), FontFamily = Hud.Font("UiFont"), FontSize = 10, Foreground = outOfStock ? Hud.Br("DangerBrush") : Hud.Br("FgDimBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(status, col++); grid.Children.Add(status);
            }
            if (_priceCols[2])
            {
                var age = DateTime.UtcNow - r.ModifiedUtc;
                var ageText = new TextBlock { Text = MarketNotice.FormatAge(age), FontFamily = Hud.Font("MonoFont"), FontSize = 10, Foreground = age.TotalHours >= 24 ? Hud.Br("AccentBrush") : Hud.Br("FgDimBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(ageText, col++); grid.Children.Add(ageText);
            }
            if (_priceCols[3])
            {
                // Week-average sell is not part of the TradePriceRow contract (no SellAvgWeek field on
                // this record, unlike the legacy MarketPriceRow); show the instant sell price with a
                // note rather than fabricate an average. Flagged: revisit if a week-avg field is added.
                var wk = new TextBlock { Text = $"{r.Sell:n0}*", FontFamily = Hud.Font("MonoFont"), FontSize = 11, Foreground = Hud.Br("FgDimBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = "No week-average field on this data source yet; showing the instant price." };
                Grid.SetColumn(wk, col++); grid.Children.Add(wk);
            }
        }
        else
        {
            // SCT-only row (mock index.html:1024-1030): terminal name + inline badge, sell = the
            // SCT price, everything else UEX has no equivalent for is a plain dash - never
            // danger-colored (no stock/status data exists to judge "out of stock" from).
            var s = item.Sct!;
            var termPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            termPanel.Children.Add(new TextBlock { Text = s.Location, FontFamily = Hud.Font("UiFont"), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Hud.Br("FgBrush"), VerticalAlignment = VerticalAlignment.Center });
            // Reuses CorroborationBadge with a synthesized SctOnly ReconciledPrice - the same
            // shape PriceReconciler.Reconcile itself returns for the SCT-only case (shared factory,
            // also used by the sell flow's SCT-only rows).
            if (CorroborationBadge(SctOnlyReconciled(s)) is { } sctBadge)
            {
                sctBadge.Margin = new Thickness(8, 0, 0, 0);
                termPanel.Children.Add(sctBadge);
            }
            Grid.SetColumn(termPanel, col++); grid.Children.Add(termPanel);

            var sell = new TextBlock { Text = s.Price.ToString("n0", CultureInfo.InvariantCulture), FontFamily = Hud.Font("MonoFont"), FontSize = 13, Foreground = Hud.Br("GoldBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sell, col++); grid.Children.Add(sell);

            var buy = DashCell(13);
            Grid.SetColumn(buy, col++); grid.Children.Add(buy);

            if (_priceCols[0]) { var stock = DashCell(12); Grid.SetColumn(stock, col++); grid.Children.Add(stock); }
            if (_priceCols[1]) { var status = DashCell(10); Grid.SetColumn(status, col++); grid.Children.Add(status); }
            if (_priceCols[2])
            {
                var age = DateTime.UtcNow - s.TimestampUtc;
                var ageText = new TextBlock { Text = MarketNotice.FormatAge(age), FontFamily = Hud.Font("MonoFont"), FontSize = 10, Foreground = age.TotalHours >= 24 ? Hud.Br("AccentBrush") : Hud.Br("FgDimBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(ageText, col++); grid.Children.Add(ageText);
            }
            if (_priceCols[3]) { var wk = DashCell(11); Grid.SetColumn(wk, col++); grid.Children.Add(wk); }   // no week-avg source for SCT-only either
        }

        return Hud.RowCard(grid, marginBottom: 8);
    }

    private static TextBlock DashCell(double fontSize) => new()
    {
        Text = "-", FontFamily = Hud.Font("MonoFont"), FontSize = fontSize, Foreground = Hud.Br("FgDimBrush"),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
    };

    private void RefreshPricesBanner()
    {
        var prices = App.Market.TradePrices;
        var freshness = App.Market.FetchInProgress ? ProviderFreshness.Fresh : prices.Freshness;
        var banner = MarketCatalogNotice.Banner(freshness);
        _pricesBanner.Text = banner ?? "";
        _pricesBanner.Visibility = banner is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshPricesSidePills(StackPanel row)
    {
        for (int i = 0; i < PriceSides.Length && i < row.Children.Count; i++)
        {
            bool on = _pricesSide == PriceSides[i].Side;
            var pill = (Border)row.Children[i];
            ((TextBlock)pill.Child).Foreground = on ? Hud.Br("AccentBrush") : Hud.Br("FgDimBrush");
            pill.BorderBrush = on ? Hud.Br("AccentStrongBrush") : Hud.Br("NavBorderBrush");
            pill.Background = on ? Hud.Br("AccentFaintBrush") : Hud.Br("Bg2NavBrush");
        }
    }

    private static Dictionary<int, MarketTerminal> CatalogTerminals()
    {
        var map = new Dictionary<int, MarketTerminal>();
        foreach (var t in App.Market.Terminals.Items)
            map[t.Id] = UexNormalizer.ToMarket(t);
        return map;
    }

    private static TradePriceRow ToTradePrice(MarketCatalogRow r) =>
        new(r.TerminalId, r.CommodityId, r.Buy, r.Sell, r.BuyStockScu, r.SellDemandScu,
            r.StatusBuy, r.StatusSell, "", r.ObservedUtc, r.TerminalName, r.CommodityName);

    private void RaiseShowOnMapForTerminal(int terminalId, string terminalName)
    {
        var terminal = App.Market.Terminals.Items.FirstOrDefault(t => t.Id == terminalId);
        if (terminal is null) return;
        if (App.Map.ResolveTerminal(UexNormalizer.ToMarket(terminal)) is not { } stop) return;
        Logger.Info($"[UI] Trade prices: show on map {terminalName}");
        RaiseShowOnMap(stop.Id);
    }

    private static TextBlock PricesEmptyNote(string text) => new()
    {
        Text = text, FontFamily = Hud.Font("UiFont"), FontSize = 12.5,
        Foreground = Hud.Br("FgDimBrush"), TextWrapping = TextWrapping.Wrap, MaxWidth = 520,
        Margin = new Thickness(0, 8, 0, 0),
    };
}
