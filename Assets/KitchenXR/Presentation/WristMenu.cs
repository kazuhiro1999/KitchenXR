using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 手首の釦とメニュー（2026-09-13 主人の実機確認 v1.0.8 の③）。
    ///
    /// 主人の言葉:
    ///   「手のひらメニューはいい感じですが、手を横に向けているときにも表示されています。
    ///     手を洗ってるときなどに出ると邪魔なので、要改善。手首の手のひら側にボタンを作っておいて、
    ///     それを押すとパネルがトグルでも全然いいと思います」
    ///
    /// v1.0.8 までは XRI の <c>HandMenu</c>（`Runtime/UI/BodyUI/HandMenu.cs`）が
    /// **手のひらの向き**でメニューを出し入れしていた。判定は
    /// 「手のひらが自分を向いている」かつ「手のひらが上を向いている」の2つで、
    /// サンプルの設定（`Menu Hands Follow Preset`）の閾値は 75.7° と **95.7°**——
    /// 後者は水平より広いので**手を横に向けても成立してしまう**。
    /// 閾値を詰めても「洗い物の途中で偶然出る」は無くならない。
    /// 手の姿勢を合図にしている限り、料理中の手は必ずどこかで合図を出す（設計 §7 の原則
    /// 「料理中の手は UI 操作の意図ではない」）。
    ///
    /// そこで**合図をやめて釦にした**。左右の手首の手のひら側に 4.4cm 角の板を1枚ずつ
    /// 付けっぱなしにし、反対の手でそれを押したときだけメニューが出る。
    /// 手を洗っていても横を向いていても、出るものは何も無い。
    /// 左右どちらでも押せるのは <c>HandMenu</c> の既定（<c>MenuHandedness.Either</c>）と同じで、
    /// メニューは**最後に押した側の手**に付いて出る。
    ///
    /// **手のひらの Transform の取り方は XRI の <c>HandMenu</c> と同じ**——
    /// MR テンプレートの <c>Left Hand</c>／<c>Right Hand</c> の下にある <c>Palm</c>
    /// （<c>TrackedPoseDriver</c> が XR Hands の手のひらの姿勢で動かしている）を挿す。
    /// <c>XRInputModalityManager</c> は手を追えていないときに
    /// <c>Left Hand</c>／<c>Right Hand</c> ごと眠らせるので、
    /// <c>Palm</c> の <c>activeInHierarchy</c> が「今この手が見えているか」になる。
    ///
    /// **手のひらの軸**（XRI の `Menu Hands Follow Preset` が <c>palmReferenceAxis: 4</c>＝Down を
    /// 手のひらの向きとして使い、メニューを z:+0.1 へ押し出していることから読める）:
    ///   - 手のひらの面が向く先 ＝ <c>-palm.up</c>
    ///   - 指先の方 ＝ <c>+palm.forward</c>
    ///   - 手首の方 ＝ <c>-palm.forward</c>
    /// </summary>
    public sealed class WristMenu : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("左手の手のひらの Transform（MR テンプレートの Left Hand の下の Palm）。")]
        private Transform _leftPalmAnchor;

        [SerializeField]
        [Tooltip("右手の手のひらの Transform（MR テンプレートの Right Hand の下の Palm）。")]
        private Transform _rightPalmAnchor;

        [SerializeField]
        [Tooltip("左の手首に付けっぱなしの小さな板。")]
        private WristMenuButton _leftToggleButton;

        [SerializeField]
        [Tooltip("右の手首に付けっぱなしの小さな板。")]
        private WristMenuButton _rightToggleButton;

        [SerializeField]
        [Tooltip("出し入れするメニューの板（PlacementMenuPanel）。左右で共用する1枚。")]
        private GameObject _menuRoot;

        [SerializeField]
        [Tooltip("メニューが向く先。未指定なら Camera.main。")]
        private Transform _headTransform;

        /// <summary>
        /// 手のひらから見た手首の釦の置き場（手のひらのローカル）。
        /// z = -0.07 で指先と反対（＝手首）へ 7cm、y = -0.02 で手のひらの面から 2cm 浮かせる。
        /// 浮かせるのは、板の当たり判定（裏側へ 24cm の箱）が腕に埋まらないようにするため。
        /// </summary>
        [SerializeField]
        private Vector3 _buttonLocalOffset = new Vector3(0f, -0.02f, -0.07f);

        /// <summary>
        /// 手のひらから見たメニューの中心の置き場（手のひらのローカル）。
        /// 手のひらの面から 14cm 浮かせた「掌の上の空間」で、指先の方へ 10cm。
        /// 26cm×18.4cm の板なので、ここに置くと腕に被らず、反対の手で押せる距離に来る。
        ///
        /// **手首の釦から 17cm 離す**のが肝心（釦は z = -0.07、メニューは z = +0.10）。
        /// 近すぎると、釦を押しに来た指がメニューの当たり判定（板の裏へ 24cm の箱）に
        /// 先に触れて、釦が押せなくなる——PlayMode 試験
        /// 「手首の釦をポークするとメニューがトグルで出入りする」がこれを踏んだ。
        /// </summary>
        [SerializeField]
        private Vector3 _menuLocalOffset = new Vector3(0f, -0.14f, 0.10f);

        /// <summary>今メニューが出ているか（試験が見る）。</summary>
        public bool IsMenuOpen { get; private set; }

        /// <summary>メニューが付いている手のひら（最後に釦を押した側）。</summary>
        public Transform MenuAnchor { get; private set; }

        /// <summary>釦に渡した聞き手。外すときに要るので覚えておく。</summary>
        private System.Action _leftHandler;
        private System.Action _rightHandler;

        private void Awake()
        {
            SubscribeAll();

            // 起動時は閉じている（主人の指示「手を洗ってるときなどに出ると邪魔」）。
            SetMenuOpen(false);
        }

        private void OnDestroy() => UnsubscribeAll();

        /// <summary>シーンでの配線と等価（試験から使う）。</summary>
        public void Bind(
            Transform leftPalmAnchor, WristMenuButton leftToggleButton,
            Transform rightPalmAnchor, WristMenuButton rightToggleButton,
            GameObject menuRoot, Transform headTransform = null)
        {
            UnsubscribeAll();

            _leftPalmAnchor = leftPalmAnchor;
            _rightPalmAnchor = rightPalmAnchor;
            _leftToggleButton = leftToggleButton;
            _rightToggleButton = rightToggleButton;
            _menuRoot = menuRoot;
            _headTransform = headTransform;

            SubscribeAll();
            SetMenuOpen(false);
        }

        private void SubscribeAll()
        {
            if (_leftToggleButton != null)
            {
                _leftHandler = () => ToggleFrom(_leftPalmAnchor);
                _leftToggleButton.Toggled += _leftHandler;
            }

            if (_rightToggleButton != null)
            {
                _rightHandler = () => ToggleFrom(_rightPalmAnchor);
                _rightToggleButton.Toggled += _rightHandler;
            }
        }

        private void UnsubscribeAll()
        {
            if (_leftToggleButton != null && _leftHandler != null)
            {
                _leftToggleButton.Toggled -= _leftHandler;
            }

            if (_rightToggleButton != null && _rightHandler != null)
            {
                _rightToggleButton.Toggled -= _rightHandler;
            }

            _leftHandler = null;
            _rightHandler = null;
        }

        /// <summary>
        /// その手の釦が押された。出ていて**同じ手**なら閉じ、
        /// 閉じているか**反対の手**なら、その手にメニューを出し直す。
        /// </summary>
        public void ToggleFrom(Transform anchor)
        {
            if (IsMenuOpen && MenuAnchor == anchor)
            {
                SetMenuOpen(false);
                return;
            }

            MenuAnchor = anchor;
            SetMenuOpen(true);
        }

        /// <summary>
        /// メニューを出す（配置モードへ入るときに <c>Bootstrap</c> が呼ぶ）。
        /// 「配置」はレシピ／一覧の板の頭からも押せるので、そのときメニューが閉じたままだと
        /// 「保存」「やめる」に手が届かない＝配置モードから出られなくなる。
        ///
        /// **手が1つも追えていなければ出せない**（false を返す）。
        /// この板は手に付いているので、手が無ければ置く場所が無い——
        /// コントローラだけで使っているときがこれに当たる。
        /// 呼び側（<c>Bootstrap</c>）はこのとき配置モードへ**入らない**
        /// （設計 §7「行き止まりを作らない」）。
        /// </summary>
        /// <returns>メニューが出ているか。</returns>
        public bool Open()
        {
            if (!IsTracked(MenuAnchor))
            {
                MenuAnchor = TrackedAnchor();
            }

            SetMenuOpen(MenuAnchor != null);
            return IsMenuOpen;
        }

        /// <summary>メニューを閉じる（「保存」「やめる」で配置を終えたら自動で閉じる）。</summary>
        public void Close() => SetMenuOpen(false);

        private Transform TrackedAnchor()
        {
            if (IsTracked(_rightPalmAnchor))
            {
                return _rightPalmAnchor;
            }

            return IsTracked(_leftPalmAnchor) ? _leftPalmAnchor : null;
        }

        private static bool IsTracked(Transform anchor) =>
            anchor != null && anchor.gameObject.activeInHierarchy;

        private void SetMenuOpen(bool open)
        {
            IsMenuOpen = open;

            if (_menuRoot == null)
            {
                return;
            }

            // 見えている間だけ立てる。PlacementMenuPanel は SetActive の出し入れを前提に
            // OnEnable で配線し直すので、ここは SetActive でよい（PanelVisibility は要らない）。
            if (_menuRoot.activeSelf != open)
            {
                _menuRoot.SetActive(open);
            }

            if (open)
            {
                PlaceMenu();
            }
        }

        /// <summary>
        /// 板を手のひらに追わせる。<c>LateUpdate</c> なのは、手の Transform を動かす
        /// <c>TrackedPoseDriver</c> より後に読むため（<c>Update</c> だと1フレーム遅れて震える）。
        /// </summary>
        private void LateUpdate()
        {
            PlaceButton(_leftToggleButton, _leftPalmAnchor);
            PlaceButton(_rightToggleButton, _rightPalmAnchor);

            if (!IsMenuOpen)
            {
                return;
            }

            // 付いている手を見失ったらメニューも引っ込める（宙に取り残さない）。
            if (!IsTracked(MenuAnchor))
            {
                SetMenuOpen(false);
                return;
            }

            PlaceMenu();
        }

        /// <summary>
        /// 手首の釦を手のひらの面と平行に置く。板の「表」は local -Z（設計 §9・Bootstrap と同じ）
        /// なので、+Z を手のひらの**裏**（<c>+palm.up</c>）へ向ければ表が手のひら側を向く。
        /// 板の上は指先の方（<c>+palm.forward</c>）——手を返して見たときに字が正しく立つ。
        ///
        /// 手が追えていなければ板ごと眠らせる（宙に置き去りにしない）。
        /// </summary>
        private void PlaceButton(WristMenuButton button, Transform anchor)
        {
            if (button == null)
            {
                return;
            }

            var tracked = IsTracked(anchor);
            if (button.gameObject.activeSelf != tracked)
            {
                button.gameObject.SetActive(tracked);
            }

            if (!tracked)
            {
                return;
            }

            var center = anchor.TransformPoint(_buttonLocalOffset);
            var rotation = Quaternion.LookRotation(anchor.up, anchor.forward);
            PlacePanelCentered(button.transform, center, rotation);
        }

        /// <summary>
        /// メニューは掌の上の空間に、**視線の側を向けて**出す。
        /// 手のひらと同じ向きにすると、手首をひねった角度がそのまま板の角度になって読みづらい。
        /// 板の表は local -Z なので、+Z を頭と反対へ向ける（＝ <c>LookRotation(menu - head)</c>）。
        /// </summary>
        private void PlaceMenu()
        {
            if (_menuRoot == null || !IsTracked(MenuAnchor))
            {
                return;
            }

            var center = MenuAnchor.TransformPoint(_menuLocalOffset);
            var head = _headTransform != null
                ? _headTransform
                : Camera.main != null ? Camera.main.transform : null;

            var rotation = MenuAnchor.rotation;
            if (head != null)
            {
                var away = center - head.position;
                if (away.sqrMagnitude > 1e-6f)
                {
                    rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                }
            }

            PlacePanelCentered(_menuRoot.transform, center, rotation);
        }

        /// <summary>
        /// 板の**中心**を <paramref name="center"/> に合わせる。
        /// 板の原点は左上（<see cref="WorldSpacePanelFactory.PanelPivot"/>）で右下へ伸びるので、
        /// 中心から左へ幅の半分・上へ高さの半分ずらした点が Transform の位置になる。
        /// 寸法は <see cref="UIDocument.worldSpaceSize"/>（UI px）から、板の縮尺込みで実寸へ直す。
        /// </summary>
        public static void PlacePanelCentered(Transform panel, Vector3 center, Quaternion rotation)
        {
            if (panel == null)
            {
                return;
            }

            var document = panel.GetComponent<UIDocument>();
            var size = document != null ? document.worldSpaceSize : Vector2.zero;
            var scale = panel.localScale.x;
            var width = size.x / WorldSpacePanelFactory.PanelPixelsPerUnit * scale;
            var height = size.y / WorldSpacePanelFactory.PanelPixelsPerUnit * scale;

            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;

            panel.SetPositionAndRotation(
                center - right * (width / 2f) + up * (height / 2f), rotation);
        }
    }
}
