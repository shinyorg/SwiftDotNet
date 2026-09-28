using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CarPlay;
using CoreFoundation;
using CoreGraphics;
using CoreLocation;
using Foundation;
using MapKit;
using ObjCRuntime;
using UIKit;

namespace SwiftDotNet.CarPlay;

/// <summary>
/// Mirrors the <see cref="CarNavigator"/> stack onto a <see cref="CPInterfaceController"/>, mapping SwiftDotNet.Car
/// templates to CPTemplates and updating them in place on invalidation.
/// </summary>
sealed partial class CarPlayRenderer : ICarRenderer
{
    readonly CPInterfaceController ic;
    readonly CarPlayMapViewController? mapController;
    readonly InterfaceDelegate icDelegate;
    readonly Action<string>? log;
    readonly Dictionary<CarScreen, Entry> entries = [];
    readonly ConditionalWeakTable<CPTemplate, TemplateState> states = new();
    CarNavigator nav = null!;
    CPTemplate? presentedNative;

    sealed class Entry(CPTemplate native, CarTemplate model)
    {
        public CPTemplate Native { get; set; } = native;
        public CarTemplate Model { get; set; } = model;
        public bool Dirty { get; set; }
    }

    /// <summary>Per-native-template state: the latest model plus delegates that must stay alive.</summary>
    sealed class TemplateState(CarTemplate model)
    {
        public CarTemplate Model { get; set; } = model;
        public NSObject? Delegate { get; set; }
        public Dictionary<string, CPTemplate>? TabChildren { get; set; }
        /// <summary>Title of the point of interest the user last selected (CarPlay keeps no selection state for us).</summary>
        public string? SelectedPoi { get; set; }
    }

    public CarPlayRenderer(CPInterfaceController interfaceController, CPWindow? window, Action<string>? log)
    {
        ic = interfaceController;
        this.log = log;
        icDelegate = new InterfaceDelegate(this);
        ic.Delegate = icDelegate;

        if (window is not null)
        {
            mapController = new CarPlayMapViewController();
            window.RootViewController = mapController;
        }
    }

    public static void Dispatch(Action action)
    {
        if (NSThread.IsMain)
            action();
        else
            DispatchQueue.MainQueue.DispatchAsync(action);
    }

    /// <summary>Limits reported by CarPlay (used when <see cref="CarLimitsSource.Platform"/> is selected).</summary>
    public static CarLimits PlatformLimits() => new()
    {
        MaxListItems = (int)CPListTemplate.MaximumItemCount,
        MaxListSections = (int)CPListTemplate.MaximumSectionCount,
        MaxGridItems = 8,
        MaxTabs = (int)CPTabBarTemplate.MaximumTabCount,
        MaxInfoItems = 10,
        MaxInfoActions = 3,
        MaxModalActions = (int)CPAlertTemplate.MaximumActionCount,
        MaxPointsOfInterest = 12,
        MaxHeaderActions = 2,
        MaxMapButtons = 4,
        MaxDepth = 5
    };

    #region ICarRenderer

    public void Attach(CarNavigator navigator) => nav = navigator;

    public void SetRoot(CarScreen root)
    {
        var entry = CreateEntry(root);
        ic.SetRootTemplate(entry.Native, false, Completion("setRoot"));
    }

    public void Push(CarScreen screen)
    {
        var entry = CreateEntry(screen);
        ic.PushTemplate(entry.Native, true, Completion("push"));
    }

    public void Pop()
    {
        ic.PopTemplate(true, Completion("pop"));
        Prune();
    }

    public void PopToRoot()
    {
        ic.PopToRootTemplate(true, Completion("popToRoot"));
        Prune();
    }

    public void Present(CarModalScreen modal)
    {
        presentedNative = Create(nav.BuildTemplateSafe(modal));
        ic.PresentTemplate(presentedNative, true, Completion("present"));
    }

    public void Dismiss(CarModalScreen modal)
    {
        if (presentedNative is null)
            return;
        presentedNative = null;
        ic.DismissTemplate(true, Completion("dismiss"));
    }

    public void Invalidate(CarScreen screen)
    {
        if (screen is CarModalScreen || !entries.TryGetValue(screen, out var entry))
            return;

        var model = nav.BuildTemplateSafe(screen);
        if (TryUpdate(entry.Native, model))
        {
            entry.Model = model;
            entry.Dirty = false;
            return;
        }

        entry.Model = model;
        if (ReferenceEquals(ic.TopTemplate, entry.Native))
            Replace(entry);
        else
            entry.Dirty = true; // rebuilt when it next appears
    }

