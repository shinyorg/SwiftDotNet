// androidx.car.app builder bindings are annotated as returning nullable builders/objects but never return null.
#pragma warning disable CS8602, CS8603, CS8604

namespace SwiftDotNet.AndroidAuto;

using Android.Text;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Navigation.Model;
using AndroidX.Lifecycle;
using CarAppAction = AndroidX.Car.App.Model.Action;
using CarAppTab = AndroidX.Car.App.Model.Tab;

/// <summary>
/// The one androidx.car.app Screen type SwiftDotNet.Car uses: it asks its <see cref="CarScreen"/> for a template in
/// <see cref="OnGetTemplate"/> and maps it. Renderer-side state (active tab, search results, map surface) lives here.
/// </summary>
sealed class CarAppScreen : Screen
{
    readonly CarNavigator nav;
    readonly AndroidCarRenderer renderer;
    string? activeTabId;
    string searchText = string.Empty;
    IReadOnlyList<CarListItem>? searchResults;
    int searchVersion;
    CarMapSurfaceRenderer? surface;

    public CarAppScreen(CarContext carContext, CarScreen screen, CarNavigator navigator, AndroidCarRenderer renderer) : base(carContext)
    {
        Model = screen;
        nav = navigator;
        this.renderer = renderer;
        Lifecycle.AddObserver(new LifecycleObserver(this));
    }

    public CarScreen Model { get; }

    bool IsRoot => nav.Stack.Count > 0 && ReferenceEquals(nav.Stack[0], Model);

    public override ITemplate OnGetTemplate()
    {
        var template = nav.BuildTemplateSafe(Model);
        try
        {
            return Map(template, inTab: false);
        }
        catch (Exception ex)
        {
            nav.Options.Log?.Invoke($"SwiftDotNet.Car Android mapping failed for {Model.GetType().Name}: {ex}");
            return MapMessage(ex.Message, "Error", []);
        }
    }

    /// <summary>Maps the current template, surfacing errors (used by <see cref="AndroidCarDiagnostics"/>).</summary>
    internal ITemplate RenderForDiagnostics(string? tabId)
    {
        if (tabId is not null)
            activeTabId = tabId;
        return Map(nav.BuildTemplate(Model), inTab: false);
    }

    ITemplate Map(CarTemplate model, bool inTab) => model switch
    {
        CarListTemplate list => MapList(list, inTab),
        CarGridTemplate grid => MapGrid(grid, inTab),
        CarInfoTemplate info => MapPane(info, inTab),
        CarTabsTemplate tabs => MapTabs(tabs),
        CarAlertTemplate alert => MapMessage(alert.Title!, null, alert.Actions),
        CarActionSheetTemplate sheet => MapMessage(sheet.Message ?? sheet.Title!, sheet.Message is null ? null : sheet.Title, sheet.Actions),
        CarSearchTemplate search => MapSearch(search),
        CarPointOfInterestTemplate poi => MapPoi(poi),
        CarMapTemplate map => MapMap(map),
        _ => throw new CarTemplateException($"Android Auto renderer does not support {model.GetType().Name}.")
    };

    CarAppAction? HeaderAction => IsRoot ? null : CarAppAction.Back;

    Header BuildHeader(CarTemplate model)
    {
        var header = new Header.Builder().SetTitle(model.Title ?? string.Empty);
        if (HeaderAction is { } back)
            header.SetStartHeaderAction(back);
        foreach (var action in model.HeaderActions)
            header.AddEndHeaderAction(action.ToAction(CarContext));
        return header.Build();
    }

    ITemplate MapList(CarListTemplate model, bool inTab)
    {
        var builder = new ListTemplate.Builder();
        if (!inTab)
            builder.SetHeader(BuildHeader(model));

        if (model.IsLoading)
            builder.SetLoading(true);
        else if (model.ItemCount == 0)
            builder.SetSingleList(Array.Empty<CarListItemBase>().ToItemList(CarContext, model.EmptyMessage));
        else if (model.Sections.Count == 1 && model.Sections[0].Header is null)
            builder.SetSingleList(model.Sections[0].Items.ToItemList(CarContext, model.EmptyMessage));
        else
            foreach (var section in model.Sections.Where(s => s.Items.Count > 0))
                builder.AddSectionedList(SectionedItemList.Create(section.Items.ToItemList(CarContext), section.Header ?? " "));

        return builder.Build();
    }

    ITemplate MapGrid(CarGridTemplate model, bool inTab)
    {
        var items = new ItemList.Builder();
        foreach (var item in model.Items)
        {
            var gi = new GridItem.Builder().SetTitle(item.Title);
            if (item.Image.ToCarIcon(CarContext) is { } icon)
                gi.SetImage(icon, GridItem.ImageTypeIcon);
            if (item.OnSelect is not null)
                gi.SetOnClickListener(new ClickListener(item.OnSelect));
            items.AddItem(gi.Build());
        }
        if (model.Items.Count == 0)
            items.SetNoItemsMessage("No items");

        var builder = new GridTemplate.Builder().SetSingleList(items.Build());
        if (!inTab)
            builder.SetHeader(BuildHeader(model));
        return builder.Build();
    }

