using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// レイの線を「指す先があるときだけ」出す（2026-09-13 主人の実機確認 v1.0.8 の④）。
    ///
    /// 主人の言葉:
    ///   「レイが操作できない場合は表示を消してほしいですね。
    ///     Youtube パネルにレイが当たっているときだけ表示はできますか？」
    ///
    /// 調理中の視界に、手から 25cm の線（<c>CurveVisualController.restingVisualLineLength</c>）が
    /// 常に伸びているのが邪魔だという話。**線を消してもレイは生きている**——
    /// 層の切り替え（<see cref="CookingModeInputGate"/>）はそのままなので、
    /// 動画の板へ向ければ当たり、当たった瞬間に線が出る。
    ///
    /// **XRI 3.5 に「無効なときは隠す」の設定は無い。**
    ///   - <c>CurveVisualController</c>（Near-Far Interactor が使うのはこちら）は
    ///     <c>LineDynamicsMode.RetractOnHitLoss</c> で**短くする**ことはできても消せない。
    ///     さらに悪いことに、この component を無効にしただけでは
    ///     <c>OnDisable</c> が <c>LineRenderer</c> に触らないので、**最後の線が出たまま固まる**。
    ///     だから <c>LineRenderer</c> も一緒に落とす。
    ///   - <c>XRInteractorLineVisual</c>（古い口。将来 rig を差し替えたときのため）は
    ///     <c>OnDisable</c> が自分で <c>LineRenderer</c> を落とすが、両方落として困ることは無い。
    ///
    /// 「指す先がある」の見方は <see cref="XRBaseInteractor.hasHover"/> ／
    /// <see cref="XRBaseInteractor.hasSelection"/>。ここは XRI の層
    /// （<c>interactionLayers</c>）を既に通った後なので、
    /// **調理モードでは動画の板だけ・配置モードでは全部の板**が自然にそうなる——
    /// モードの分岐をここに書かなくてよい。
    ///
    /// XRI は機種非依存のツールキットそのものなので、この配線は <c>Platform/</c> に
    /// 閉じ込める対象ではない（設計 §4.2。<see cref="CookingModeInputGate"/> と同じ理由）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RayLineVisibility : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("線の持ち主（Near-Far Interactor 等）。空なら同じ GameObject から拾う。")]
        private XRBaseInteractor _interactor;

        [SerializeField]
        [Tooltip("線を描く仕掛け（CurveVisualController か XRInteractorLineVisual）。空なら同じ GameObject から拾う。")]
        private Behaviour _lineVisual;

        [SerializeField]
        [Tooltip("線そのもの。CurveVisualController は無効化しても線を消さないので、ここも落とす。")]
        private LineRenderer _lineRenderer;

        /// <summary>今 線が出ているか（試験が見る）。</summary>
        public bool IsLineShown { get; private set; } = true;

        /// <summary>配線を差し替える（シーンでの挿し込みと等価。試験から使う）。</summary>
        public void Bind(XRBaseInteractor interactor, Behaviour lineVisual, LineRenderer lineRenderer)
        {
            _interactor = interactor;
            _lineVisual = lineVisual;
            _lineRenderer = lineRenderer;
            Apply(ShouldShow(), force: true);
        }

        private void Awake() => ResolveMissing();

        private void OnEnable()
        {
            ResolveMissing();
            Apply(ShouldShow(), force: true);
        }

        /// <summary>
        /// 自分が止められたら線は**出したまま**返す。XRI の既定の振る舞いへ戻すのが筋で、
        /// 「線を消す仕掛けを外したのに線が消えたままになる」を作らないため。
        /// </summary>
        private void OnDisable() => Apply(true, force: true);

        /// <summary>
        /// <c>CurveVisualController</c> は <c>Application.onBeforeRender</c> で線を引き直すので、
        /// その前（LateUpdate）に決めておけば、消した次のフレームに一瞬出る、が起きない。
        /// </summary>
        private void LateUpdate() => Apply(ShouldShow(), force: false);

        private void ResolveMissing()
        {
            if (_interactor == null)
            {
                _interactor = GetComponent<XRBaseInteractor>();
            }

            if (_lineRenderer == null)
            {
                _lineRenderer = FindLineRenderer(gameObject);
            }

            if (_lineVisual == null)
            {
                _lineVisual = FindLineVisual(gameObject);
            }
        }

        /// <summary>
        /// 線を描いている component を名前で**子まで見て**拾う。
        ///
        /// 子まで見るのが肝心——XRI の `Left_NearFarInteractor.prefab` では
        /// <c>CurveVisualController</c> も <c>LineRenderer</c> も
        /// Interactor 本体ではなく **`LineVisual` という子**に載っている。
        /// 同じ GameObject だけを見ると黙って何も見つからず、線は出っぱなしのままになる。
        ///
        /// 型で書かない理由は2つ——<c>CurveVisualController</c> と
        /// <c>XRInteractorLineVisual</c> に共通の親が無いことと、rig を差し替えたときに
        /// 別の仕掛けが入っていても壊れないようにするため
        /// （拾えなければ <see cref="_lineRenderer"/> だけで済ませる）。
        /// </summary>
        public static Behaviour FindLineVisual(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            foreach (var behaviour in go.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }

                var name = behaviour.GetType().Name;
                if (name == "CurveVisualController" || name == "XRInteractorLineVisual")
                {
                    return behaviour;
                }
            }

            return null;
        }

        /// <summary>線そのもの。こちらも子まで見る（上と同じ理由）。</summary>
        public static LineRenderer FindLineRenderer(GameObject go) =>
            go != null ? go.GetComponentInChildren<LineRenderer>(true) : null;

        private bool ShouldShow()
        {
            if (_interactor == null || !_interactor.isActiveAndEnabled)
            {
                return false;
            }

            // 掴んでいる間も出す——遠くの板を引き寄せている最中に線が消えると、
            // どこを掴んでいるのか分からなくなる（配置モード）。
            return _interactor.hasHover || _interactor.hasSelection;
        }

        private void Apply(bool show, bool force)
        {
            if (!force && show == IsLineShown)
            {
                return;
            }

            IsLineShown = show;

            if (show)
            {
                // 線を先に起こしてから描き手を起こす（描き手が毎フレーム面倒を見る）。
                if (_lineRenderer != null)
                {
                    _lineRenderer.enabled = true;
                }

                if (_lineVisual != null)
                {
                    _lineVisual.enabled = true;
                }

                return;
            }

            // 消すときは逆。描き手を止めてからでないと、同じフレームに描き直される。
            if (_lineVisual != null)
            {
                _lineVisual.enabled = false;
            }

            if (_lineRenderer != null)
            {
                _lineRenderer.enabled = false;
            }
        }
    }
}
