# Visual Pinball Engine - DMD Integration

Enables DMD Extensions / LibDmd output in VPE.

## Structure

This project contains two root folders:

- `VisualPinball.Engine.DMD` is a small .NET proxy project. It pulls in `LibDmd.Core`
  from NuGet and copies the managed and native binaries into the Unity package.
- `VisualPinball.Engine.DMD.Unity` is the Unity UPM package that plugs into VPE.

## Development Setup

Build the proxy project first. This restores `LibDmd.Core` and deploys the binaries
into `VisualPinball.Engine.DMD.Unity/Plugins/<rid>`.

```bash
dotnet build VisualPinball.Engine.DMD/VisualPinball.Engine.DMD.csproj
```

For local development before `LibDmd.Core` is published, pass a local package source:

```bash
dotnet build VisualPinball.Engine.DMD/VisualPinball.Engine.DMD.csproj -p:RestoreSources=C:\Development\dmd-extensions\artifacts
```

Then add `VisualPinball.Engine.DMD.Unity` to a Unity project through Package Manager
or via a `file:` entry in `Packages/manifest.json`.

## License

This plugin follows the VPE package licensing model. The bundled native libraries
(`libserum`, `libzedmd`, and their transitive native dependencies) retain their
upstream licenses.
