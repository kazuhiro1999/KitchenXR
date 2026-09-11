#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using KitchenXR.Domain;
using KitchenXR.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>
    /// 「指で『次へ』を押すと工程が1つ進む」を機械で示す検算（2026-09-12 実機・v1.0.1 の残り1件）。
    ///
    /// v1.0.1 は受け口（XRUIToolkitManager・PanelInputConfiguration・bypassUIToolkitEvents=false・
    /// コライダーの寸法）を揃えただけで、「押すと動く」を試していなかった。
    /// 実機では板に触ると色が変わり振動する（＝XRSimpleInteractable のホバーは届く）のに
    /// ボタンが反応しない、という状態だった——当たり判定には当たっているが、
    /// その当たり点を板のローカル座標へ写すと文字の外に落ちていたため。
    ///
    /// ここでは XR 機器なしで <see cref="XRPokeInteractor"/> を板の手前から中へ実際に動かし、
    /// <see cref="CookSession.Current"/> が 1 → 2 になることを確かめる。
    /// 板の組み立ては Kitchen.unity と同じ <see cref="WorldSpacePanelFactory"/> を通す。
    /// </summary>
    public class PokeButtonInteractionTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string RecipeUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipePanel.uxml";
        private const string RecipeResourcePath = "Recipes/chahan";

        // Kitchen.unity の RecipePanel と同じ寸法（UI px）。
        private const float PanelWidthUnits = 260f;
        private const float PanelHeightUnits = 190f;

        private GameObject _panelGo;
        private GameObject _pokeGo;
        private GameObject _rigGo;
        private GameObject _cameraGo;
        private GameObject _eventSystemGo;
        private GameObject _managerGo;

        private RecipePanel _recipePanel;
        private CookSession _session;
        private XRPokeInteractor _poke;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _panelGo, _pokeGo, _rigGo, _cameraGo, _eventSystemGo, _managerGo })
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }

            _panelGo = _pokeGo = _rigGo = _cameraGo = _eventSystemGo = _managerGo = null;
            _recipePanel = null;
            _session = null;
            _poke = null;

            yield return null;
        }

        /// <summary>
        /// Kitchen.unity と同じ受け口を1式だけ立てる
        /// （XRI manual `ui-world-space-ui-toolkit-support` の「Scene Configuration」）。
        /// </summary>
        private IEnumerator BuildRig()
        {
            _managerGo = new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            _cameraGo = new GameObject("Main Camera", typeof(Camera));
            _cameraGo.tag = "MainCamera";
            _cameraGo.transform.position = new Vector3(0f, 0f, -1f);
            _cameraGo.transform.rotation = Quaternion.identity;

            // EventSystem ＋ XRUIInputModule。bypassUIToolkitEvents を立てると UI Toolkit に届かない。
            _eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
            var inputModule = _eventSystemGo.AddComponent<XRUIInputModule>();
            inputModule.bypassUIToolkitEvents = false;

            _rigGo = new GameObject("UI Toolkit Support");
            _rigGo.AddComponent<XRUIToolkitManager>();

            var panelInput = _rigGo.AddComponent<PanelInputConfiguration>();
            panelInput.panelInputRedirection = PanelInputConfiguration.PanelInputRedirection.Never;
            panelInput.processWorldSpaceInput = true;

            ForceEditorInputRedirect();

            yield return null;
        }

        /// <summary>
        /// 試験のための細工（アプリ側の不具合ではない）。
        ///
        /// UI Toolkit の <c>DefaultEventSystem</c> は、エディタでは Game View に focus が無いと
        /// ワールド空間の入力を processs しない（XRI manual `ui-world-space-ui-toolkit-support` の
        /// Known limitations に同じ話が書かれている）。バッチモードには Game View that自体が無いので、
        /// この細工をしないと実機では動く配線でも PointerMove すら届かない。
        ///
        /// XRI 自身も XR 機器が有効なときは同じフラグを立てている
        /// （<c>XRUIToolkitHandler.UpdateEventSystem</c> の <c>IsEditorRemoteConnected</c>）。
        /// 実機（Player ビルド）ではこの分岐自体が無いので、ここだけの話。
        /// </summary>
        private static void ForceEditorInputRedirect()
        {
            var type = typeof(PanelSettings).Assembly
                .GetType("UnityEngine.UIElements.DefaultEventSystem");
            var field = type?.GetField("IsEditorRemoteConnected",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsNotNull(field,
                "DefaultEventSystem.IsEditorRemoteConnected が見つかりません。"
                + " Unity の更新でこの細工が要らなくなったか、名前が変わった可能性があります。");

            Func<bool> alwaysTrue = () => true;
            field.SetValue(null, alwaysTrue);
        }

        private IEnumerator BuildPanel()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RecipeUxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(uxml, "RecipePanel.uxml が見つかりません。");

            _panelGo = new GameObject("RecipePanel");
            _panelGo.SetActive(false); // UIDocument の OnEnable を配線が終わるまで待たせる。

            _panelGo.transform.position = Vector3.zero;
            _panelGo.transform.rotation = Quaternion.identity;

            WorldSpacePanelFactory.Configure(_panelGo, panelSettings, uxml, PanelWidthUnits, PanelHeightUnits);

            _panelGo.SetActive(true);

            // rootVisualElement が出来てから RecipePanel の Awake を走らせる。
            yield return null;

            _recipePanel = _panelGo.AddComponent<RecipePanel>();

            var text = Resources.Load<TextAsset>(RecipeResourcePath);
            Assert.IsNotNull(text, $"見本レシピが読めません: Resources/{RecipeResourcePath}.json");
            _session = new CookSession(RecipeJson.Parse(text.text));

            // Bootstrap と同じ配線（Bootstrap 自体は XR Origin を要るのでここでは通さない）。
            _recipePanel.NextRequested += () => _session.Apply(SessionEvent.NextRequested.Instance);
            _recipePanel.PrevRequested += () => _session.Apply(SessionEvent.PrevRequested.Instance);
            _recipePanel.Refresh(_session);

            // レイアウトが確定するまで回す（worldBound が 0 のままだと狙えない）。
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }
        }

        private IEnumerator BuildPokeInteractor()
        {
            _pokeGo = new GameObject("Poke Interactor");
            _pokeGo.SetActive(false);
            _poke = _pokeGo.AddComponent<XRPokeInteractor>();
            _poke.enableUIInteraction = true;
            _poke.requirePokeFilter = false;
            _poke.pokeDepth = 0.1f;
            _pokeGo.SetActive(true);

            yield return null;
        }

        /// <summary>
        /// UI 要素が実際に描かれている世界座標。
        /// ワールド空間パネルの <c>worldBound</c> は既に板のローカル単位
        /// （UI px ÷ Pixels Per Unit、y は下向き＝負）で返るので、そのまま板の Transform に載せる。
        /// つまり「主人が見えている場所」を突く——狙いは pivot の設定に依存しない。
        /// </summary>
        private Vector3 WorldPositionOf(VisualElement element)
        {
            var rect = element.worldBound;
            Assert.Greater(rect.width, 0f, "UI 要素の幅が 0 です（レイアウトが未確定）。");

            return _panelGo.transform.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
        }

        /// <summary>
        /// 板の手前（ローカル -Z）から中（+Z）へ指を動かし、離す。
        ///
        /// <paramref name="overshootMeters"/> は板の面から何メートル奥まで突っ込むか。
        /// ホログラムの板には手応えが無いので、主人の指は面で止まらず深く入る——
        /// v1.0.1 が実機で反応しなかったのはここで指が当たり判定から出ていたため。
        /// 既定を深め（8cm）にして、その状況でも押せることを常に確かめる。
        /// </summary>
        private IEnumerator PokeAt(Vector3 worldTarget, float overshootMeters = 0.08f)
        {
            var forward = _panelGo.transform.forward; // 板の表は -Z、指は +Z 方向へ進む。

            // 刻みは 2mm。粗いと押し込みの判定窓を1フレームで飛び越してしまう。
            for (var d = -0.06f; d <= overshootMeters; d += 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 3; i++)
            {
                yield return null;
            }

            for (var d = overshootMeters; d >= -0.06f; d -= 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        private Button NextButton
        {
            get
            {
                var button = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<Button>("nextButton");
                Assert.IsNotNull(button, "『次へ』(nextButton) が UXML にありません。");
                return button;
            }
        }

        // ---------------------------------------------------------------- 本題

        [UnityTest]
        public IEnumerator ポークで次へを押すと工程が1つ進む()
        {
            yield return BuildRig();
            yield return BuildPanel();
            yield return BuildPokeInteractor();

            Assert.AreEqual(1, _session.Current, "初期状態は1工程目のはずです。");

            yield return PokeAt(WorldPositionOf(NextButton));

            Assert.AreEqual(2, _session.Current,
                "ポークが UI Toolkit の Button のクリックに変換されていません。"
                + " 板のコライダーと UIDocument の矩形が揃っているか"
                + "（UIDocument.pivot = TopLeft）、XRUIToolkitManager・PanelInputConfiguration(Never)・"
                + "bypassUIToolkitEvents=false が揃っているかを確かめること。");
        }

        [UnityTest]
        public IEnumerator ポークで戻るを押すと工程が1つ戻る()
        {
            yield return BuildRig();
            yield return BuildPanel();
            yield return BuildPokeInteractor();

            // まず1つ進めておく（1工程目で戻るを押しても何も起きないため）。
            _session.Apply(SessionEvent.NextRequested.Instance);
            _recipePanel.Refresh(_session);
            yield return null;
            Assert.AreEqual(2, _session.Current);

            var back = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<Button>("backButton");
            Assert.IsNotNull(back, "『戻る』(backButton) が UXML にありません。");

            yield return PokeAt(WorldPositionOf(back));

            Assert.AreEqual(1, _session.Current, "ポークが『戻る』のクリックに変換されていません。");
        }

        [UnityTest]
        public IEnumerator 板から外れた場所を突いても工程は進まない()
        {
            yield return BuildRig();
            yield return BuildPanel();
            yield return BuildPokeInteractor();

            // 板の外（右へ 1m）。ここで進んでしまうなら当たり判定が板より大きい。
            var outside = _panelGo.transform.TransformPoint(new Vector3(10f, -0.95f, 0f));
            yield return PokeAt(outside);

            Assert.AreEqual(1, _session.Current, "板の外を突いたのに工程が進みました。");
        }
    }
}
#endif
