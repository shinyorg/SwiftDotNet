using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests.Car;

public class ValidatorTests
{
    static CarTemplateValidator Truncating(List<string>? log = null) =>
        new(CarLimits.Portable, CarConstraintMode.Truncate, log is null ? null : log.Add);

    static CarTemplateValidator Throwing() => new(CarLimits.Portable, CarConstraintMode.Throw);

    [Fact]
    public void List_WithinLimits_IsReturnedUnchanged()
    {
        var list = CarListTemplate.Create("x", Items.Rows(3));
        Assert.Same(list, Truncating().Normalize(list));
    }

    [Fact]
    public void List_TruncatesAcrossSections_AndLogs()
    {
        var log = new List<string>();
        var list = new CarListTemplate
        {
            Sections =
            [
                new CarListSection { Header = "A", Items = Items.Rows(4) },
                new CarListSection { Header = "B", Items = Items.Rows(4) },
                new CarListSection { Header = "C", Items = Items.Rows(4) }
            ]
        };

        var result = (CarListTemplate)Truncating(log).Normalize(list);

        Assert.Equal(CarLimits.Portable.MaxListItems, result.ItemCount);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal(4, result.Sections[0].Items.Count);
        Assert.Equal(2, result.Sections[1].Items.Count);
        Assert.Contains(log, l => l.Contains("list items (12 > 6)"));
    }

    [Fact]
    public void List_ThrowMode_Throws()
    {
        var list = CarListTemplate.Create("x", Items.Rows(7));
        Assert.Throws<CarConstraintException>(() => Throwing().Normalize(list));
    }

    [Fact]
    public void Sections_AreCapped()
    {
        var list = new CarListTemplate
        {
            Sections = Enumerable.Range(0, 6).Select(i => new CarListSection { Header = $"{i}", Items = Items.Rows(1) }).ToList()
        };
        var result = (CarListTemplate)Truncating().Normalize(list);
        Assert.Equal(CarLimits.Portable.MaxListSections, result.Sections.Count);
    }

    [Fact]
    public void HeaderActions_AreCapped()
    {
        var list = CarListTemplate.Create("x", Items.Rows(1)) with
        {
            HeaderActions = [new CarAction("a"), new CarAction("b"), new CarAction("c")]
        };
        var result = Truncating().Normalize(list);
        Assert.Equal(2, result.HeaderActions.Count);
    }

    [Fact]
    public void Grid_IsCapped()
    {
        var grid = new CarGridTemplate
        {
            Items = Enumerable.Range(0, 9).Select(i => new CarGridItem { Title = $"{i}", Image = Items.Icon }).ToList()
        };
        var result = (CarGridTemplate)Truncating().Normalize(grid);
        Assert.Equal(6, result.Items.Count);
    }

    [Fact]
    public void Info_CapsItemsAndActions()
    {
        var info = new CarInfoTemplate
        {
            Items = Enumerable.Range(0, 8).Select(i => new CarInfoItem($"{i}")).ToList(),
            Actions = [new CarAction("1"), new CarAction("2"), new CarAction("3")]
        };
        var result = (CarInfoTemplate)Truncating().Normalize(info);
        Assert.Equal(4, result.Items.Count);
        Assert.Equal(2, result.Actions.Count);
    }

    [Fact]
    public void Tabs_RequireTwoTabs()
    {
        var tabs = new CarTabsTemplate
        {
            Tabs = [new CarTab { Id = "a", Title = "A", Image = Items.Icon, Content = CarListTemplate.Create(null, Items.Rows(1)) }]
        };
        Assert.Throws<CarTemplateException>(() => Truncating().Normalize(tabs));
    }

    [Fact]
    public void Tabs_RejectNonPortableContent()
    {
        var tabs = new CarTabsTemplate
        {
            Tabs =
            [
                new CarTab { Id = "a", Title = "A", Image = Items.Icon, Content = CarListTemplate.Create(null, Items.Rows(1)) },
                new CarTab { Id = "b", Title = "B", Image = Items.Icon, Content = new CarPointOfInterestTemplate() }
            ]
        };
        var ex = Assert.Throws<CarTemplateException>(() => Truncating().Normalize(tabs));
        Assert.Contains("cannot be tab content", ex.Message);
    }

