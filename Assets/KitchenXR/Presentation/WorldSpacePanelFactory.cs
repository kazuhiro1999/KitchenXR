using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// ワールド空間の UI Toolkit 板を「指で押せる」状態に組み立てる唯一の場所。
    ///
    /// シーンを作る <c>KitchenSceneBuilder</c>（Editor）と、押せることを確かめる PlayMode 試験の
    /// 両方がここを通る。片方だけ直して片方が置いていかれる、を起こさないため。
    ///
    /// 構成は XRI 3.5.1 の World Space UI サンプル
    /// （<c>Assets/Samples/.../World Space UI/Prefabs/UI Toolkit Button With Event.prefab</c>）と
    /// manual <c>ui-world-space-ui-toolkit-support</c> に合わせてある:
    ///   - <see cref="UIDocument"/>（PanelSettings は Render Mode = World Space・
    ///     Collider Update Mode = Keep existing colliders）
    ///   - 板と**同じ矩形**の <see cref="BoxCollider"/>（isTrigger = false）
    ///   - <see cref="XRSimpleInteractable"/> ＋ <see cref="XRPokeFilter"/>（poke の受け口）
    ///
    /// 2026-09-12 の「ボタンが押せない」の原因はここの <see cref="UIDocument.pivot"/> だった。
    /// 既定のままだと板の原点が中央になり、コライダー（左上原点で置いている）と板の矩形が
    /// 半分ずれる。コライダーには当たるので Interactable のホバー（色と振動）は効くのに、
    /// その当たり点を板のローカル座標へ写すと文字の外に落ちるため、
    /// UI Toolkit 側は Button を1度も掴めない。サンプルの板が全て
    /// <see cref="Pivot.TopLeft"/> なのはこのため。
    /// </summary>
    public static class WorldSpacePanelFactory
    {
        /// <summary>板の Transform の縮尺。PanelSettings の Pixels Per Unit = 100 と合わせて 1 UI px ≒ 2mm。</summary>
        public const float PanelLocalScale = 0.2f;

        /// <summary>PanelSettings の Pixels Per Unit。UI px → ローカル単位の換算に使う。</summary>
        public const float PanelPixelsPerUnit = 100f;

        /// <summary>
        /// 板の当たり判定の奥行き（ローカル単位。1.2 ≒ 24cm）。**板の裏側にだけ**伸ばす。
        ///
        /// 2026-09-12 の「ボタンが押せない」の本当の原因がここ。
        /// XRPokeInteractor は指が当たり判定から出ると掴みを手放す（`ResetPointerState`）。
        /// 出られる余裕は「箱の半分の厚み ＋ 約16mm」しかないので、以前の 0.02（＝実寸 4mm）だと
        /// 板の面から **18mm** 奥へ入った時点で指が抜けてしまう。
        /// ホログラムの板を指で押せば普通はもっと深く突き抜けるので、
        /// PointerDown は出るのに PointerUp が板の外で起き、Button の clicked が発火しない
        /// （実機で「触ると色は変わり振動するのに反応しない」と見えていたのはこれ）。
        ///
        /// 箱を裏へ伸ばすと、表の面（＝押し込みの判定位置）は板のままで、
        /// 突き抜けてよい深さだけが伸びる。24cm あれば腕ごと通しても取りこぼさない
        /// （PlayMode 試験 PokeButtonInteractionTests で確かめている）。
        /// </summary>
        public const float PanelColliderDepth = 1.2f;

        /// <summary>
        /// 板の原点。XRI のサンプルの板は全てこれ。コライダーの中心を
        /// <c>(+w/2, -h/2)</c> に置いているのと対になっている（原点が左上なので板は右下へ伸びる）。
        /// </summary>
        public const Pivot PanelPivot = Pivot.TopLeft;

        /// <summary>UI px の大きさ（<paramref name="widthUnits"/>）からコライダーのローカル寸法へ。</summary>
        public static Vector2 ColliderSizeFor(float widthUnits, float heightUnits) =>
            new Vector2(widthUnits / PanelPixelsPerUnit, heightUnits / PanelPixelsPerUnit);

        /// <summary>
        /// <see cref="PanelPivot"/> に対応するコライダーの中心。
        /// x/y は板の原点が左上なので右下へ半分ずらす。z は箱を裏側にだけ伸ばすための半分。
        /// </summary>
        public static Vector3 ColliderCenterFor(float widthUnits, float heightUnits)
        {
            var size = ColliderSizeFor(widthUnits, heightUnits);
            return new Vector3(size.x / 2f, -size.y / 2f, PanelColliderDepth / 2f);
        }

        /// <summary>
        /// 既にある <paramref name="go"/> をワールド空間の押せる板に仕立てる。
        /// Transform の位置と向きは呼び出し側の責任（ここでは縮尺だけ合わせる）。
        /// </summary>
        /// <param name="interactionLayers">
        /// 板が名乗る XRI の Interaction Layer。既定（null）は Default の1枚だけで、
        /// 今までどおり「指で押せる・配置モードで掴める」板になる。
        /// 動画の板だけは Default ＋ Video を名乗る——調理中も Ray で操作させるため
        /// （設計 §11 追補 2026-09-13。<see cref="CookingModeInputGate"/> に理由を書いた）。
        /// </param>
        public static UIDocument Configure(
            GameObject go, PanelSettings panelSettings, VisualTreeAsset uxml,
            float widthUnits, float heightUnits, int? interactionLayers = null)
        {
            go.transform.localScale = new Vector3(PanelLocalScale, PanelLocalScale, PanelLocalScale);

            var uiDocument = go.GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = go.AddComponent<UIDocument>();
            }

            uiDocument.panelSettings = panelSettings;
            uiDocument.visualTreeAsset = uxml;
            uiDocument.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            uiDocument.worldSpaceSize = new Vector2(widthUnits, heightUnits);

            // ここが 2026-09-12 の不具合の核心。既定（中央）のままだとコライダーと板が半分ずれる。
            uiDocument.pivot = PanelPivot;

            var size = ColliderSizeFor(widthUnits, heightUnits);
            var collider = go.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = go.AddComponent<BoxCollider>();
            }

            collider.center = ColliderCenterFor(widthUnits, heightUnits);
            collider.size = new Vector3(size.x, size.y, PanelColliderDepth);

            // isTrigger を立てると XRSimpleInteractable のコライダー一覧から外れて poke が当たらない
            // （XRI manual ui-world-space-ui-toolkit-support「Panel Settings」節）。
            collider.isTrigger = false;

            var interactable = go.GetComponent<XRSimpleInteractable>();
            if (interactable == null)
            {
                interactable = go.AddComponent<XRSimpleInteractable>();
            }

            // 明示的に挿す。空のままだと子（UI Document が作るコライダー等）を拾ってしまう。
            if (!interactable.colliders.Contains(collider))
            {
                interactable.colliders.Clear();
                interactable.colliders.Add(collider);
            }

            if (interactionLayers.HasValue)
            {
                interactable.interactionLayers = interactionLayers.Value;
            }

            var pokeFilter = go.GetComponent<XRPokeFilter>();
            if (pokeFilter == null)
            {
                pokeFilter = go.AddComponent<XRPokeFilter>();
            }

            pokeFilter.pokeInteractable = interactable;
            pokeFilter.pokeCollider = collider;

            return uiDocument;
        }

        /// <summary>
        /// できあがった板の寸法だけを変える（動画の板の 16:9 ⇄ 9:16。設計 §6）。
        /// 板の矩形と当たり判定は**必ず一緒に**動かす——片方だけ変えると
        /// 2026-09-12 の「触っているのに押せない」がそのまま戻る。
        /// </summary>
        public static void Resize(GameObject go, UIDocument uiDocument, float widthUnits, float heightUnits)
        {
            if (go == null || uiDocument == null)
            {
                return;
            }

            uiDocument.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            uiDocument.worldSpaceSize = new Vector2(widthUnits, heightUnits);
            uiDocument.pivot = PanelPivot;

            var collider = go.GetComponent<BoxCollider>();
            if (collider == null)
            {
                return;
            }

            var size = ColliderSizeFor(widthUnits, heightUnits);
            collider.center = ColliderCenterFor(widthUnits, heightUnits);
            collider.size = new Vector3(size.x, size.y, PanelColliderDepth);
        }
    }
}
