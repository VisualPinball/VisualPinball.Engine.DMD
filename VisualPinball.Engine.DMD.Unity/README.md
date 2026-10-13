# Visual Pinball Engine (DMD)

Unity package for DMD Extensions / LibDmd integration in VPE.

Build the sibling `VisualPinball.Engine.DMD` proxy project before importing this
package locally so the `Plugins/<rid>` folders contain the required binaries.

This package includes its VPE Player integration. Configuration controls are rendered by the shared Player from the plugin's field schema and discovery providers; no extra Player package is needed.

The DMD runtime retains its GPL-2.0 license. The Player integration under `Runtime/Plugins` is separately [MIT licensed](Runtime/Plugins/LICENSE); its source headers and that license apply to those files. Native DMD dependencies retain their upstream licenses.