    [Fact]
    public void Tabs_NormalizeContent_CapTabs_AndDefaultSelection()
    {
        var tabs = new CarTabsTemplate
        {
            SelectedTabId = "missing",
            Tabs = Enumerable.Range(0, 6).Select(i => new CarTab
            {
                Id = $"t{i}",
                Title = $"T{i}",
                Image = Items.Icon,
                Content = CarListTemplate.Create(null, Items.Rows(10))
            }).ToList()
        };

        var result = (CarTabsTemplate)Truncating().Normalize(tabs);

        Assert.Equal(4, result.Tabs.Count);
        Assert.Equal("t0", result.SelectedTabId);
        Assert.All(result.Tabs, t => Assert.Equal(6, ((CarListTemplate)t.Content).ItemCount));
    }

    [Fact]
    public void Tabs_DuplicateIds_Throw()
    {
        var tab = new CarTab { Id = "a", Title = "A", Image = Items.Icon, Content = CarListTemplate.Create(null, Items.Rows(1)) };
        Assert.Throws<CarTemplateException>(() => Truncating().Normalize(new CarTabsTemplate { Tabs = [tab, tab] }));
    }

    [Fact]
    public void TabContent_HeaderActions_AreStripped()
    {
        var tabs = new CarTabsTemplate
        {
            Tabs =
            [
                new CarTab { Id = "a", Title = "A", Image = Items.Icon, Content = CarListTemplate.Create(null, Items.Rows(1)) with { HeaderActions = [new CarAction("x")] } },
                new CarTab { Id = "b", Title = "B", Image = Items.Icon, Content = new CarGridTemplate() }
            ]
        };
        var result = (CarTabsTemplate)Truncating().Normalize(tabs);
        Assert.Empty(result.Tabs[0].Content.HeaderActions);
    }

    [Fact]
    public void Modal_CannotBeBuiltByScreen()
    {
        Assert.Throws<CarTemplateException>(() =>
            Truncating().Normalize(new CarAlertTemplate { Title = "x", Actions = [new CarAction("ok")] }));
    }

    [Fact]
    public void Modal_RequiresTitleAndAction()
    {
        Assert.Throws<CarTemplateException>(() =>
            Truncating().Normalize(new CarAlertTemplate { Title = "x" }, CarTemplatePlacement.Modal));
        Assert.Throws<CarTemplateException>(() =>
            Truncating().Normalize(new CarAlertTemplate { Actions = [new CarAction("ok")] }, CarTemplatePlacement.Modal));
    }

    [Fact]
    public void OnlyModals_CanBePresented()
    {
        Assert.Throws<CarTemplateException>(() =>
            Truncating().Normalize(CarListTemplate.Create("x", Items.Rows(1)), CarTemplatePlacement.Modal));
    }

    [Fact]
    public void Poi_CapsPoints_AndResetsOutOfRangeSelection()
    {
        var poi = new CarPointOfInterestTemplate
        {
            SelectedIndex = 9,
            Points = Enumerable.Range(0, 10).Select(i => new CarPointOfInterest { Title = $"{i}", Latitude = i, Longitude = i }).ToList()
        };
        var result = (CarPointOfInterestTemplate)Truncating().Normalize(poi);
        Assert.Equal(6, result.Points.Count);
        Assert.Equal(-1, result.SelectedIndex);
    }

    [Fact]
    public void Map_RequiresButtonImages_AndNormalizesContent()
    {
        Action<CarMapDrawContext> drawable = _ => { };
        Assert.Throws<CarTemplateException>(() =>
            Truncating().Normalize(new CarMapTemplate { Draw = drawable, MapButtons = [new CarAction("zoom")] }));

        var map = new CarMapTemplate
        {
            Draw = drawable,
            MapButtons = Enumerable.Range(0, 6).Select(i => new CarAction($"{i}") { Image = Items.Icon }).ToList(),
            Content = CarListTemplate.Create(null, Items.Rows(9))
        };
        var result = (CarMapTemplate)Truncating().Normalize(map);
        Assert.Equal(4, result.MapButtons.Count);
        Assert.Equal(6, ((CarListTemplate)result.Content!).ItemCount);

        Assert.Throws<CarTemplateException>(() =>
            Truncating().Normalize(map with { Content = new CarSearchTemplate { Search = (_, _) => Task.FromResult<IReadOnlyList<CarListItem>>([]) } }));
    }