    #endregion

    Entry CreateEntry(CarScreen screen)
    {
        var model = nav.BuildTemplateSafe(screen);
        var entry = new Entry(Create(model), model);
        entries[screen] = entry;
        return entry;
    }

    /// <summary>Swaps the native template of the top screen when the template type changed.</summary>
    void Replace(Entry entry)
    {
        var old = entry.Native;
        entry.Native = Create(entry.Model);
        entry.Dirty = false;

        if (ic.Templates.Length <= 1 || ReferenceEquals(ic.RootTemplate, old))
        {
            ic.SetRootTemplate(entry.Native, false, Completion("replaceRoot"));
        }
        else
        {
            var replacement = entry.Native;
            ic.PopTemplate(false, (_, _) => ic.PushTemplate(replacement, false, Completion("replace")));
        }
    }

    void Prune()
    {
        foreach (var screen in entries.Keys.Where(s => !s.IsAttached).ToList())
            entries.Remove(screen);
    }

    Action<bool, NSError> Completion(string op) => (_, error) =>
    {
        if (error is not null)
            log?.Invoke($"SwiftDotNet.Car CarPlay {op} failed: {error.LocalizedDescription}");
    };

    #region Interface controller callbacks

    void OnTemplateWillAppear(CPTemplate template)
    {
        if (mapController is not null && states.TryGetValue(template, out var state) && state.Model is CarMapTemplate map)
            mapController.Model = map;
    }

    void OnTemplateDidAppear(CPTemplate template)
    {
        var entry = entries.Values.FirstOrDefault(e => ReferenceEquals(e.Native, template));
        if (entry is { Dirty: true })
            Replace(entry);
    }

    void OnTemplateDidDisappear(CPTemplate template)
    {
        // still on the stack - merely covered by a pushed/presented template
        if (ic.Templates.Any(t => ReferenceEquals(t, template)))
            return;

        var screen = entries.FirstOrDefault(kv => ReferenceEquals(kv.Value.Native, template)).Key;
        if (screen is null)
            return;

        entries.Remove(screen);
        nav.OnPlatformPopped(screen); // user tapped back; no-op when we popped it ourselves
    }

    sealed class InterfaceDelegate(CarPlayRenderer renderer) : CPInterfaceControllerDelegate
    {
        public override void TemplateWillAppear(CPTemplate aTemplate, bool animated) => renderer.OnTemplateWillAppear(aTemplate);
        public override void TemplateDidAppear(CPTemplate aTemplate, bool animated) => renderer.OnTemplateDidAppear(aTemplate);
        public override void TemplateDidDisappear(CPTemplate aTemplate, bool animated) => renderer.OnTemplateDidDisappear(aTemplate);
    }

    #endregion

    #region Create

    CPTemplate Create(CarTemplate model)
    {
        CPTemplate native = model switch
        {
            CarListTemplate list => CreateList(list),
            CarGridTemplate grid => CreateGrid(grid),
            CarTabsTemplate tabs => CreateTabs(tabs),
            CarInfoTemplate info => CreateInfo(info),
            CarAlertTemplate alert => new CPAlertTemplate([alert.Title!], alert.Actions.Select(ToAlertAction).ToArray()),
            CarActionSheetTemplate sheet => new CPActionSheetTemplate(sheet.Title, sheet.Message, sheet.Actions.Select(ToAlertAction).ToArray()),
            CarSearchTemplate search => CreateSearch(search),
            CarMapTemplate map => CreateMap(map),
            CarPointOfInterestTemplate poi => CreatePoi(poi),
            _ => throw new CarTemplateException($"CarPlay renderer does not support {model.GetType().Name}.")
        };

        if (!states.TryGetValue(native, out _))
            states.Add(native, new TemplateState(model));
        return native;
    }

    CPListTemplate CreateList(CarListTemplate model)
    {
        var template = new CPListTemplate(model.Title ?? string.Empty, ToSections(model));
        ApplyList(template, model);
        return template;
    }

    void ApplyList(CPListTemplate template, CarListTemplate model)
    {
        template.TrailingNavigationBarButtons = ToBarButtons(model.HeaderActions);
        if (OperatingSystem.IsIOSVersionAtLeast(18, 4))
            template.ShowsSpinnerWhileEmpty = model.IsLoading;
        template.EmptyViewTitleVariants = model.EmptyMessage is null ? [] : [model.EmptyMessage];
    }

