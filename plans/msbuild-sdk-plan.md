# Plan: A SwiftDotNet MSBuild project SDK (and custom target frameworks)

**Status:** Draft for review — **nothing built**; a throwaway prototype was used to establish the
findings in §2 · **Date:** 2026-07-20
**Scope:** A `SwiftDotNet.Sdk` project SDK that picks the right backend packages/references for an app,
and — separately — whether SwiftDotNet should mint its own target-framework monikers
(`net10.0-gtk`, `net10.0-web`, `net10.0-skia`, …).

---

## 1. Two questions, very different risk

These get conflated but are independent, and only the first is cheap:

| | Question | Verdict |
|---|---|---|
| **A** | Ship a project SDK that decides references, constants and defaults from one declared backend | ✅ Standard, low risk, do it |
| **B** | Mint custom TFMs so the backend is a *dimension of the TFM* | ⚠️ Works, but viral — see §2.4 |

**A does not need B.** The dependency-picking payoff is available today keyed off an ordinary property.

## 2. Prototype findings (measured, not assumed)

A scratch SDK + multi-targeted app was built against the .NET 10.0.302 SDK. Everything below was
observed, with the exact diagnostics.

### 2.1 A custom SDK is just a wrapper

```xml
<!-- Sdk/Sdk.props -->
<Project>
  <PropertyGroup> … </PropertyGroup>
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
</Project>

<!-- Sdk/Sdk.targets -->
<Project>
  <ItemGroup Condition="'$(SwiftDotNetPlatform)' == 'gtk'">
    <ProjectReference Include="…/SwiftDotNet.Gtk.csproj" />
  </ItemGroup>
  <PropertyGroup>
    <DefineConstants>$(DefineConstants);SWIFTDOTNET_$(SwiftDotNetPlatform.ToUpperInvariant())</DefineConstants>
  </PropertyGroup>
  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
</Project>
```

A two-TFM app built cleanly with each head getting only its own backend, and the matching `#if`
branch compiling. This is the same shape as `Microsoft.NET.Sdk.Maui` / `.BlazorWebAssembly`.

### 2.2 Custom TFMs *do* work — three obstacles, all surmountable

| Obstacle | Diagnostic | Resolution |
|---|---|---|
| Unknown platform | `NETSDK1139: The target platform identifier gtk was not recognized.` | Set `TargetPlatformSupported=true`. It is the **only** gate — see `Microsoft.NET.TargetFrameworkInference.targets`, target `_CheckForUnsupportedTargetPlatformIdentifier`. |
| Platform version rejected | `NETSDK1140: 1.0 is not a valid TargetPlatformVersion for linux. Valid versions include: None` | Also set `TargetPlatformVersionSupported=true`. |
| Version omitted | `NU1012: Platform version is not present for one or more target frameworks` | The moniker **must** carry a version: `net10.0-gtk1.0`, not `net10.0-gtk`. |

**Gotcha that cost a build:** these must be derived from the **raw `$(TargetFramework)` string**, because
`$(TargetPlatformIdentifier)` does not exist yet when `Sdk.props` is evaluated:

```xml
<SwiftDotNetPlatform Condition="$(TargetFramework.Contains('-gtk'))">gtk</SwiftDotNetPlatform>
<TargetPlatformSupported Condition="'$(SwiftDotNetPlatform)' != ''">true</TargetPlatformSupported>
<TargetPlatformVersionSupported Condition="'$(SwiftDotNetPlatform)' != ''">true</TargetPlatformVersionSupported>
```

### 2.3 Packing works properly

`dotnet pack` produced exactly what you would want:

```
lib/net10.0-gtk1.0/app.dll
lib/net10.0-skia1.0/app.dll

<group targetFramework="net10.0-gtk1.0">  <dependency id="SwiftDotNet.Gtk"  … /></group>
<group targetFramework="net10.0-skia1.0"> <dependency id="SwiftDotNet.Skia" … /></group>
```

### 2.4 The cost: custom TFMs are viral

A stock-SDK project consuming that package:

```
error NU1202: Package app 1.0.0 is not compatible with net10.0 (.NETCoreApp,Version=v10.0).
  Package app 1.0.0 supports:  - net10.0-gtk1.0   - net10.0-skia1.0
```

Combined with `NETSDK1139` (a stock SDK cannot even *build* such a TFM), this means **every consumer
down the chain must adopt `SwiftDotNet.Sdk`**. For an app that is fine. For a library it is a hard
compatibility break.

Lesser costs, also observed:

- **`CA1418: The platform 'gtk' is not a known platform name`** — one warning per project. Suppressible
  in the SDK, but it is noise we would be choosing to create.