    [Fact]
    public void SearchResults_AreCapped()
    {
        var results = Enumerable.Range(0, 20).Select(i => new CarListItem { Title = $"{i}" }).ToList();
        Assert.Equal(6, Truncating().NormalizeSearchResults(results).Count);
    }

    [Fact]
    public void Limits_ResolveFromOptions()
    {
        var platform = CarLimits.Portable with { MaxListItems = 100 };
        Assert.Equal(6, new CarOptions().ResolveLimits(platform).MaxListItems);
        Assert.Equal(100, new CarOptions { LimitsSource = CarLimitsSource.Platform }.ResolveLimits(platform).MaxListItems);
        Assert.Equal(3, new CarOptions { Limits = CarLimits.Portable with { MaxListItems = 3 } }.ResolveLimits(platform).MaxListItems);
        Assert.Equal(6, platform.Min(CarLimits.Portable).MaxListItems);
    }

    [Fact]
    public void SymbolImage_DerivesAndroidName()
    {
        Assert.Equal("bolt_car_fill", ((CarSymbolImage)CarImage.Symbol("bolt.car-fill")).AndroidResourceName);
        Assert.Equal("ic_bolt", ((CarSymbolImage)CarImage.Symbol("bolt.car", "ic_bolt")).AndroidResourceName);
    }

}

public class CategoryTests
{
    static CarTemplateValidator For(CarPlatform platform, CarAppCategory? category) =>
        new(CarLimits.Portable, CarConstraintMode.Truncate, null, platform, category);

    static readonly CarMapTemplate Map = new() { Draw = _ => { } };

    [Fact]
    public void CarPlay_OnlyNavigationDrawsMaps()
    {
        Assert.False(For(CarPlatform.CarPlay, CarAppCategory.Charging).Supports(typeof(CarMapTemplate)));
        Assert.True(For(CarPlatform.CarPlay, CarAppCategory.Navigation).Supports(typeof(CarMapTemplate)));
        Assert.Throws<CarTemplateException>(() => For(CarPlatform.CarPlay, CarAppCategory.Charging).Normalize(Map));
    }

    [Fact]
    public void CarPlay_OnlyNavigationSearches()
    {
        Assert.False(For(CarPlatform.CarPlay, CarAppCategory.Charging).Supports(typeof(CarSearchTemplate)));
        Assert.True(For(CarPlatform.CarPlay, CarAppCategory.Navigation).Supports(typeof(CarSearchTemplate)));
        Assert.True(For(CarPlatform.AndroidAuto, CarAppCategory.Charging).Supports(typeof(CarSearchTemplate)));
    }

    [Fact]
    public void AndroidAuto_PoiCategoriesMayDrawMaps()
    {
        Assert.True(For(CarPlatform.AndroidAuto, CarAppCategory.Charging).Supports(typeof(CarMapTemplate)));
        Assert.False(For(CarPlatform.AndroidAuto, CarAppCategory.Audio).Supports(typeof(CarMapTemplate)));
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay)]
    [InlineData(CarPlatform.AndroidAuto)]
    public void Navigation_CannotUsePointOfInterest(CarPlatform platform)
    {
        Assert.False(For(platform, CarAppCategory.Navigation).Supports(typeof(CarPointOfInterestTemplate)));
        Assert.True(For(platform, CarAppCategory.Charging).Supports(typeof(CarPointOfInterestTemplate)));
    }

    [Fact]
    public void NoCategory_AllowsEverything()
    {
        Assert.True(For(CarPlatform.CarPlay, null).Supports(typeof(CarMapTemplate)));
        Assert.True(new CarTemplateValidator(CarLimits.Portable, CarConstraintMode.Throw).Supports(typeof(CarPointOfInterestTemplate)));
    }

    [Fact]
    public void Navigator_ExposesSupports()
    {
        var nav = new CarNavigator(new FakeRenderer(), new EmptyServices(), new CarOptions { Category = CarAppCategory.Charging }, platform: CarPlatform.CarPlay);
        Assert.False(nav.Supports<CarMapTemplate>());
        Assert.True(nav.Supports<CarPointOfInterestTemplate>());
        Assert.Equal(CarPlatform.CarPlay, nav.Platform);
    }

}
