using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace KitchenXR.Platform.ArFoundation
{
    /// <summary>
    /// AR Foundation 6.5 の永続アンカーで板の位置を覚える。
    ///   保存 = <c>TryAddAnchorAsync(worldPose)</c> でアンカーを1つ作り、
    ///          <c>TrySaveAnchorAsync(anchor)</c> が返す <c>SerializableGuid</c> を
    ///          <see cref="AnchorGuidFile"/> へ鍵ごとに控える
    ///   復元 = 控えた GUID で <c>TryLoadAnchorAsync(guid)</c>。返ったアンカーの Pose へ板を置く
    ///   消去 = 同じ鍵を保存し直すときは、先に <c>TryEraseAnchorAsync(古い GUID)</c>
    ///
    /// 例外を投げない。マネージャが無い・保存に対応しない・呼び出しが失敗した——どれも
    /// false／null を返すだけにして、呼び出し側は退避路（`panels.json`）へ落ちる。
    /// Meta Quest は Get Saved Anchor Ids が非対応なので GUID は自分で持つほかなく、
    /// Editor の XR Simulation はいずれも非対応なので必ず控えの側を通る。
    /// </summary>
    public sealed class ArAnchorStore : IAnchorStore
    {
        private readonly AnchorGuidFile _guidFile;

        /// <summary>鍵 → このセッションで作った／読み戻したアンカー。保存し直すときに消すために持つ。</summary>
        private readonly Dictionary<string, ARAnchor> _anchors = new Dictionary<string, ARAnchor>();

        private ARAnchorManager _manager;
        private readonly bool _searchScene;
        private bool _loadedGuids;
        private bool _warnedUnsupported;

        /// <param name="guidFile">GUID の帳簿。null なら persistentDataPath の既定。</param>
        /// <param name="manager">
        /// 使う <c>ARAnchorManager</c>。null ならシーンから探す（見つからなければ「使えない」）。
        /// </param>
        public ArAnchorStore(AnchorGuidFile guidFile = null, ARAnchorManager manager = null)
            : this(guidFile, manager, true)
        {
        }

        private ArAnchorStore(AnchorGuidFile guidFile, ARAnchorManager manager, bool searchScene)
        {
            _guidFile = guidFile ?? AnchorGuidFile.CreateDefault();
            _manager = manager;
            _searchScene = searchScene;
        }

        /// <summary>
        /// マネージャを探さない個体（試験用）。「AR Foundation が居ない機」を再現する——
        /// EditMode 試験はシーンが開いたままのことがあるので、探しに行かせると結果が揺れる。
        /// </summary>
        public static ArAnchorStore CreateWithoutManager(AnchorGuidFile guidFile = null) =>
            new ArAnchorStore(guidFile, null, false);

        public string GuidFilePath => _guidFile.FilePath;

        /// <summary>
        /// シーンに <c>ARAnchorManager</c> が在るか（<c>Bootstrap</c> がどちらの実装を挿すか決めるのに使う）。
        /// これは「アンカーが使える」の保証ではない——サブシステムが保存に対応しているかは
        /// 起動直後には分からない（AR Session が立ち上がるまで descriptor が無い）ので、
        /// 実際の可否は <see cref="SaveAsync"/>／<see cref="LoadAsync"/> の中で毎回見る。
        /// </summary>
        public static bool HasAnchorManagerInScene() => FindManager() != null;

        // ---------------------------------------------------------------- IAnchorStore

        public async UniTask<bool> SaveAsync(string key, Pose pose)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            var manager = ResolveManager();
            if (!CanSave(manager))
            {
                return false;
            }

            EnsureGuidsLoaded();

            try
            {
                // 同じ鍵の古いアンカーを先に片付ける（帳簿に GUID が1つだけ残るようにする）。
                await EraseKeyAsync(manager, key);

                var added = await manager.TryAddAnchorAsync(pose);
                if (!added.status.IsSuccess() || added.value == null)
                {
                    Debug.LogWarning($"[KitchenXR] アンカーを作れませんでした（{key}）: {added.status.nativeStatusCode}");
                    return false;
                }

                var anchor = added.value;

                var saved = await manager.TrySaveAnchorAsync(anchor);
                if (!saved.status.IsSuccess())
                {
                    Debug.LogWarning($"[KitchenXR] アンカーを保存できませんでした（{key}）: {saved.status.nativeStatusCode}");
                    return false;
                }

                _anchors[key] = anchor;
                _guidFile.Set(key, saved.value.guid);
                _guidFile.Save();
                return true;
            }
            catch (Exception e)
            {
                // NotSupportedException（サブシステムが保存に対応しない）もここで受ける。
                Debug.LogWarning($"[KitchenXR] アンカーの保存に失敗しました（{key}）: {e.Message}");
                return false;
            }
        }

        public async UniTask<Pose?> LoadAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            var manager = ResolveManager();
            if (manager == null || manager.subsystem == null || manager.descriptor == null ||
                !manager.descriptor.supportsLoadAnchor)
            {
                return null;
            }

            EnsureGuidsLoaded();

            if (!_guidFile.TryGet(key, out var guid))
            {
                return null;
            }

            try
            {
                var loaded = await manager.TryLoadAnchorAsync(new SerializableGuid(guid));
                if (!loaded.status.IsSuccess() || loaded.value == null)
                {
                    Debug.LogWarning($"[KitchenXR] アンカーを読み戻せませんでした（{key}）: {loaded.status.nativeStatusCode}");
                    return null;
                }

                var anchor = loaded.value;
                _anchors[key] = anchor;

                return new Pose(anchor.transform.position, anchor.transform.rotation);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitchenXR] アンカーの読み戻しに失敗しました（{key}）: {e.Message}");
                return null;
            }
        }

        public async UniTask ClearAsync()
        {
            var manager = ResolveManager();

            EnsureGuidsLoaded();

            if (manager != null && manager.subsystem != null && manager.descriptor != null &&
                manager.descriptor.supportsEraseAnchor)
            {
                foreach (var key in new List<string>(_guidFile.Guids.Keys))
                {
                    try
                    {
                        await EraseKeyAsync(manager, key);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[KitchenXR] アンカーを消せませんでした（{key}）: {e.Message}");
                    }
                }
            }

            _anchors.Clear();
            _guidFile.Delete();
        }

        // ---------------------------------------------------------------- 中身

        /// <summary>鍵1つ分のアンカーを、保存先からも場面からも片付ける。</summary>
        private async UniTask EraseKeyAsync(ARAnchorManager manager, string key)
        {
            if (_guidFile.TryGet(key, out var oldGuid) &&
                manager.descriptor != null && manager.descriptor.supportsEraseAnchor)
            {
                var status = await manager.TryEraseAnchorAsync(new SerializableGuid(oldGuid));
                if (status.IsError())
                {
                    // 消せなくても新しいほうを保存して帳簿を上書きすれば動く（古いのは孤児になる）。
                    Debug.LogWarning($"[KitchenXR] 古いアンカーを消せませんでした（{key}）: {status.nativeStatusCode}");
                }
            }

            _guidFile.Remove(key);

            if (_anchors.TryGetValue(key, out var oldAnchor))
            {
                _anchors.Remove(key);
                if (oldAnchor != null && manager.enabled)
                {
                    manager.TryRemoveAnchor(oldAnchor);
                }
            }
        }

        private bool CanSave(ARAnchorManager manager)
        {
            if (manager == null || manager.subsystem == null)
            {
                return false;
            }

            if (manager.descriptor == null || !manager.descriptor.supportsSaveAnchor)
            {
                if (!_warnedUnsupported)
                {
                    _warnedUnsupported = true;
                    Debug.Log("[KitchenXR] この機ではアンカーの保存に対応していません。板の位置は控え"
                              + "（panels.json）だけで覚えます（Editor の XR Simulation は常にこちら）。");
                }

                return false;
            }

            return true;
        }

        private void EnsureGuidsLoaded()
        {
            if (_loadedGuids)
            {
                return;
            }

            _loadedGuids = true;
            _guidFile.Load();
        }

        private ARAnchorManager ResolveManager()
        {
            if (_manager == null && _searchScene)
            {
                _manager = FindManager();
            }

            return _manager;
        }

        private static ARAnchorManager FindManager() =>
            UnityEngine.Object.FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Exclude);
    }
}