    static CPListSection[] ToSections(CarListTemplate model) =>
        model.IsLoading
            ? []
            : model.Sections.Select(s =>
            {
                var items = s.Items.Select(i => (ICPListTemplateItem)ToListItem(i)).ToArray();
                return new CPListSection(items, s.Header!, null!);
            }).ToArray();

    static CPListItem ToListItem(CarListItemBase model)
    {
        CPListItem item;
        switch (model)
        {
            case CarToggleItem toggle:
                // CarPlay has no switch - emulate with a checkmark accessory and flip on tap
                var check = UIImage.GetSystemImage(toggle.IsOn ? "checkmark.circle.fill" : "circle");
                item = new CPListItem(toggle.Title, toggle.Detail, toggle.Image.ToUIImage(), check, CPListItemAccessoryType.None);
                item.Handler = (_, done) =>
                {
                    toggle.OnToggled?.Invoke(!toggle.IsOn);
                    done();
                };
                break;

            case CarListItem row:
                var accessory = row.Accessory switch
                {
                    CarAccessory.Disclosure => CPListItemAccessoryType.DisclosureIndicator,
                    CarAccessory.Cloud => CPListItemAccessoryType.Cloud,
                    _ => CPListItemAccessoryType.None
                };
                item = new CPListItem(row.Title, row.Detail, row.Image.ToUIImage(), null!, accessory);
                item.Handler = (_, done) =>
                {
                    row.OnSelect?.Invoke();
                    done();
                };
                break;

            default:
                throw new CarTemplateException($"Unsupported list item {model.GetType().Name}.");
        }

        item.Enabled = model.IsEnabled;
        return item;
    }

    CPGridTemplate CreateGrid(CarGridTemplate model)
    {
        var template = new CPGridTemplate(model.Title ?? string.Empty, ToGridButtons(model));
        template.TrailingNavigationBarButtons = ToBarButtons(model.HeaderActions);
        return template;
    }

    static CPGridButton[] ToGridButtons(CarGridTemplate model) =>
        model.Items.Select(i =>
        {
            var image = i.Image.ToUIImage() ?? UIImage.GetSystemImage("square.grid.2x2")!;
            Action<CPGridButton> handler = _ => i.OnSelect?.Invoke();
            var button = OperatingSystem.IsIOSVersionAtLeast(26)
                ? new CPGridButton([i.Title], image, null!, handler)
                : new CPGridButton([i.Title], image, handler);
            button.Enabled = i.IsEnabled;
            return button;
        }).ToArray();

    CPTabBarTemplate CreateTabs(CarTabsTemplate model)
    {
        var children = new Dictionary<string, CPTemplate>();
        var natives = model.Tabs.Select(tab =>
        {
            var child = Create(tab.Content);
            ApplyTab(child, tab);
            children[tab.Id] = child;
            return child;
        }).ToArray();

        var state = new TemplateState(model) { TabChildren = children };
        var template = new CPTabBarTemplate(natives);
        var tabDelegate = new TabDelegate(state);
        template.Delegate = tabDelegate;
        state.Delegate = tabDelegate;
        states.Add(template, state);

        var selected = model.Tabs.ToList().FindIndex(t => t.Id == model.SelectedTabId);
        if (selected > 0 && OperatingSystem.IsIOSVersionAtLeast(17))
            template.SelectTemplate(selected);
        return template;
    }

    static void ApplyTab(CPTemplate child, CarTab tab)
    {
        child.TabTitle = tab.Title;
        child.TabImage = tab.Image.ToUIImage();
    }

    sealed class TabDelegate(TemplateState state) : CPTabBarTemplateDelegate
    {
        public override void DidSelectTemplate(CPTabBarTemplate tabBarTemplate, CPTemplate selectedTemplate)
        {
            var model = (CarTabsTemplate)state.Model;
            var id = state.TabChildren?.FirstOrDefault(kv => ReferenceEquals(kv.Value, selectedTemplate)).Key;
            if (id is not null)
                model.OnTabSelected?.Invoke(id);
        }
    }

    CPInformationTemplate CreateInfo(CarInfoTemplate model)
    {
        var template = new CPInformationTemplate(
            model.Title ?? string.Empty,
            model.Layout == CarInfoLayout.TwoColumn ? CPInformationTemplateLayout.TwoColumn : CPInformationTemplateLayout.Leading,
            ToInfoItems(model),
            model.Actions.Select(ToTextButton).ToArray());
        template.TrailingNavigationBarButtons = ToBarButtons(model.HeaderActions);
        return template;
    }

