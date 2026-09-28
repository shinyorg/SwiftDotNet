#!/usr/bin/env python3
"""
Retargets Mach-O objects built for iOS / iOS Simulator so they can be linked into watchOS binaries.

NativeAOT ships runtime packs for ios-arm64 / iossimulator-arm64 but not for watchOS. The machine code is
the same (arm64, same ABI, same libSystem), only the LC_BUILD_VERSION platform stamp differs - and ld refuses
to mix platforms. LC_BUILD_VERSION is fixed-size, so we patch platform/minos/sdk in place (vtool cannot,
because it wants header padding that compiler-produced .o files don't have).

usage: retarget-macho.py <platform> <minos> <sdk> <input .o|.a> <output> <workdir>
  platform: watchos | watchossim
"""
import os
import shutil
import struct
import subprocess
import sys

PLATFORMS = {"watchos": 4, "watchossim": 9}
MH_MAGIC_64 = 0xFEEDFACF
LC_BUILD_VERSION = 0x32
LC_VERSION_MIN_IPHONEOS = 0x25
LC_VERSION_MIN_WATCHOS = 0x30


def encode_version(v: str) -> int:
    parts = [int(p) for p in v.split(".")] + [0, 0]
    return (parts[0] << 16) | (parts[1] << 8) | parts[2]


def patch_object(data: bytearray, platform: int, minos: int, sdk: int) -> bool:
    if len(data) < 32:
        return False
    magic, _cpu, _sub, _ftype, ncmds, _size, _flags, _res = struct.unpack_from("<IiiIIIII", data, 0)
    if magic != MH_MAGIC_64:
        return False
    off = 32
    patched = False
    for _ in range(ncmds):
        cmd, cmdsize = struct.unpack_from("<II", data, off)
        if cmd == LC_BUILD_VERSION:
            struct.pack_into("<III", data, off + 8, platform, minos, sdk)
            patched = True
        elif cmd == LC_VERSION_MIN_IPHONEOS:
            # legacy form: switch the command itself to the watchOS variant (same layout)
            struct.pack_into("<I", data, off, LC_VERSION_MIN_WATCHOS)
            struct.pack_into("<II", data, off + 8, minos, sdk)
            patched = True
        off += cmdsize
    return patched


def patch_file(path: str, platform: int, minos: int, sdk: int) -> None:
    with open(path, "rb") as f:
        data = bytearray(f.read())
    if patch_object(data, platform, minos, sdk):
        with open(path, "wb") as f:
            f.write(data)


def main() -> int:
    if len(sys.argv) != 7:
        print(__doc__, file=sys.stderr)
        return 2
    plat_name, minos_s, sdk_s, src, dst, work = sys.argv[1:]
    platform = PLATFORMS[plat_name]
    minos, sdk = encode_version(minos_s), encode_version(sdk_s)
    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)

    if src.endswith(".o"):
        shutil.copyfile(src, dst)
        patch_file(dst, platform, minos, sdk)
        return 0

    if not src.endswith(".a"):
        print(f"retarget-macho: unsupported input {src}", file=sys.stderr)
        return 1

    objdir = os.path.join(work, os.path.basename(src)[:-2] + ".objs")
    os.makedirs(objdir, exist_ok=True)
    for name in os.listdir(objdir):
        if name.endswith(".o"):
            os.remove(os.path.join(objdir, name))

    # archives may contain members with duplicate names; `ar t` order + `ar xN` keeps them all
    members = subprocess.run(["ar", "t", src], check=True, capture_output=True, text=True).stdout.splitlines()
    members = [m for m in members if not m.startswith("__.SYMDEF")]
    seen: dict[str, int] = {}
    outputs = []
    for m in members:
        seen[m] = seen.get(m, 0) + 1
        idx = seen[m]
        subprocess.run(["ar", "-xN", str(idx), src, m] if idx > 1 else ["ar", "-x", src, m], cwd=objdir, check=True)
        target = os.path.join(objdir, f"{idx}_{m}")
        os.replace(os.path.join(objdir, m), target)
        patch_file(target, platform, minos, sdk)
        outputs.append(target)

    if os.path.exists(dst):
        os.remove(dst)
    subprocess.run(["libtool", "-static", "-no_warning_for_no_symbols", "-o", dst, *outputs], check=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
