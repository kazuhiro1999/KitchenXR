using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using KitchenXR.Net;
using KitchenXR.Platform;
using KitchenXR.Platform.Null;
using KitchenXR.Presentation;
using UnityEngine;

namespace KitchenXR.App
{
    /// <summary>
    /// どのアダプタを挿すかはここだけ（設計 §4.2）。見本レシピを読み、CookSession を作り、
    /// レシピ／材料／タイマーの3枚のパネルへ配る。アンカーは P2 まで <see cref="InMemoryAnchorStore"/>。
    ///
    /// レシピと画像は <see cref="RecipeStore"/>（persistentDataPath）を通す（設計 §11 追補）。
    /// 見本（Resources）も初回に写して、以後はローカルのものを読む——電子レンジで通信が切れても
    /// 同じ画面が出る。通信があるうちに残りの画像を裏で取っておく。
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [Header("見本レシピ（Resources 配下。P1 はローカル JSON。設計 ROADMAP P1）")]
        [SerializeField] private string _recipeResourcePath = "Recipes/chahan";

        [Header("パネル（Kitchen.unity で配置済みのものを挿す）")]
        [SerializeField] private RecipePanel _recipePanel;
        [SerializeField] private IngredientsPanel _ingredientsPanel;
        [SerializeField] private TimerPanel _timerPanel;
        [SerializeField] private CookingModeInputGate _cookingModeInputGate;

        [Header("初期配置（設計 §9: 頭の前0.8m・目線より少し下に3枚）")]
        [SerializeField] private Transform _headTransform; // 未指定なら Camera.main を使う
        [SerializeField] private float _forwardDistanceMeters = 0.8f;
        [SerializeField] private float _belowEyelineMeters = 0.08f;
        [SerializeField] private float _lateralSpacingMeters = 0.5f;

        // P0/P1 では Null 実装しか挿さない。ArFoundation 実装は P2（設計 §4.3）。
        private IAnchorStore _anchorStore;
        private IPassthroughControl _passthrough;
        private IHandInputPolicy _handInputPolicy;

        private RecipeStore _recipeStore;
        private CookSession _session;
        private CancellationTokenSource _prefetchCts;

        private void Awake()
        {
            _anchorStore = new InMemoryAnchorStore(); // P2 で ArAnchorStore に差し替える（設計 §4.3）。
            _passthrough = new NullPassthrough();
            _passthrough.Enable(); // MR テンプレートは既定でパススルー済みだが、状態としても明示しておく。
            _handInputPolicy = new DefaultHandInputPolicy();

            _recipeStore = RecipeStore.CreateDefault();
            var recipe = LoadRecipe(_recipeStore, _recipeResourcePath);
            _session = new CookSession(recipe);

            _recipePanel.Bind(_recipeStore, recipe.Id);
            _ingredientsPanel.BindRecipe(recipe);

            _recipePanel.NextRequested += HandleNext;
            _recipePanel.PrevRequested += HandlePrev;

            if (_cookingModeInputGate != null)
            {
                _cookingModeInputGate.Bind(_handInputPolicy);
            }

            RefreshAllPanels();
        }

        private void Start()
        {
            PlaceInitialPanels();

            // 通信があるうちに hero と全工程の画像を手元へ（オフライン前提。設計 §11 追補）。
            _prefetchCts = new CancellationTokenSource();
            _recipeStore.PrefetchAsync(_session.Recipe, _prefetchCts.Token).Forget();
        }

        private void OnDestroy()
        {
            _recipePanel.NextRequested -= HandleNext;
            _recipePanel.PrevRequested -= HandlePrev;

            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
        }

        /// <summary>
        /// 見本（Resources）も保管庫を通す: 初回だけ Resources の中身を写し、読むのは常にローカル。
        /// P3 でサーバから取ってくるようになっても、表示側の経路はここのままで変わらない。
        /// </summary>
        private static Recipe LoadRecipe(RecipeStore store, string resourcePath)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                throw new System.InvalidOperationException(
                    $"見本レシピが見つかりません: Resources/{resourcePath}.json");
            }

            // 契約 JSON の id が保管庫の置き場になるので、まずは中身から id を読む。
            var bundled = RecipeJson.Parse(textAsset.text);
            return store.LoadRecipe(bundled.Id, textAsset.text);
        }

        private void HandleNext()
        {
            _session.Apply(SessionEvent.NextRequested.Instance);
            RefreshAllPanels();
        }

        private void HandlePrev()
        {
            _session.Apply(SessionEvent.PrevRequested.Instance);
            RefreshAllPanels();
        }

        private void RefreshAllPanels()
        {
            _recipePanel.Refresh(_session);
            _ingredientsPanel.Refresh(_session);
            _timerPanel.Refresh(_session);
        }

        /// <summary>
        /// 起動時に頭の前 0.8m・目線より少し下へ3枚を配る（設計 §9）。
        /// アンカーへの保存・復元は P2（<see cref="_anchorStore"/> は今は InMemory）。
        /// </summary>
        private void PlaceInitialPanels()
        {
            var head = _headTransform != null ? _headTransform : Camera.main != null ? Camera.main.transform : null;
            if (head == null)
            {
                Debug.LogWarning("[KitchenXR] 頭のTransformが見つからないため、初期配置をスキップしました。");
                return;
            }

            var flatForward = head.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 1e-6f)
            {
                flatForward = Vector3.forward;
            }

            flatForward.Normalize();

            // パネルの「表」は local -Z（XRI World Space UI サンプルの向きに合わせた。設計 Platform 外の判断・README 参照）。
            var rotation = Quaternion.LookRotation(flatForward, Vector3.up);
            var right = rotation * Vector3.right;

            var basePosition = head.position + flatForward * _forwardDistanceMeters + Vector3.down * _belowEyelineMeters;

            PlacePanel(_recipePanel != null ? _recipePanel.transform : null, basePosition, rotation);
            PlacePanel(_ingredientsPanel != null ? _ingredientsPanel.transform : null,
                basePosition - right * _lateralSpacingMeters, rotation);
            PlacePanel(_timerPanel != null ? _timerPanel.transform : null,
                basePosition + right * _lateralSpacingMeters, rotation);
        }

        private static void PlacePanel(Transform panel, Vector3 position, Quaternion rotation)
        {
            if (panel == null)
            {
                return;
            }

            panel.SetPositionAndRotation(position, rotation);
        }
    }
}