    static CPInformationItem[] ToInfoItems(CarInfoTemplate model) =>
        model.IsLoading ? [new CPInformationItem("Loading…", null!)] : model.Items.Select(i => new CPInformationItem(i.Title, i.Detail!)).ToArray();

    CPSearchTemplate CreateSearch(CarSearchTemplate model)
    {
        var state = new TemplateState(model);
        var template = new CPSearchTemplate();
        var searchDelegate = new SearchDelegate(state, nav.Validator, log);
        template.Delegate = searchDelegate;
        state.Delegate = searchDelegate;
        states.Add(template, state);
        return template;
    }

    sealed class SearchDelegate(TemplateState state, CarTemplateValidator validator, Action<string>? log) : CPSearchTemplateDelegate
    {
        readonly Dictionary<CPListItem, CarListItem> current = [];
        string text = string.Empty;

        CarSearchTemplate Model => (CarSearchTemplate)state.Model;

        public override void UpdatedSearchText(CPSearchTemplate searchTemplate, string searchText, CPSearchTemplateDelegateUpdateHandler completionHandler)
        {
            text = searchText ?? string.Empty;
            _ = RunAsync(text, completionHandler);
        }

        async Task RunAsync(string query, CPSearchTemplateDelegateUpdateHandler completion)
        {
            CPListItem[] items = [];
            try
            {
                var results = validator.NormalizeSearchResults(await Model.Search(query, CancellationToken.None));
                Dispatch(() =>
                {
                    current.Clear();
                    items = results.Select(r =>
                    {
                        var li = new CPListItem(r.Title, r.Detail, r.Image.ToUIImage());
                        current[li] = r;
                        return li;
                    }).ToArray();
                    completion(items);
                });
            }
            catch (Exception ex)
            {
                log?.Invoke($"SwiftDotNet.Car search failed: {ex}");
                Dispatch(() => completion([]));
            }
        }

        public override void SelectedResult(CPSearchTemplate searchTemplate, CPListItem item, Action completionHandler)
        {
            if (current.TryGetValue(item, out var result))
                result.OnSelect?.Invoke();
            completionHandler();
        }

        public override void SearchButtonPressed(CPSearchTemplate searchTemplate) => Model.OnSubmitted?.Invoke(text);
    }

    CPTemplate CreateMap(CarMapTemplate model)
    {
        if (mapController is null)
        {
            log?.Invoke("SwiftDotNet.Car: CarMapTemplate needs the CarPlay navigation entitlement (com.apple.developer.carplay-maps); CarPlay did not provide a map window. Showing a fallback.");
            return CreateInfo(new CarInfoTemplate
            {
                Title = model.Title ?? "Map",
                Items = [new CarInfoItem("Map unavailable", "This CarPlay app category cannot draw maps.")]
            });
        }

        var template = NewMapTemplate();
        template.AutomaticallyHidesNavigationBar = false;
        var state = new TemplateState(model);
        var mapDelegate = new MapDelegate(state);
        template.MapDelegate = mapDelegate;
        state.Delegate = mapDelegate;
        states.Add(template, state);
        ApplyMap(template, model);
        return template;
    }

    void ApplyMap(CPMapTemplate template, CarMapTemplate model)
    {
        template.MapButtons = model.MapButtons.Select(b => new CPMapButton(_ => b.OnSelect?.Invoke())
        {
            Image = b.Image.ToUIImage(),
            Enabled = b.IsEnabled
        }).ToArray();

        var bar = ToBarButtons(model.HeaderActions).ToList();
        if (model.Content is { } content)
        {
            // CarPlay cannot overlay list content on a map; expose it as a pushed screen instead
            bar.Add(new CPBarButton(content.Title ?? "List", _ => nav.Push(new CarTemplateScreen(content))));
        }
        template.TrailingNavigationBarButtons = bar.ToArray();

        if (mapController is not null)
            mapController.Model = model;
    }

