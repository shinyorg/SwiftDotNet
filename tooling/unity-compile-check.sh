#!/usr/bin/env bash
#
# Compiles the Unity host component against real UnityEngine reference assemblies.
#
# WHY THIS EXISTS
#   `unity/com.swiftdotnet.unity/Runtime/SwiftDotNetView.cs` is the only backend file in the repo that no
#   compiler had ever seen: it is a Unity script, and building it needs UnityEngine.dll, which only comes
#   with a Unity Editor install. This script gets the reference assemblies from NuGet instead, so at least
#   the API surface the host uses is checked mechanically rather than by eye.
#
# WHAT IT DOES *NOT* PROVE
#   * It is not a run. Nothing here creates a Texture2D, pumps input, or draws a frame.
#   * The reference assemblies are Unity 2021.1 (the newest republished on NuGet). The APIs the host uses —
#     MonoBehaviour, Texture2D, Input, Screen, GUI, NativeArrayUnsafeUtility — long predate that and have
#     not changed, but a Unity 6-only regression would not show up here.
#   * uGUI (RawImage) ships as a Unity *package*, not in UnityEngine.dll, so the two members the host
#     touches are stubbed below. Those two are checked against Unity's documentation, not a compiler.
#
#   The reference package is downloaded to a temp directory and never committed — it is a third-party
#   republish of Unity's assemblies, not something to take a build dependency on.
#
# USAGE
#   tooling/unity-compile-check.sh

set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PACKAGE=unity3d.sdk
VERSION=2021.1.14.1

echo "==> fetching UnityEngine reference assemblies ($PACKAGE $VERSION)"
curl -sSL -o "$WORK/unity.nupkg" \
  "https://api.nuget.org/v3-flatcontainer/$PACKAGE/$VERSION/$PACKAGE.$VERSION.nupkg"
unzip -q -o "$WORK/unity.nupkg" -d "$WORK/unity"

cat > "$WORK/Ugui.cs" <<'EOF'
// Minimal stand-ins for the two uGUI members the host touches. uGUI ships as com.unity.ugui, not in
// UnityEngine.dll, so a check against the reference assemblies has to supply them.
namespace UnityEngine.UI
{
    public class RawImage : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Texture texture;
        public UnityEngine.RectTransform rectTransform => null;
    }
}
EOF

echo "==> building the netstandard2.1 assemblies the Unity package ships"
for project in SwiftDotNet SwiftDotNet.Graphics SwiftDotNet.Skia; do
  dotnet build "$REPO/src/$project/$project.csproj" -f netstandard2.1 --nologo -v quiet
done
BIN="$REPO/src/SwiftDotNet.Skia/bin/Debug/netstandard2.1"

# Assembly references rather than ProjectReferences: the check project lives outside the repo, and
# MSBuild rebases a referenced project's own relative references against the *referencing* project.
cat > "$WORK/unitycheck.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Unity's scripting runtime, which is why Core / Graphics / Skia all carry this TFM. -->
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$WORK/Ugui.cs" />
    <Compile Include="$REPO/unity/com.swiftdotnet.unity/Runtime/SwiftDotNetView.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine"><HintPath>$WORK/unity/lib/UnityEngine.dll</HintPath></Reference>
    <Reference Include="SwiftDotNet"><HintPath>$BIN/SwiftDotNet.dll</HintPath></Reference>
    <Reference Include="SwiftDotNet.Graphics"><HintPath>$BIN/SwiftDotNet.Graphics.dll</HintPath></Reference>
    <Reference Include="SwiftDotNet.Skia"><HintPath>$BIN/SwiftDotNet.Skia.dll</HintPath></Reference>
    <PackageReference Include="SkiaSharp" Version="3.119.0" />
  </ItemGroup>
</Project>
EOF

echo "==> compiling SwiftDotNetView.cs"
dotnet build "$WORK/unitycheck.csproj" --nologo -v quiet

echo "==> OK: the Unity host compiles against UnityEngine $VERSION reference assemblies."
echo "    This is a compile check only — the component has still never been run in an Editor."
