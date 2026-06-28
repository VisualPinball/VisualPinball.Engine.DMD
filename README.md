# Visual Pinball Engine - DMD Integration

This repository enables [DMD Extensions](https://github.com/freezy/dmd-extensions) / LibDmd output
within the [Visual Pinball Engine](https://github.com/freezy/VisualPinball.Engine) (VPE). It drives
real DMD hardware (ZeDMD, PinDMD v1/v2/v3, PIN2DMD, Pixelcade), an on-screen virtual DMD in its own
native window, and the in-scene DMD — all from one shared, colorized frame pipeline.

## Structure

This project contains two root folders:

- `VisualPinball.Engine.DMD` wraps the cross-platform `LibDmd.Core` library (from dmd-extensions)
  and supplies the Unity plugins. It currently builds against local `dmd-extensions` projects and
  copies the managed assemblies and per-platform native binaries into the Unity package.
- `VisualPinball.Engine.DMD.Unity` is the UPM package that integrates with VPE.

## Unity Package

In order to use the package from our registry, open the Package Manager in Unity, and add
`org.visualpinball.engine.dmd` under *Add package from git URL*.

To import the package locally instead of from the registry, clone and compile it. This will copy the
necessary binaries into the Unity folder (see Development Setup).

## Development Setup

For local development, clone the repository and build the proxy project first. This builds
`LibDmd.Core` (with the Windows and SDL/GL-ES backends and the serial shim) and deploys the binaries
into the Unity package — the managed assemblies into `Plugins/Managed` as a single, shared "Any
Platform" copy, and the native libraries into `Plugins/<rid>`.

```bash
dotnet build VisualPinball.Engine.DMD/VisualPinball.Engine.DMD.csproj
```

Restart Unity after rebuilding the plugin binaries; the editor keeps loaded DLLs locked and cannot
hot-swap them.

The build regenerates the platform-specific plugin `.meta` files. To keep those local changes out
of your commits, mark them assume-unchanged:

```bash
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/LibDmd.Core.Sdl.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/LibDmd.Core.Windows.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/LibDmd.Core.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/System.IO.Ports.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/System.Reactive.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/System.Runtime.CompilerServices.Unsafe.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/System.Threading.Tasks.Extensions.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/Managed/VisualPinball.Engine.DMD.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libcargs.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libserialport.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libserialport.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libserum.2.5.2.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libserum.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libsockpp.1.0.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libsockpp.1.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libsockpp.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libusb-1.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libzedmd.0.11.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-arm64/libzedmd.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libcargs.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libserialport.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libserialport.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libserum.2.5.2.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libserum.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libsockpp.1.0.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libsockpp.1.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libsockpp.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libusb-1.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libzedmd.0.11.0.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/osx-x64/libzedmd.dylib.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/ftd2xx.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/libSkiaSharp.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/libserialport.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/libserialport64-0.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/libusb-1.0.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/serum64.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/sockpp64.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/sqlite3.dll.meta
git update-index --assume-unchanged VisualPinball.Engine.DMD.Unity/Plugins/win-x64/zedmd64.dll.meta
```

### Configuration

`DmdBridgePlayer` loads `DmdDevice.ini` when present (relative paths resolve from Unity's persistent
data folder; `DMDDEVICE_CONFIG` is honored when set). The INI is read-only here — VPE persists its
own settings. Supported sections include `[virtualdmd]` (native-window layout plus the dmdext style
keys `dotsize`, `dotrounding`, `dotsharpness`, `unlitdot`, `brightness`, `dotglow`, `backglow`,
`gamma`, `glass`, `glasslighting`), `[zedmd]`, `[pindmd1]`/`[pindmd2]`/`[pindmd3]`, `[pixelcade]`,
`[pin2dmd]`, and per-ROM colorization.

Colorization runs once in the shared LibDmd converter stage; enable it with `[global] colorize =
true`. If the component ROM name is empty, the bridge reads the active PinMAME `romId`; if the
altcolor path is empty, it uses the `DMDDEVICE_CONFIG` folder's `altcolor` directory, then LibDmd's
VPM folder lookup. Serum is tried first, then VNI/PAL/PAC, with `[global] vni.key` and
`[global] vni.scalermode` honored.

## License

This plugin is licensed under the GPL-2.0 license, matching DMD Extensions / LibDmd, which it builds
on. The bundled native libraries (`libserum`, `libzedmd`, `libusb`, `libftd2xx`, `libserialport`,
and their transitive native dependencies) retain their respective upstream licenses.