    /// <summary>The CPMapTemplate binding exposes no parameterless constructor; create it through the ObjC runtime.</summary>
    static CPMapTemplate NewMapTemplate()
    {
        var handle = IntPtr_objc_msgSend(Class.GetHandle("CPMapTemplate"), Selector.GetHandle("new"));
        return Runtime.GetNSObject<CPMapTemplate>(handle, true)!;
    }

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    sealed class MapDelegate(TemplateState state) : CPMapTemplateDelegate
    {
        CGPoint lastTranslation;
        nfloat lastScale = 1;

        CarMapTemplate Model => (CarMapTemplate)state.Model;

        public override void DidBeginPanGesture(CPMapTemplate mapTemplate) => lastTranslation = CGPoint.Empty;

        public override void DidUpdatePanGesture(CPMapTemplate mapTemplate, CGPoint translation, CGPoint velocity)
        {
            var dx = (float)(translation.X - lastTranslation.X);
            var dy = (float)(translation.Y - lastTranslation.Y);
            lastTranslation = translation;
            Model.OnPan?.Invoke(dx, dy);
        }

        public override void Pan(CPMapTemplate mapTemplate, CPPanDirection direction)
        {
            const float step = 60;
            var (dx, dy) = direction switch
            {
                CPPanDirection.Left => (step, 0f),
                CPPanDirection.Right => (-step, 0f),
                CPPanDirection.Up => (0f, step),
                CPPanDirection.Down => (0f, -step),
                _ => (0f, 0f)
            };
            Model.OnPan?.Invoke(dx, dy);
        }

        public override void DidBeginZoomGesture(CPMapTemplate mapTemplate) => lastScale = 1;

        public override void DidUpdateZoomGesture(CPMapTemplate mapTemplate, CGPoint center, nfloat scale, nfloat velocity)
        {
            if (scale <= 0)
                return;
            var factor = (float)(scale / lastScale);
            lastScale = scale;
            Model.OnZoom?.Invoke(factor, new SwiftDotNet.Graphics.Point((float)center.X, (float)center.Y));
        }
    }

    CPPointOfInterestTemplate CreatePoi(CarPointOfInterestTemplate model)
    {
        var state = new TemplateState(model);
        var template = new CPPointOfInterestTemplate(model.Title ?? string.Empty, ToPois(model), PoiSelection(model));
        var poiDelegate = new PoiDelegate(state);
        template.PointOfInterestDelegate = poiDelegate;
        state.Delegate = poiDelegate;
        template.TrailingNavigationBarButtons = ToBarButtons(model.HeaderActions);
        states.Add(template, state);
        return template;
    }

    // CarPlay expects NSNotFound (not -1) for "no selection"
    static nint PoiSelection(CarPointOfInterestTemplate model) => model.SelectedIndex < 0 ? (nint)NSRange.NotFound : model.SelectedIndex;

    static CPPointOfInterest[] ToPois(CarPointOfInterestTemplate model) =>
        model.Points.Select(p =>
        {
            var coordinate = new CLLocationCoordinate2D(p.Latitude, p.Longitude);
            var mapItem = new MKMapItem(new MKPlacemark(coordinate)) { Name = p.Title };
            var pin = p.PinColor is null
                ? null
                : CarImage.Symbol("mappin.circle.fill").WithTint(p.PinColor.Value).ToUIImage();
            return new CPPointOfInterest(mapItem, p.Title, p.Subtitle!, p.Summary!, p.Title, p.Subtitle!, p.Summary!, pin!)
            {
                PrimaryButton = p.PrimaryAction is null ? null : ToTextButton(p.PrimaryAction),
                SecondaryButton = p.SecondaryAction is null ? null : ToTextButton(p.SecondaryAction)
            };
        }).ToArray();

    sealed class PoiDelegate(TemplateState state) : CPPointOfInterestTemplateDelegate
    {
        public override void DidSelectPointOfInterest(CPPointOfInterestTemplate pointOfInterestTemplate, CPPointOfInterest pointOfInterest)
        {
            var model = (CarPointOfInterestTemplate)state.Model;
            var index = Array.IndexOf(pointOfInterestTemplate.PointsOfInterest, pointOfInterest);
            if (index >= 0 && index < model.Points.Count)
            {
                state.SelectedPoi = model.Points[index].Title;
                model.Points[index].OnSelect?.Invoke();
            }
        }

        public override void DidChangeMapRegion(CPPointOfInterestTemplate pointOfInterestTemplate, MKCoordinateRegion region) { }
    }

    #endregion

    #region Update in place