- **Names collide with real OS platforms.** `net10.0-linux1.0` builds, but earns a *different* CA1418
  (`Version '1.0' is not valid for platform 'linux'`) because `linux` is a platform the
  `SupportedOSPlatform` analyzer already knows. **`linux` is a bad moniker; `gtk` is a good one** — name
  after the toolkit, not the OS.
- **No workload story.** No `dotnet workload install swiftdotnet`, no IDE TFM awareness, no analyzer
  integration. `SupportedOSPlatform`-style guard-rail semantics do not apply to our monikers.

## 3. Why this is tempting anyway

[`CLAUDE.md`](../CLAUDE.md) records the exact constraint custom TFMs would dissolve:

> GTK / Web / Skia are **separate** projects (they share the `net10.0` TFM with Core, so they can't fold
> into the multi-target library without forcing their dependency on every consumer).

With custom TFMs, `net10.0-gtk` / `net10.0-web` / `net10.0-skia` become dimensions of the one
multi-targeted library, exactly as `net10.0-ios` already is — the separate projects exist *only* because
the TFM system has no way to express "the GTK flavour of net10.0". That is a real architectural win, not
cosmetics.

The counter-argument is §2.4: the reason those backends are separate is to avoid forcing a dependency on
every consumer, and custom TFMs trade that for forcing *the SDK* on every consumer. It is the same tax
in a different currency — worth paying for apps, not for libraries.

## 4. Recommended shape

Split by audience, the way MAUI does:

| Layer | Recommendation |
|---|---|
| **Apps / platform heads** | `SwiftDotNet.Sdk`. Lock-in is free here — an app has already committed to the framework — and the payoff is largest: one declaration picks the backend reference, the constants, the entry point and the host base. |
| **Libraries** (`SwiftDotNet`, `.Controls`, `.Maps`, …) | **Keep standard TFMs.** `NU1202` is not an acceptable trade for a library anyone might consume. |
| **Custom TFMs** | **Defer.** Adopt only if/when apps are the dominant consumer and the separate-backend-project split becomes the bigger pain. |

### 4.1 The 80/20: property, not moniker

Ship the SDK first with the backend as an ordinary property. Same conditional machinery, zero NuGet
compatibility loss, no `CA1418`, no viral consumers:

```xml
<Project Sdk="SwiftDotNet.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SwiftDotNetBackend>gtk</SwiftDotNetBackend>   <!-- gtk | web | skia | skia-silk … -->
  </PropertyGroup>
</Project>
```

The SDK then supplies: the backend `PackageReference`, `SWIFTDOTNET_GTK`, sensible `OutputType`,
the `[Inject]` source generator (which today every in-repo consumer must reference by hand — see
[Hosting & DI](../docs/hosting-and-di.md) gotchas), and for Apple heads the
`SwiftDotNetBridge.targets` import that `NativeReference` cannot flow transitively.

That last pair is worth emphasising: **the SDK earns its keep purely as a papering-over of existing
per-project boilerplate**, before any TFM question is asked.

## 5. Open decisions

1. **Ship the SDK at all, or leave app wiring hand-rolled?** *Rec: ship — the generator reference and
   the Apple `.targets` import are per-project boilerplate today.*
2. **Backend as property (§4.1) or as TFM (§2.2)?** *Rec: property now; revisit TFMs later.*
3. **If TFMs: which monikers?** Toolkit-named (`gtk`, `skia`, `web`) — **not** OS-named (`linux`), which
   collides with analyzer-known platforms (§2.4). Note the mandatory version suffix (`-gtk1.0`).
4. **One SDK or several?** A single `SwiftDotNet.Sdk` switching on the backend, vs `SwiftDotNet.Sdk.Gtk`
   etc. *Rec: one — the backend list is small and the switch is trivial.*
5. **Does the SDK own the host entry point too?** It could supply `Program.cs`-equivalent glue per
   backend, so a head is a `.csproj` plus `SwiftProgram.cs` and nothing else. *Rec: defer to a second
   pass; get references right first.*
6. **Workload instead?** A real .NET workload (how Tizen ships `net10.0-tizen`) gives genuine TFMs,
   `dotnet workload install`, and IDE recognition, with no `TargetPlatformSupported` hack. *Rec: not now
   — manifest, packs, and a release per SDK feature band is a disproportionate lift at this stage.*

## 6. Prior art

- **`Microsoft.NET.Sdk.Maui`, `Microsoft.NET.Sdk.BlazorWebAssembly`, `Microsoft.NET.Sdk.Web`** — the
  wrapper-SDK pattern in §2.1.
- **Tizen (`net10.0-tizen`)** — a third party shipping a genuine platform TFM, via a workload.
- **Uno Platform** — ships an SDK and leans on standard TFMs rather than minting its own.

## 7. Related

- [Hosting & Dependency Injection](../docs/hosting-and-di.md) — the generator reference and `UseX()`
  seam the SDK would automate.
- [`plans/README.md`](README.md) — plan index and statuses.
