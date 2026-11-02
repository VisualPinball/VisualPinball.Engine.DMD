# Visual Pinball Engine (DMD)

Unity package for DMD Extensions / LibDmd integration in VPE.

Build the sibling `VisualPinball.Engine.DMD` proxy project before importing this
package locally so the `Plugins/<rid>` folders contain the required binaries.

This package includes its VPE Player integration. Configuration controls are rendered by the shared Player from the plugin's field schema and discovery providers; no extra Player package is needed.

The DMD runtime retains its GPL-2.0 license. The Player integration under `Runtime/Plugins` is separately [MIT licensed](Runtime/Plugins/LICENSE); its source headers and that license apply to those files. Native DMD dependencies retain their upstream licenses.

## Player cabinet outputs

The optional cabinet snapshot allows independent adoption of the DMD monitor and hardware. Missing snapshots and Custom preserve legacy settings. Player or Off suppresses the native window after DmdDevice.ini inheritance. Follow Player publishes a managed-only surface derived from the processed RGB display resource; identification and scene bindings keep their original contracts. Surface withdrawal precedes stop and native cleanup, while device leases remain until quiescence.

The factory offers six metadata-only global hardware profiles: ZeDMD, PinDMD 3, Pixelcade, PIN2DMD, PinDMD 1 and PinDMD 2. Configuration stays adapter-owned. Profile enumeration never probes hardware. Canonical serial keys and conservative singleton-family guards coordinate Custom and Player paths. A busy device disables only that output and appears in diagnostics. Central profiles currently require Windows; existing Custom platform support is unchanged. Real USB hardware and other platform/backend combinations remain uncertified.