    bool TryUpdate(CPTemplate native, CarTemplate model)
    {
        if (!states.TryGetValue(native, out var state))
            return false;

        var updated = (native, model) switch
        {
            (CPListTemplate list, CarListTemplate m) when list.Title == (m.Title ?? string.Empty) => UpdateList(list, m),
            (CPGridTemplate grid, CarGridTemplate m) => UpdateGrid(grid, m),
            (CPInformationTemplate info, CarInfoTemplate m) => UpdateInfo(info, m),
            (CPTabBarTemplate tabs, CarTabsTemplate m) => UpdateTabs(tabs, state, m),
            (CPPointOfInterestTemplate poi, CarPointOfInterestTemplate m) => UpdatePoi(poi, state, m),
            (CPMapTemplate map, CarMapTemplate m) => UpdateMap(map, m),
            (CPSearchTemplate, CarSearchTemplate) => true,
            _ => false
        };

        if (updated)
            state.Model = model;
        return updated;
    }

    bool UpdateList(CPListTemplate native, CarListTemplate model)
    {
        native.UpdateSections(ToSections(model));
        ApplyList(native, model);
        return true;
    }

    bool UpdateGrid(CPGridTemplate native, CarGridTemplate model)
    {
        native.UpdateGridButtons(ToGridButtons(model));
        native.UpdateTitle(model.Title ?? string.Empty);
        native.TrailingNavigationBarButtons = ToBarButtons(model.HeaderActions);
        return true;
    }

    bool UpdateInfo(CPInformationTemplate native, CarInfoTemplate model)
    {
        native.Title = model.Title ?? string.Empty;
        native.Items = ToInfoItems(model);
        native.Actions = model.Actions.Select(ToTextButton).ToArray();
        native.TrailingNavigationBarButtons = ToBarButtons(model.HeaderActions);
        return true;
    }

    bool UpdateTabs(CPTabBarTemplate native, TemplateState state, CarTabsTemplate model)
    {
        var children = state.TabChildren ??= [];
        var sameShape = children.Count == model.Tabs.Count && model.Tabs.All(t => children.ContainsKey(t.Id));

        if (sameShape && model.Tabs.All(t => TryUpdate(children[t.Id], t.Content)))
        {
            foreach (var tab in model.Tabs)
                ApplyTab(children[tab.Id], tab);
            return true;
        }

        // shape changed - rebuild the children but keep the tab bar
        children.Clear();
        var natives = model.Tabs.Select(tab =>
        {
            var child = Create(tab.Content);
            ApplyTab(child, tab);
            children[tab.Id] = child;
            return child;
        }).ToArray();
        native.UpdateTemplates(natives);
        return true;
    }

    static bool UpdatePoi(CPPointOfInterestTemplate native, TemplateState state, CarPointOfInterestTemplate model)
    {
        // replacing the points closes the detail card; keep the user's selection open across live updates
        var selected = model.SelectedIndex;
        if (selected < 0 && state.SelectedPoi is { } title)
            selected = model.Points.ToList().FindIndex(p => p.Title == title);
        native.SetPointsOfInterest(ToPois(model), PoiSelection(model with { SelectedIndex = selected }));
        native.TrailingNavigationBarButtons = ToBarButtons(model.HeaderActions);
        return true;
    }

    bool UpdateMap(CPMapTemplate native, CarMapTemplate model)
    {
        ApplyMap(native, model);
        mapController?.Redraw();
        return true;
    }

    #endregion

    #region Buttons

    static CPBarButton[] ToBarButtons(IReadOnlyList<CarAction> actions) =>
        actions.Select(a =>
        {
            var image = a.Image.ToUIImage();
            var button = image is not null
                ? new CPBarButton(image, _ => a.OnSelect?.Invoke())
                : new CPBarButton(a.Title, _ => a.OnSelect?.Invoke());
            button.Enabled = a.IsEnabled;
            return button;
        }).ToArray();

    static CPTextButton ToTextButton(CarAction action) =>
        new(action.Title, action.Style switch
        {
            CarActionStyle.Primary => CPTextButtonStyle.Confirm,
            CarActionStyle.Destructive or CarActionStyle.Cancel => CPTextButtonStyle.Cancel,
            _ => CPTextButtonStyle.Normal
        }, _ => action.OnSelect?.Invoke());

    static CPAlertAction ToAlertAction(CarAction action) =>
        new(action.Title, action.Style switch
        {
            CarActionStyle.Destructive => CPAlertActionStyle.Destructive,
            CarActionStyle.Cancel => CPAlertActionStyle.Cancel,
            _ => CPAlertActionStyle.Default
        }, _ => action.OnSelect?.Invoke());

    #endregion
}
