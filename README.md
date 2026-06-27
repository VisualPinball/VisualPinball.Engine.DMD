# Visual Pinball Engine - DMD Integration

Enables DMD Extensions / LibDmd output in VPE.

## Structure

This project contains two root folders:

- `VisualPinball.Engine.DMD` is a small .NET proxy project. It currently builds
  against local `dmd-extensions` projects and copies the managed and native binaries
  into the Unity package.
- `VisualPinball.Engine.DMD.Unity` is the Unity UPM package that plugs into VPE.

## Development Setup

Build the proxy project first. This builds `LibDmd.Core` and deploys the binaries
into `VisualPinball.Engine.DMD.Unity/Plugins/<rid>`.

```bash
dotnet build VisualPinball.Engine.DMD/VisualPinball.Engine.DMD.csproj
```

Restart Unity after rebuilding Windows plugin binaries; the editor keeps loaded DLLs locked.

`DmdBridgePlayer` automatically loads `DmdDevice.ini` when found. Relative INI paths
are resolved from Unity's persistent data folder; `DMDDEVICE_CONFIG` is honored when set.
The INI is read-only from this package; VPE-side settings should be persisted by VPE.
The component/INI supports native-window layout and dmdext shader style keys such as
`dotsize`, `dotrounding`, `dotsharpness`, `unlitdot`, `brightness`, `dotglow`,
`backglow`, `gamma`, `glass`, and `glasslighting`.
Colorization uses the shared LibDmd converter stage. Enable it with
`[global] colorize = true`. If the component ROM name is empty, the bridge reads
the active PinMAME `romId`. If the component altcolor path is empty, it uses the
`DMDDEVICE_CONFIG` folder's `altcolor` directory, then LibDmd's VPM folder lookup.
Serum is tried first, then VNI/PAL/PAC; `[global] vni.key` and
`[global] vni.scalermode` are honored for PAC/VNI.

Then add `VisualPinball.Engine.DMD.Unity` to a Unity project through Package Manager
or via a `file:` entry in `Packages/manifest.json`.

## License

This plugin follows the VPE package licensing model. The bundled native libraries
(`libserum`, `libzedmd`, and their transitive native dependencies) retain their
upstream licenses.
