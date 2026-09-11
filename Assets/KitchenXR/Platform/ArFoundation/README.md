# ArFoundation（P2 で実装）

ここには機種・SDK 固有の実装だけを置く（`UnityEngine.XR.ARFoundation`・`OVR`・`PXR` の呼び出しは
`Platform/<系>/` の中に閉じ込める。設計 `Docs/design/PROTOTYPE.md` §4.2）。

P0/P1 では空。P2（アンカー）で以下を実装する:

- `ArAnchorStore.cs` — `IAnchorStore` の実装。`ARAnchorManager.TrySaveAnchorAsync` /
  `TryLoadAnchorAsync`（AR Foundation 6 ＋ Unity OpenXR: Meta の永続アンカー）。
  `SerializableGuid` を JSON に保存し、起動時に読み戻す（設計 §4.3）。
- `ArPassthrough.cs` — `IPassthroughControl` の実装。`ARCameraManager` 経由でパススルーを制御する。

`Platform/` の外にこれらの型が漏れていないかは EditMode 試験
（`KitchenXR.Tests.EditMode` の Platform 隔離試験）で検算する。
