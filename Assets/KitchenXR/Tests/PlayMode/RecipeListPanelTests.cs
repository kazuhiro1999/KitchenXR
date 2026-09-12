#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using KitchenXR.Net;
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
    /// レシピを選ぶ板の検算。
    ///
    ///   - 一覧が題名・分・分類・kcal で並ぶ（先頭は見本）
    ///   - 行を指で突くと、レシピが手元に保存され、調理セッションが始まり、調理の板に入れ替わる
    ///   - レシピの板の「一覧へ」は2度押しでだけ戻る（誤操作防止）
    ///
    /// 本物の manor へは繋がない（<see cref="IHttpTransport"/> ごと差し替える）。
    /// 保管庫も一時フォルダへ逃がす（実機の persistentDataPath を汚さない）。
    /// 突くのは <see cref="XRPokeInteractor"/> を実際に動かして——
    /// 「触れたら反応する」が新しい板でも効くことを、板越しに示す。
    /// </summary>
    public class RecipeListPanelTests
    {
        private const string PanelSettingsPath = "Assets/KitchenXR/Presentation/UI/KitchenPanelSettings.asset";
        private const string ListUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipeListPanel.uxml";
        private const string RecipeUxmlPath = "Assets/KitchenXR/Presentation/UI/RecipePanel.uxml";

        // Kitchen.unity の RecipePanel / RecipeListPanel と同じ寸法（UI px）。
        private const float PanelWidthUnits = 260f;
        private const float PanelHeightUnits = 190f;

        private const float StartDepthMeters = -0.06f;
        private const string ManorRecipeId = "12";

        private const string ManorRecipeJson = @"{
  ""id"": 12,
  ""title"": ""照り焼き"",
  ""total_minutes"": 25,
  ""phases"": [{""id"": ""cook"", ""title"": ""焼く""}],
  ""steps"": [
    {""index"": 1, ""phase"": ""cook"", ""title"": ""肉を焼く"", ""instruction"": ""皮目から焼く。""},
    {""index"": 2, ""phase"": ""cook"", ""title"": ""たれを絡める"", ""instruction"": ""煮からめる。""}
  ]
}";

        private GameObject _panelGo;
        private GameObject _pokeGo;
        private GameObject _rigGo;
        private GameObject _cameraGo;
        private GameObject _eventSystemGo;
        private GameObject _managerGo;

        private XRPokeInteractor _poke;
        private string _storeRoot;

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
            _poke = null;

            if (!string.IsNullOrEmpty(_storeRoot) && Directory.Exists(_storeRoot))
            {
                Directory.Delete(_storeRoot, true);
            }

            _storeRoot = null;
            yield return null;
        }

        // ---------------------------------------------------------------- 鋳型

        private IEnumerator BuildRig()
        {
            _managerGo = new GameObject("XR Interaction Manager", typeof(XRInteractionManager));

            _cameraGo = new GameObject("Main Camera", typeof(Camera));
            _cameraGo.tag = "MainCamera";
            _cameraGo.transform.position = new Vector3(0f, 0f, -1f);

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
        /// 試験のための細工（アプリ側の不具合ではない）。エディタでは Game View に focus が無いと
        /// ワールド空間の入力が process されない（<c>PokeButtonInteractionTests</c> と同じ理由）。
        /// </summary>
        private static void ForceEditorInputRedirect()
        {
            var type = typeof(PanelSettings).Assembly.GetType("UnityEngine.UIElements.DefaultEventSystem");
            var field = type?.GetField("IsEditorRemoteConnected",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "DefaultEventSystem.IsEditorRemoteConnected が見つかりません。");

            Func<bool> alwaysTrue = () => true;
            field.SetValue(null, alwaysTrue);
        }

        private IEnumerator BuildPanel(string name, string uxmlPath)
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            Assert.IsNotNull(panelSettings, "KitchenPanelSettings が見つかりません。");
            Assert.IsNotNull(uxml, $"{uxmlPath} が見つかりません。");

            _panelGo = new GameObject(name);
            _panelGo.SetActive(false); // UIDocument の OnEnable を配線が終わるまで待たせる。
            _panelGo.transform.position = Vector3.zero;
            _panelGo.transform.rotation = Quaternion.identity;

            WorldSpacePanelFactory.Configure(_panelGo, panelSettings, uxml, PanelWidthUnits, PanelHeightUnits);
            _panelGo.SetActive(true);

            yield return null;
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

        private static IEnumerator Settle()
        {
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }
        }

        private Vector3 WorldPositionOf(VisualElement element)
        {
            var rect = element.worldBound;
            Assert.Greater(rect.width, 0f, "UI 要素の幅が 0 です（レイアウトが未確定）。");
            return _panelGo.transform.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
        }

        private IEnumerator PokeAt(Vector3 worldTarget, float depthMeters = 0.05f)
        {
            var forward = _panelGo.transform.forward;

            for (var d = StartDepthMeters; d <= depthMeters; d += 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            for (var d = depthMeters; d >= StartDepthMeters; d -= 0.002f)
            {
                _pokeGo.transform.position = worldTarget + forward * d;
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitRealSeconds(float seconds)
        {
            var until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }
        }

        private VisualElement Root => _panelGo.GetComponent<UIDocument>().rootVisualElement;

        /// <summary>並んでいるカード。</summary>
        private List<VisualElement> Rows =>
            Root.Query<VisualElement>(className: "recipe-card").ToList();

        // ---------------------------------------------------------------- 差し替えの通信

        /// <summary>manor の代わり。レシピ1本と調理セッションだけ答える。</summary>
        private sealed class FakeManorTransport : IHttpTransport
        {
            public readonly List<string> Urls = new List<string>();
            public bool HasCurrentSession;

            public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken token)
            {
                Urls.Add(request.Url);

                if (request.Url.EndsWith("/auth/login"))
                {
                    return UniTask.FromResult(new HttpResponse(200, "{\"ok\":true,\"mode\":\"loopback\"}"));
                }

                if (request.Url.EndsWith("/cook-sessions/current"))
                {
                    return UniTask.FromResult(new HttpResponse(200, HasCurrentSession
                        ? "{\"id\":7,\"recipe_id\":12,\"current\":1}"
                        : "{\"id\":null,\"recipe_id\":null,\"current\":null}"));
                }

                if (request.Url.EndsWith("/cook-sessions"))
                {
                    return UniTask.FromResult(new HttpResponse(200, "{\"id\":7,\"current\":1}"));
                }

                if (request.Url.EndsWith("/recipes/" + ManorRecipeId))
                {
                    return UniTask.FromResult(new HttpResponse(200, ManorRecipeJson));
                }

                return UniTask.FromResult(new HttpResponse(404, ""));
            }
        }

        /// <summary>
        /// 端末の鍵で叩く客。鍵が無いと <c>IsConfigured</c> が false で、
        /// 料理長の口はそもそも叩かれない。
        /// </summary>
        private ManorClient BuildManorClient(FakeManorTransport transport)
        {
            var client = new ManorClient(
                ManorSettings.Parse("{\"base_url\": \"https://manor.example.invalid\"}"), transport);
            client.UseDeviceToken("device-token");
            return client;
        }

        private RecipeStore BuildStore()
        {
            _storeRoot = Path.Combine(Path.GetTempPath(), "KitchenXRPlayTests", Guid.NewGuid().ToString("N"));
            return new RecipeStore(_storeRoot, null); // 画像は取りに行かない（URL が無い）。
        }

        private static IReadOnlyList<RecipeSummary> SampleAndManorItems() => new List<RecipeSummary>
        {
            new RecipeSummary("chahan", "見本: 炒飯", 10, "見本", null, null, true),
            new RecipeSummary(ManorRecipeId, "照り焼き", 25, "主菜", 420, null),
        };

        // ---------------------------------------------------------------- 一覧の描画

        [UnityTest]
        public IEnumerator 一覧が題名と分と分類とkcalで並ぶ()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipeListPanel", ListUxmlPath);

            var panel = _panelGo.AddComponent<RecipeListPanel>();
            panel.Show(SampleAndManorItems(), string.Empty);

            yield return Settle();

            var rows = Rows;
            Assert.AreEqual(2, rows.Count, "一覧のカードが並んでいません。");

            var titles = rows.Select(r => r.Q<Label>(className: "recipe-card__title").text).ToList();
            Assert.AreEqual("見本: 炒飯", titles[0], "先頭は必ず見本（manor が無くても1本は進められる）。");
            Assert.AreEqual("照り焼き", titles[1]);

            var detail = rows[1].Q<Label>(className: "recipe-card__detail").text;
            StringAssert.Contains("25分", detail);
            StringAssert.Contains("主菜", detail);
            StringAssert.Contains("420kcal", detail);

            Assert.IsTrue(rows[0].ClassListContains("recipe-card--sample"), "見本のカードに印がありません。");

            // 最小4cm角。板の縮尺は 1 UI px ≒ 2mm なので 20px 以上。
            Assert.GreaterOrEqual(rows[0].resolvedStyle.height, 19.9f,
                "カードが 4cm を切ると、調理中の手では狙えません。");
        }

        /// <summary>
        /// 3列に折り返し、カードの上に写真の場所がある——写真が無いうちは「写真なし」の札。
        /// </summary>
        [UnityTest]
        public IEnumerator 一覧が3列のグリッドで写真の場所を持つ()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipeListPanel", ListUxmlPath);

            var panel = _panelGo.AddComponent<RecipeListPanel>();
            var many = new List<RecipeSummary>();
            for (var i = 0; i < 6; i++)
            {
                many.Add(new RecipeSummary($"r{i}", $"レシピ{i}", 20, "主菜", 400));
            }

            panel.Show(many, string.Empty);
            yield return Settle();

            var cards = Rows;
            Assert.AreEqual(6, cards.Count);

            // 先頭の3枚は同じ段（＝横に並ぶ）、4枚目は次の段へ折り返す。
            // layout は親から見た px の矩形（worldBound は板のローカル単位なので使わない）。
            Assert.AreEqual(cards[0].layout.y, cards[2].layout.y, 0.5f,
                "3列に並んでいません（グリッドになっていない）。");
            Assert.Greater(cards[3].layout.y, cards[0].layout.y + 1f,
                "4枚目が次の段へ折り返していません。");
            Assert.Greater(cards[1].layout.x, cards[0].layout.x + 1f,
                "2枚目が1枚目の右に並んでいません。");

            // 6件なら縦スクロール無しで全部見える（7 件目から縦スクロール）。
            // 折り返した段は容れ物の高さに出ないので、最後のカードの下端で測る。
            var scroll = Root.Q<ScrollView>("recipeScroll");
            var viewportHeight = scroll.contentViewport.resolvedStyle.height;
            Assert.LessOrEqual(cards[5].layout.yMax, viewportHeight,
                $"6 件目が表示領域からはみ出しています"
                + $"（下端 {cards[5].layout.yMax}px / 枠 {viewportHeight}px）。");

            var photo = cards[0].Q<VisualElement>(className: "recipe-card__photo");
            Assert.IsNotNull(photo, "カードに写真の場所がありません。");
            Assert.Greater(photo.resolvedStyle.height, 20f, "写真の場所が潰れています。");

            var noPhoto = photo.Q<Label>(className: "recipe-card__no-photo");
            Assert.IsNotNull(noPhoto, "写真が無いときの札がありません。");
            Assert.AreEqual(DisplayStyle.Flex, noPhoto.resolvedStyle.display,
                "写真が無いのに札が消えています。");
        }

        [UnityTest]
        public IEnumerator manor未設定なら見本だけを札つきで出す()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipeListPanel", ListUxmlPath);

            var panel = _panelGo.AddComponent<RecipeListPanel>();
            panel.Show(
                new List<RecipeSummary> { new RecipeSummary("chahan", "見本: 炒飯", 10, "見本", null, null, true) },
                "manor 未設定（見本だけ）");

            yield return Settle();

            Assert.AreEqual(1, Rows.Count);
            Assert.AreEqual("manor 未設定（見本だけ）", Root.Q<Label>("statusLabel").text);
        }

        // ---------------------------------------------------------------- 選ぶ

        /// <summary>
        /// 行を突くと、レシピが先に手元へ保存されてから調理セッションが始まり、板が入れ替わる
        /// （先に全部保存してから調理を始める）。
        /// 配線は <c>Bootstrap</c> と同じ順を試験側でなぞる（Bootstrap 自体は XR Origin を要するため）。
        /// </summary>
        [UnityTest]
        public IEnumerator 選ぶとレシピが保存されセッションが始まり調理の板へ移る()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipeListPanel", ListUxmlPath);

            var listPanel = _panelGo.AddComponent<RecipeListPanel>();
            listPanel.Show(SampleAndManorItems(), string.Empty);

            yield return Settle();
            yield return BuildPokeInteractor();

            var transport = new FakeManorTransport();
            var manor = BuildManorClient(transport);
            var store = BuildStore();

            Recipe opened = null;
            int? startedSessionId = null;
            var finished = false;

            listPanel.RecipeSelected += summary =>
            {
                OpenAsync(summary).Forget();
            };

            async UniTaskVoid OpenAsync(RecipeSummary summary)
            {
                listPanel.ShowBusy("準備中");

                var started = await manor.StartSessionAsync(summary.Id);
                startedSessionId = started.IsSuccess ? started.Value.Id : null;

                var fetched = await manor.GetRecipeAsync(summary.Id);
                if (fetched.IsSuccess)
                {
                    store.SaveRecipeJson(summary.Id, fetched.Value);
                }

                opened = store.LoadRecipe(summary.Id, null);
                await store.PrepareAsync(opened, (done, total) => listPanel.ShowPreparing(done, total));

                PanelVisibility.SetVisible(listPanel, false);
                finished = true;
            }

            // manor のレシピ（2行目）を突く。
            yield return PokeAt(WorldPositionOf(Rows[1]));

            var deadline = Time.unscaledTime + 5f;
            while (!finished && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(finished, "行を突いてもレシピが開きませんでした。");
            Assert.IsTrue(store.HasLocalRecipe(ManorRecipeId),
                "調理を始める前にレシピが手元へ保存されていません（電子レンジで通信が切れると出せなくなる）。");
            Assert.AreEqual(7, startedSessionId, "manor の調理セッションが始まっていません。");
            Assert.IsNotNull(opened);
            Assert.AreEqual("照り焼き", opened.Title);
            Assert.AreEqual(2, opened.Steps.Count);

            Assert.IsFalse(PanelVisibility.IsVisible(listPanel), "一覧の板が引っ込んでいません。");

            CollectionAssert.Contains(
                transport.Urls.Select(u => u.Substring(u.IndexOf("/api", StringComparison.Ordinal))).ToList(),
                "/api/v1/kitchen/recipes/12",
                "レシピ本体を manor から取っていません。");
        }

        [UnityTest]
        public IEnumerator 準備中は次の行を受け付けない()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipeListPanel", ListUxmlPath);

            var listPanel = _panelGo.AddComponent<RecipeListPanel>();
            listPanel.Show(SampleAndManorItems(), string.Empty);

            yield return Settle();
            yield return BuildPokeInteractor();

            var selections = 0;
            listPanel.RecipeSelected += _ =>
            {
                selections++;
                listPanel.ShowPreparing(0, 10); // 覆いを出したまま止める。
            };

            yield return PokeAt(WorldPositionOf(Rows[0]));
            Assert.AreEqual(1, selections);
            Assert.IsTrue(listPanel.IsBusy);

            // 覆いの下の別の行を突いても受けない（押し間違いを防ぐ）。
            yield return WaitRealSeconds(0.7f);
            yield return PokeAt(WorldPositionOf(Rows[1]));

            Assert.AreEqual(1, selections, "準備中なのに別のレシピが選べてしまいました。");
        }

        // ---------------------------------------------------------------- 「一覧へ」の2度押し

        [UnityTest]
        public IEnumerator 一覧へは2度押しでだけ戻る()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipePanel", RecipeUxmlPath);

            yield return null;
            var recipePanel = _panelGo.AddComponent<RecipePanel>();
            recipePanel.Refresh(new CookSession(RecipeJson.Parse(ManorRecipeJson)));

            yield return Settle();
            yield return BuildPokeInteractor();

            var backCount = 0;
            recipePanel.BackToListRequested += () => backCount++;

            var button = Root.Q<Button>("backToListButton");
            Assert.IsNotNull(button, "『一覧へ』(backToListButton) が UXML にありません。");

            // 1度目——戻らない。身構えるだけ。
            yield return PokeAt(WorldPositionOf(button));

            Assert.AreEqual(0, backCount, "1度押しただけで一覧へ戻りました（誤操作防止になっていません）。");
            Assert.IsTrue(recipePanel.IsBackToListArmed, "1度目で身構えていません。");
            Assert.AreEqual("もう一度", button.text, "2度目を待っていることが見た目に出ていません。");

            // 2度目——戻る（連打の抑止 600ms を越えてから）。
            yield return WaitRealSeconds(0.7f);
            yield return PokeAt(WorldPositionOf(button));

            Assert.AreEqual(1, backCount, "2度押しで一覧へ戻りませんでした。");
            Assert.IsFalse(recipePanel.IsBackToListArmed);
            Assert.AreEqual("一覧へ", button.text, "身構えが解けていません。");
        }

        [UnityTest]
        public IEnumerator 身構えは猶予を過ぎると解ける()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipePanel", RecipeUxmlPath);

            yield return null;
            var recipePanel = _panelGo.AddComponent<RecipePanel>();
            recipePanel.Refresh(new CookSession(RecipeJson.Parse(ManorRecipeJson)));

            yield return Settle();
            yield return BuildPokeInteractor();

            var backCount = 0;
            recipePanel.BackToListRequested += () => backCount++;

            var button = Root.Q<Button>("backToListButton");
            yield return PokeAt(WorldPositionOf(button));
            Assert.IsTrue(recipePanel.IsBackToListArmed);

            // 猶予（4秒）を過ぎると、次の1押しはまた「1度目」になる。
            yield return WaitRealSeconds(RecipePanel.BackToListConfirmSeconds + 0.5f);
            Assert.IsFalse(recipePanel.IsBackToListArmed, "猶予を過ぎても身構えたままです。");
            Assert.AreEqual("一覧へ", button.text);

            yield return PokeAt(WorldPositionOf(button));
            Assert.AreEqual(0, backCount, "猶予を過ぎた後の1押しで戻ってしまいました。");
        }

        /// <summary>
        /// 最後の工程を越えると「作り終えた」が出る（それまでは出ない）。
        /// 出るのは完了の札と同じ区画なので、調理中に誤って押すことはない。
        /// </summary>
        [UnityTest]
        public IEnumerator 作り終えたボタンは完了してから出る()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipePanel", RecipeUxmlPath);

            yield return null;
            var recipePanel = _panelGo.AddComponent<RecipePanel>();

            var session = new CookSession(RecipeJson.Parse(ManorRecipeJson));
            recipePanel.Refresh(session);
            yield return Settle();

            var complete = Root.Q<VisualElement>("completeSection");
            var finish = Root.Q<Button>("finishButton");
            Assert.IsNotNull(finish, "『作り終えた』(finishButton) が UXML にありません。");
            Assert.IsTrue(complete.ClassListContains("is-hidden"), "調理中に『作り終えた』が出ています。");

            // 2工程のレシピ。次へを3回で完了（1→2→最後を越える）。
            for (var i = 0; i < 3; i++)
            {
                session.Apply(SessionEvent.NextRequested.Instance);
            }

            Assert.IsTrue(session.IsComplete);
            recipePanel.Refresh(session);
            yield return Settle();

            Assert.IsFalse(complete.ClassListContains("is-hidden"), "完了しても『作り終えた』が出ません。");
        }

        // ---------------------------------------------------------------- ペアリングの番号

        /// <summary>
        /// 鍵が無いときは覆いに6桁を大きく出し、manor の Web の 設定 → 端末 で許可してもらう。
        /// 板に文字入力は置かない。
        ///
        /// 大きさを見るのは、これが「離れた所から読み上げる」ための文字だから——
        /// 20px は実寸 4cm（theme.uss の換算）で、押せる的と同じ寸法。
        /// </summary>
        [UnityTest]
        public IEnumerator 覆いにペアリングの番号が大きく出る()
        {
            yield return BuildRig();
            yield return BuildPanel("RecipeListPanel", ListUxmlPath);

            var panel = _panelGo.AddComponent<RecipeListPanel>();
            panel.Show(SampleAndManorItems(), "manor と繋いでいません（見本だけ）");
            yield return Settle();

            Assert.IsFalse(panel.IsBusy, "一覧を出した時点で覆いが出ています。");
            Assert.IsEmpty(panel.PairingCode);

            panel.ShowPairing("482913");
            yield return Settle();

            Assert.IsTrue(panel.IsBusy, "ペアリングの間は覆いで一覧を隠します（鍵が無いと開けません）。");
            Assert.AreEqual("482913", panel.PairingCode, "6桁が覆いに出ていません。");

            var code = Root.Q<Label>("pairCodeLabel");
            var hint = Root.Q<Label>("pairHintLabel");
            Assert.IsNotNull(code, "pairCodeLabel が UXML にありません。");
            Assert.IsFalse(code.ClassListContains("is-hidden"));
            Assert.GreaterOrEqual(code.resolvedStyle.fontSize, 19.9f,
                "番号が小さすぎます（離れた所から読めません）。");

            Assert.IsNotNull(hint, "pairHintLabel が UXML にありません。");
            Assert.IsFalse(hint.ClassListContains("is-hidden"));
            StringAssert.Contains("設定", hint.text, "どこで許可するのかが書かれていません。");
            StringAssert.Contains("端末", hint.text);

            // 許可されたら覆いごと消える（番号は残らない）。
            panel.HideBusy();
            yield return Settle();

            Assert.IsFalse(panel.IsBusy);
            Assert.IsEmpty(panel.PairingCode);
            Assert.IsTrue(code.ClassListContains("is-hidden"), "番号が覆いの外に残っています。");

            // 「準備中」の覆いには番号を出さない（用が違う）。
            panel.ShowBusy("準備中");
            yield return Settle();

            Assert.IsTrue(panel.IsBusy);
            Assert.IsEmpty(panel.PairingCode, "準備中の覆いに番号が出ています。");
        }
    }
}
#endif
