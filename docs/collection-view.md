# Collection View (`List`)

The SwiftUI `List` name is kept, but it's a full **CollectionView**: keyed identity, host-side recycling +
native virtualization, selection, grids, sections/slots, pull-to-refresh, and load-more. It's defined in
[`Core/Views/List.cs`](../src/SwiftDotNet/Core/Views/List.cs); the diff-engine support is in
[`Core/TreeDiffer.cs`](../src/SwiftDotNet/Core/TreeDiffer.cs).

> **Design decisions:** the item content stays a `Func<T, View>` closure — *that closure is the template*.
> A "template selector" is just a branch inside it (no MAUI-style `DataTemplate` type). Recycling is host-side
> + native virtualization, not true windowed streaming (see [Deferred](#deferred)).

## Keyed data

```csharp
new List()
    .ForEach(people, p => p.Id, p =>          // (items, keySelector, rowBuilder)
        new HStack(
            new Text(p.Name),
            new Spacer(),
            new Text(p.Role).ForegroundColor(Color.Secondary)
        ));
```

The key selector gives each row a **stable identity**. Identity rides as a `key` prop while node ids stay
positional (`path + "." + i`). The crux is in `TreeDiffer.DiffNode`: for a keyed container it emits
`setChildren` when the child **key sequence** changes, otherwise it recurses positionally. Without this, a
reorder of homogeneous rows would look like N in-place `updateProps`.

## Grid & horizontal

```csharp
new List().Columns(3).ForEach(items, i => i.Id, Cell);   // 3-column grid
new List().Horizontal().ForEach(items, i => i.Id, Cell);  // horizontal scroll
```

## Selection

Single or multi, keyed by row key:

```csharp
new List()
    .Selection(_selectedId)                    // State<string?>  — single
    .ForEach(items, i => i.Id, Row);

new List()
    .Selection(_selectedIds)                   // State<HashSet<string>> — multi
    .ForEach(items, i => i.Id, Row);
```

## Sections & slots

Header / footer / empty slots carry a `role` prop and are **excluded from the key sequence** (so they don't
disturb reorder diffing):

```csharp
new List()
    .Header(new Text("People"))
    .Footer(new Text($"{people.Count} total"))
    .Empty(new Text("No one here yet"))
    .ForEach(people, p => p.Id, Row);
```

## Refresh & load-more

```csharp
new List()
    .Refreshable(async () => await ReloadAsync())          // pull-to-refresh
    .OnReachEnd(() => LoadNextPage(), threshold: 3)         // infinite scroll
    .ForEach(items, i => i.Id, Row);
```

A single consolidated node action dispatches selection / refresh / load-more by value, using the sentinels
`List.RefreshValue` / `List.LoadMoreValue`.

## Backend support

All six hosts parse the same JSON patch and resolve nodes with a positional `Find(id)`:

| Backend | Notes |
|---------|-------|
| **Skia** | Reconcile-by-key `Adopt`, viewport paint-culling, scroll-aware grid, selection hit-test + highlight, scroll-driven load-more + pull refresh. **Fully test-verified.** |
| **Swift** | `ForEach(id:\.identity)`, `LazyVGrid` / `LazyHStack`, selection tap + `listRowBackground`, `.refreshable`. |
| **WinUI** | `ReconcileChildren`, grid/horizontal, selection tap + highlight. |
| **GTK** | `ReconcileChildren`, grid/horizontal, native `ListBox` selection. |
| **Web** | `SetKey`, grid/horizontal, selection click + highlight. |

Coverage: Core, Skia, Web, and GTK compile on macOS; Skia is fully test-verified (see
[`tests/SwiftDotNet.Tests`](../tests/SwiftDotNet.Tests) — e.g. `ListKeyedTests`, `SkiaKeyedReconcileTests`,
`SkiaSelectionTests`, `SkiaGridLayoutTests`, `SkiaViewportCullingTests`, `SkiaLoadMoreTests`). Swift builds
via Xcode/xcframework and WinUI only on Windows — both idiomatic but unverified locally.

## Deferred

- True Option-B **windowed streaming** for WinUI / GTK / Web.
- Web pull-refresh / load-more / windowing (needs JS-interop `scrollTop`).
- Swift load-more (last-row `onAppear`) not yet wired.

See the [Roadmap](roadmap.md).