    ITemplate MapPane(CarInfoTemplate model, bool inTab)
    {
        var pane = new Pane.Builder();
        if (model.IsLoading)
        {
            pane.SetLoading(true);
        }
        else
        {
            foreach (var item in model.Items)
            {
                var row = new Row.Builder().SetTitle(item.Title);
                if (!string.IsNullOrEmpty(item.Detail))
                    row.AddText(item.Detail);
                pane.AddRow(row.Build());
            }
            if (model.Items.Count == 0)
                pane.AddRow(new Row.Builder().SetTitle(model.Title ?? " ").Build());
            foreach (var action in model.Actions)
                pane.AddAction(action.ToAction(CarContext));
        }

        var builder = new PaneTemplate.Builder(pane.Build());
        if (!inTab)
            builder.SetHeader(BuildHeader(model));
        return builder.Build();
    }

    ITemplate MapTabs(CarTabsTemplate model)
    {
        if (activeTabId is null || model.Tabs.All(t => t.Id != activeTabId))
            activeTabId = model.SelectedTabId ?? model.Tabs[0].Id;

        var builder = new TabTemplate.Builder(new TabCallback(id =>
        {
            activeTabId = id;
            model.OnTabSelected?.Invoke(id);
            Invalidate();
        }));
        builder.SetHeaderAction(CarAppAction.AppIcon);

        foreach (var tab in model.Tabs)
        {
            var t = new CarAppTab.Builder().SetContentId(tab.Id).SetTitle(tab.Title);
            if (tab.Image.ToCarIcon(CarContext) is { } icon)
                t.SetIcon(icon);
            builder.AddTab(t.Build());
        }

        var active = model.Tabs.First(t => t.Id == activeTabId);
        builder.SetTabContents(new TabContents.Builder(Map(active.Content, inTab: true)).Build());
        builder.SetActiveTabContentId(active.Id);
        return builder.Build();
    }

    sealed class TabCallback(Action<string> onSelected) : Java.Lang.Object, TabTemplate.ITabCallback
    {
        public void OnTabSelected(string? tabContentId) => onSelected(tabContentId ?? string.Empty);
    }

    ITemplate MapMessage(string message, string? title, IReadOnlyList<CarAction> actions)
    {
        var header = new Header.Builder().SetStartHeaderAction(CarAppAction.Back);
        if (title is not null)
            header.SetTitle(title);
        var builder = new MessageTemplate.Builder(message).SetHeader(header.Build());
        foreach (var action in actions)
            builder.AddAction(action.ToAction(CarContext));
        return builder.Build();
    }

    ITemplate MapSearch(CarSearchTemplate model)
    {
        var builder = new SearchTemplate.Builder(new SearchCallback(this, model))
            .SetHeaderAction(CarAppAction.Back)
            .SetShowKeyboardByDefault(false)
            .SetInitialSearchText(searchText);
        if (model.Hint is not null)
            builder.SetSearchHint(model.Hint);

        if (searchResults is null && searchText.Length > 0)
            builder.SetLoading(true);
        else
            builder.SetItemList((searchResults ?? []).ToItemList(CarContext, searchText.Length == 0 ? "Type to search" : "No results"));
        return builder.Build();
    }

    void RunSearch(CarSearchTemplate model, string text)
    {
        searchText = text;
        var version = ++searchVersion;
        _ = RunAsync();

        async Task RunAsync()
        {
            IReadOnlyList<CarListItem> results;
            try
            {
                results = nav.Validator.NormalizeSearchResults(await model.Search(text, CancellationToken.None));
            }
            catch (Exception ex)
            {
                nav.Options.Log?.Invoke($"SwiftDotNet.Car search failed: {ex}");
                results = [];
            }

            AndroidCarRenderer.Dispatch(() =>
            {
                if (version != searchVersion)
                    return; // superseded by newer text
                searchResults = results;
                Invalidate();
            });
        }
    }

    sealed class SearchCallback(CarAppScreen screen, CarSearchTemplate model) : Java.Lang.Object, SearchTemplate.ISearchCallback
    {
        public void OnSearchTextChanged(string? searchText) => screen.RunSearch(model, searchText ?? string.Empty);

        public void OnSearchSubmitted(string? searchText)
        {
            screen.RunSearch(model, searchText ?? string.Empty);
            model.OnSubmitted?.Invoke(searchText ?? string.Empty);
        }
    }

