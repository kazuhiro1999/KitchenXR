#if UNITY_EDITOR
using System.Collections;
using KitchenXR.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>
    /// 指先の光る点（<see cref="FingertipCursor"/>）の検算。確かめるのは2つ:
    ///   1. 近づいたときだけ出る（料理中の手にいつも点が付いて回らない）
    ///   2. 近いほど大きい（これが奥行きの合図になる）
    /// 板の代わりは「コライダー＋ XRSimpleInteractable」——台所の板（WorldSpacePanelFactory）と
    /// 同じ形で、点が「板だけ」を相手にしていることも一緒に見る。
    /// </summary>
    public class FingertipCursorTests
    {
        private GameObject _managerGo;
        private GameObject _panelGo;
        private GameObject _wallGo;
        private GameObject _pokeGo;
        private FingertipCursor _cursor;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _panelGo, _wallGo, _pokeGo, _managerGo })
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _panelGo = _wallGo = _pokeGo = _managerGo = null;
            _cursor = null;
            yield return null;
        }

        /// <summary>板（コライダー ＋ Interactable）と、指先の点を載せた Poke Interactor を1つ。</summary>
        private IEnumerator Build()
        {
            _managerGo = new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            _panelGo = new GameObject("Panel");
            _panelGo.transform.position = Vector3.zero;
            var collider = _panelGo.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.5f, 0.3f, 0.05f);
            collider.isTrigger = false;
            var interactable = _panelGo.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(collider);

            _pokeGo = new GameObject("Poke Interactor");
            _pokeGo.SetActive(false);
            var poke = _pokeGo.AddComponent<XRPokeInteractor>();
            poke.enableUIInteraction = false;
            poke.requirePokeFilter = false;
            poke.pokeDepth = 0.1f;
            _pokeGo.transform.position = new Vector3(0f, 0f, -1f);

            // 名前はシーンを組む側（KitchenSceneBuilder.FingertipCursorObjectName）と同じ。
            // PlayMode の試験は Editor の組み立て assembly を参照できないので文字で書く。
            var cursorGo = new GameObject("Fingertip Cursor");
            cursorGo.transform.SetParent(_pokeGo.transform, false);
            _cursor = cursorGo.AddComponent<FingertipCursor>();
            _cursor.BindInteractor(poke);

            _pokeGo.SetActive(true);

            yield return null;
            yield return null;
        }

        private IEnumerator MoveTo(Vector3 position)
        {
            _pokeGo.transform.position = position;
            yield return null;
            yield return null;
        }

        // 板の表の面は z = -0.025（厚み 5cm の箱の手前側）。
        private const float PanelFaceZ = -0.025f;

        [UnityTest]
        public IEnumerator 板から遠いときは点が出ない()
        {
            yield return Build();

            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - 0.3f)); // 30cm 手前

            Assert.IsFalse(_cursor.IsVisible,
                "板から 30cm 離れているのに点が出ています（料理中の手にいつも付いて回ります）。");
        }

        [UnityTest]
        public IEnumerator 板に近づくと点が出る()
        {
            yield return Build();

            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - 0.3f));
            Assert.IsFalse(_cursor.IsVisible);

            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - 0.03f)); // 3cm 手前

            Assert.IsTrue(_cursor.IsVisible,
                $"板の 3cm 手前なのに点が出ません（測った距離 {_cursor.LastDistance:0.000}m）。");
            Assert.AreEqual(0.03f, _cursor.LastDistance, 0.005f, "測った距離が合いません。");
        }

        /// <summary>
        /// 近いほど大きい——これが奥行きの合図そのもの。
        /// </summary>
        [UnityTest]
        public IEnumerator 近いほど点が大きくなる()
        {
            yield return Build();

            // 一番遠い（5cm）ところ。
            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - FingertipCursor.FarDistanceMeters + 0.002f));
            var far = _cursor.CurrentScale;

            // 3cm 手前。ここより近いと XRPokeInteractor が板を掴んで（pokeHoverRadius 1.5cm）
            // 「押した」の一瞬の光りが混ざるので、距離の見え方はここまでで測る。
            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - 0.03f));
            var near = _cursor.CurrentScale;

            Assert.Greater(near, far, "板に近づいても点が大きくなりません（奥行きの合図になりません）。");
            Assert.AreEqual(FingertipCursor.MinScale, far, 0.1f, "一番遠いときの大きさが合いません。");
            Assert.AreEqual(
                Mathf.Lerp(FingertipCursor.MaxScale, FingertipCursor.MinScale,
                    0.03f / FingertipCursor.FarDistanceMeters),
                near, 0.1f, "3cm 手前の大きさが距離に合いません。");
        }

        /// <summary>
        /// 板に触れた瞬間は一瞬だけ強く光る（ホログラムには手応えが無いので、
        /// 「今 押した」の返事がこれと効果音しかない）。
        /// </summary>
        [UnityTest]
        public IEnumerator 押した瞬間に強く光る()
        {
            yield return Build();

            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - 0.03f));
            Assert.Less(_cursor.CurrentScale, FingertipCursor.PressFlashScale,
                "まだ押していないのに強く光っています。");

            // 面まで押し込む（XRPokeInteractor が板を掴む）。
            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - 0.001f));

            Assert.AreEqual(FingertipCursor.PressFlashScale, _cursor.CurrentScale, 0.01f,
                "押したのに点が強く光りません。");
        }

        /// <summary>
        /// 点の相手は板だけ。台所の壁や鍋（Interactable を持たないコライダー）では出ない
        /// ——出てしまうと、シンクに手を伸ばすたびに点が光る。
        /// </summary>
        [UnityTest]
        public IEnumerator 板でないものには反応しない()
        {
            yield return Build();

            // 板を遠くへ退けて、代わりに Interactable を持たない壁を目の前に置く。
            _panelGo.transform.position = new Vector3(0f, 0f, 50f);

            _wallGo = new GameObject("Wall");
            _wallGo.transform.position = Vector3.zero;
            _wallGo.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.05f);

            yield return MoveTo(new Vector3(0f, 0f, PanelFaceZ - 0.02f));

            Assert.IsFalse(_cursor.IsVisible, "板ではない壁に近づいて点が出ました。");
        }
    }
}
#endif
