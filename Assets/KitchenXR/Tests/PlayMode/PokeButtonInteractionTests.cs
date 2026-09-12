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
    /// 「触れたら反応する」の検算。
    ///
    /// UI Toolkit の Button は PointerUp が同じ要素の上で起きたときだけ clicked を出すので、
    /// <c>Button.clicked</c>（押し上げ）で受けると「表面をかすめてすぐ戻す」以外——押し込んだ
    /// ままにする・深く突き抜ける——では反応しない。
    ///
    /// ここでは XR 機器なしで <see cref="XRPokeInteractor"/> を板の手前から中へ実際に動かし、
    ///   - 面から 5mm / 50mm / 200mm のどこまで突き抜けても 1回だけ発火すること
    ///   - 抜かずに留まったまま連打の抑止（600ms）を越えても 2回目が起きないこと
    /// を示す。板の組み立ては Kitchen.unity と同じ <see cref="WorldSpacePanelFactory"/> を通す。
    /// </summary>
    public class PokeButtonInteractionTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string RecipeUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipePanel.uxml";
        private const string RecipeResourcePath = "Recipes/chahan";

        // Kitchen.unity の RecipePanel と同じ寸法（UI px）。
        private const float PanelWidthUnits = 260f;
        private const float PanelHeightUnits = 190f;

        /// <summary>指を戻す位置（板の手前 6cm）。</summary>
        private const float StartDepthMeters = -0.06f;

        private GameObject _panelGo;
        private GameObject _pokeGo;
        private GameObject _rigGo;
        private GameObject _cameraGo;
        private GameObject _eventSystemGo;
        private GameObject _managerGo;

        private RecipePanel _recipePanel;
        private CookSession _session;
        private XRPokeInteractor _poke;

        private int _nextCount;
        private int _prevCount;

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
            _nextCount = 0;
            _prevCount = 0;

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
        /// ワールド空間の入力を process しない（XRI manual `ui-world-space-ui-toolkit-support` の
        /// Known limitations に同じ話が書かれている）。バッチモードには Game View 自体が無いので、
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

            // Bootstrap と同じ配線（Bootstrap 自体は XR Origin が要るのでここでは通さない）。
            // 画像の保管庫は挿さない——挿さなければ RecipePanel は札のまま（通信もしない）。
            _recipePanel.NextRequested += () =>
            {
                _nextCount++;
                _session.Apply(SessionEvent.NextRequested.Instance);
                _recipePanel.Refresh(_session);
            };
            _recipePanel.PrevRequested += () =>
            {
                _prevCount++;
                _session.Apply(SessionEvent.PrevRequested.Instance);
                _recipePanel.Refresh(_session);
            };
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
            _pokeGo.transform.position = new Vector3(0f, 0f, StartDepthMeters);
            _pokeGo.SetActive(true);

            yield return null;
        }

        /// <summary>
        /// UI 要素が実際に描かれている世界座標。
        /// ワールド空間パネルの <c>worldBound</c> は既に板のローカル単位
        /// （UI px ÷ Pixels Per Unit、y は下向き＝負）で返るので、そのまま板の Transform に載せる。
        /// つまり「目に見えている場所」を突く——狙いは pivot の設定に依存しない。
        /// </summary>
        private Vector3 WorldPositionOf(VisualElement element)
        {
            var rect = element.worldBound;
            Assert.Greater(rect.width, 0f, "UI 要素の幅が 0 です（レイアウトが未確定）。");

            return _panelGo.transform.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
        }

        /// <summary>板の手前（ローカル -Z）から <paramref name="depthMeters"/> だけ中へ指を進める（戻さない）。</summary>
        private IEnumerator PokeIn(Vector3 worldTarget, float depthMeters)
        {
            var forward = _panelGo.transform.forward; // 板の表は -Z、指は +Z 方向へ進む。

            // 刻みは 2mm。粗いと押し込みの判定窓を1フレームで飛び越してしまう。
            for (var d = StartDepthMeters; d <= depthMeters; d += 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            _pokeGo.transform.position = worldTarget + forward * depthMeters;

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        /// <summary>指を板の手前まで戻す。</summary>
        private IEnumerator PokeOut(Vector3 worldTarget, float fromDepthMeters)
        {
            var forward = _panelGo.transform.forward;

            for (var d = fromDepthMeters; d >= StartDepthMeters; d -= 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        /// <summary>押して戻す（実機で人がやること）。</summary>
        private IEnumerator PokeAt(Vector3 worldTarget, float depthMeters)
        {
            yield return PokeIn(worldTarget, depthMeters);
            yield return PokeOut(worldTarget, depthMeters);
        }

        /// <summary>時計を <paramref name="seconds"/> 秒だけ進める（連打の抑止 600ms を越えさせる）。</summary>
        private static IEnumerator WaitRealSeconds(float seconds)
        {
            var until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
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

        private IEnumerator BuildAll()
        {
            yield return BuildRig();
            yield return BuildPanel();
            yield return BuildPokeInteractor();

            Assert.AreEqual(1, _session.Current, "初期状態は1工程目のはずです。");
        }

        /// <summary>深さを変えて「押して戻す」。どの深さでも1回だけ発火する。</summary>
        private IEnumerator 押して戻すと1回だけ発火する(float depthMeters)
        {
            yield return BuildAll();
            yield return PokeAt(WorldPositionOf(NextButton), depthMeters);

            Assert.AreEqual(1, _nextCount,
                $"面から {depthMeters * 1000f:0} mm 突き抜けたとき、『次へ』の発火が1回ではありません"
                + "（押し下げ＝PointerDown で発火し、押し上げは見ない。二重発火は 600ms の抑止と"
                + "「板から離れるまで次を受けない」で止める）。");
            Assert.AreEqual(2, _session.Current, "工程が1つ進んでいません。");
        }

        // ---------------------------------------------------------------- 本題

        [UnityTest]
        public IEnumerator 面から5mmで押しても1回だけ発火する()
        {
            yield return 押して戻すと1回だけ発火する(0.005f);
        }

        [UnityTest]
        public IEnumerator 面から50mmで押しても1回だけ発火する()
        {
            yield return 押して戻すと1回だけ発火する(0.05f);
        }

        [UnityTest]
        public IEnumerator 面から200mm突き抜けても1回だけ発火する()
        {
            yield return 押して戻すと1回だけ発火する(0.2f);
        }

        /// <summary>
        /// 指を突っ込んだまま留めても2回目は起きない
        /// （連打の抑止 600ms を越えて待っても、板から離れるまでは次を受けない）。
        /// </summary>
        [UnityTest]
        public IEnumerator 抜かずに留まっても2回目は起きない()
        {
            yield return BuildAll();

            var target = WorldPositionOf(NextButton);
            yield return PokeIn(target, 0.05f);

            Assert.AreEqual(1, _nextCount, "押し下げで1回発火しているはずです。");

            // 600ms の抑止を越えて留まる。
            yield return WaitRealSeconds(1.2f);

            Assert.AreEqual(1, _nextCount,
                "指を留めたままなのに2回目が発火しました（板から離れるまで次を受けないこと）。");
            Assert.AreEqual(2, _session.Current);

            // 一度抜いて押し直せば、また受ける。
            yield return PokeOut(target, 0.05f);
            yield return WaitRealSeconds(0.7f);
            yield return PokeIn(target, 0.05f);

            Assert.AreEqual(2, _nextCount, "抜いて押し直したのに発火しませんでした。");
            Assert.AreEqual(3, _session.Current);
        }

        [UnityTest]
        public IEnumerator ポークで戻るを押すと工程が1つ戻る()
        {
            yield return BuildAll();

            // まず1つ進めておく（1工程目で戻るを押しても何も起きないため）。
            _session.Apply(SessionEvent.NextRequested.Instance);
            _recipePanel.Refresh(_session);
            yield return null;
            Assert.AreEqual(2, _session.Current);

            var back = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<Button>("backButton");
            Assert.IsNotNull(back, "『戻る』(backButton) が UXML にありません。");

            yield return PokeAt(WorldPositionOf(back), 0.05f);

            Assert.AreEqual(1, _prevCount, "『戻る』の発火が1回ではありません。");
            Assert.AreEqual(1, _session.Current, "ポークが『戻る』に変換されていません。");
        }

        [UnityTest]
        public IEnumerator 板から外れた場所を突いても工程は進まない()
        {
            yield return BuildAll();

            // 板の外（右へ 1m）。ここで進んでしまうなら当たり判定が板より大きい。
            var outside = _panelGo.transform.TransformPoint(new Vector3(10f, -0.95f, 0f));
            yield return PokeAt(outside, 0.05f);

            Assert.AreEqual(0, _nextCount, "板の外を突いたのに発火しました。");
            Assert.AreEqual(1, _session.Current, "板の外を突いたのに工程が進みました。");
        }

        /// <summary>
        /// 本文のある領域（工程の説明）を突いても進まない
        /// ——ボタンの行と重なっていないことの検算。
        /// </summary>
        [UnityTest]
        public IEnumerator 本文を突いても工程は進まない()
        {
            yield return BuildAll();

            var instruction = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<Label>("currentInstruction");
            Assert.IsNotNull(instruction, "『工程の説明』(currentInstruction) が UXML にありません。");

            yield return PokeAt(WorldPositionOf(instruction), 0.05f);

            Assert.AreEqual(0, _nextCount, "本文を突いたのに『次へ』が発火しました（ボタンと重なっています）。");
            Assert.AreEqual(1, _session.Current);
        }
    }
}
#endif