    ITemplate MapPoi(CarPointOfInterestTemplate model)
    {
        var items = new ItemList.Builder();
        foreach (var point in model.Points)
        {
            var marker = new PlaceMarker.Builder();
            if (point.PinColor is { } pin)
                marker.SetColor(pin.ToCarColor());

            var place = new Place.Builder(CarLocation.Create(point.Latitude, point.Longitude)).SetMarker(marker.Build()).Build();
            var row = new Row.Builder()
                .SetTitle(point.Title)
                .SetMetadata(new Metadata.Builder().SetPlace(place).Build())
                .SetOnClickListener(new ClickListener(() => SelectPoint(point)));
            if (point.DistanceKm is { } km)
            {
                // PlaceListMapTemplate requires a DistanceSpan on non-browsable rows
                var distance = km < 1 ? Distance.Create(km * 1000, Distance.UnitMeters) : Distance.Create(km, Distance.UnitKilometers);
                var text = new SpannableString("\u00b7 " + (point.Subtitle ?? string.Empty));
                text.SetSpan(DistanceSpan.Create(distance), 0, 1, SpanTypes.InclusiveInclusive);
                row.AddText(text);
            }
            else
            {
                row.SetBrowsable(true); // browsable rows are exempt from the distance requirement
                if (!string.IsNullOrEmpty(point.Subtitle))
                    row.AddText(point.Subtitle);
            }
            if (!string.IsNullOrEmpty(point.Summary))
                row.AddText(point.Summary);
            items.AddItem(row.Build());
        }
        if (model.Points.Count == 0)
            items.SetNoItemsMessage("Nothing nearby");
        if (model.SelectedIndex >= 0)
            items.SetSelectedIndex(model.SelectedIndex);

        var builder = new PlaceListMapTemplate.Builder()
            .SetTitle(model.Title ?? string.Empty)
            .SetItemList(items.Build());
        if (HeaderAction is { } back)
            builder.SetHeaderAction(back);
        if (model.HeaderActions.ToActionStrip(CarContext) is { } strip)
            builder.SetActionStrip(strip);
        return builder.Build();
    }

    void SelectPoint(CarPointOfInterest point)
    {
        if (point.OnSelect is not null)
        {
            point.OnSelect();
            return;
        }
        // CarPlay shows primary/secondary buttons in its detail card; Android Auto has no card, so ask
        var actions = new[] { point.PrimaryAction, point.SecondaryAction }.OfType<CarAction>().ToList();
        if (actions.Count > 0)
            nav.Present(new CarActionSheetTemplate { Title = point.Title, Message = point.Summary ?? point.Subtitle, Actions = actions });
    }

    ITemplate MapMap(CarMapTemplate model)
    {
        if (CarContext.CarAppApiLevel < 7)
        {
            nav.Options.Log?.Invoke("SwiftDotNet.Car: CarMapTemplate requires Android Auto host API level 7 (MapWithContentTemplate).");
            return MapMessage("This head unit does not support app-drawn maps.", model.Title ?? "Map", []);
        }

        if (renderer.SurfaceEnabled)
        {
            surface ??= new CarMapSurfaceRenderer(nav.Options.Log);
            surface.Model = model;
            renderer.ActivateSurface(surface);
            surface.Draw();
        }

        var mapActions = new ActionStrip.Builder();
        if (model.OnPan is not null || model.OnZoom is not null)
            mapActions.AddAction(CarAppAction.Pan); // enters pan mode so the host forwards scroll/scale gestures
        foreach (var button in model.MapButtons)
            mapActions.AddAction(button.ToAction(CarContext, iconOnly: true));

        var controller = new MapController.Builder();
        if (model.MapButtons.Count > 0 || model.OnPan is not null || model.OnZoom is not null)
            controller.SetMapActionStrip(mapActions.Build());

        var content = model.Content is null
            ? MapMessage(model.Title ?? "Map", model.Title ?? "Map", [])
            : Map(model.Content with { Title = model.Content.Title ?? model.Title }, inTab: false);

        var builder = new MapWithContentTemplate.Builder()
            .SetMapController(controller.Build())
            .SetContentTemplate(content);
        if (model.HeaderActions.ToActionStrip(CarContext) is { } strip)
            builder.SetActionStrip(strip);
        return builder.Build();
    }

    void OnStopped()
    {
        if (surface is not null)
            renderer.DeactivateSurface(surface);
    }

    void OnDestroyed()
    {
        renderer.OnScreenDestroyed(this);
        nav.OnPlatformPopped(Model); // user pressed back; no-op if the navigator removed it
    }

    sealed class LifecycleObserver(CarAppScreen screen) : Java.Lang.Object, IDefaultLifecycleObserver
    {
        public void OnStart(ILifecycleOwner owner)
        {
            if (screen.surface is not null)
                screen.renderer.ActivateSurface(screen.surface);
        }

        public void OnStop(ILifecycleOwner owner) => screen.OnStopped();
        public void OnDestroy(ILifecycleOwner owner) => screen.OnDestroyed();
    }
}
