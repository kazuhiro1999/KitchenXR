using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 「触れたら反応する」ボタンの配線。押し下げ（PointerDown）で発火し、押し上げは見ない。
    ///
    /// UI Toolkit の Button は PointerUp が同じ要素の上で起きたときだけ clicked を出すので、
    /// <see cref="Button.clicked"/> で受けると「表面をかすめてすぐ戻す」以外では反応しない——
    /// 押し込んだままだと PointerUp が来ず、深く突き抜けると指が当たり判定から出て
    /// XRPokeInteractor が <c>ResetPointerState</c> を呼び、PointerUp が板の外で起きる。
    /// 手応えの無いホログラムを調理中の手で押せば深く入るのが普通なので、押し上げは使えない。
    ///
    /// 二重発火の歯止めは2つ: <see cref="ClickDebounce"/> で同じ鍵の連打を 600ms 間引き、
    /// 一度発火したらその指が板の前面領域から離れるまで次を受けない（突っ込んだまま留まって
    /// 600ms 後に再発火する、を防ぐ）。<see cref="Button.clicked"/> は購読しない
    /// （浅く押して引くと二重発火になる）。
    /// </summary>
    public static class PokePress
    {
        /// <summary>
        /// どこかのボタン（行）が発火した。効果音（<see cref="PressSound"/>）が聞く。
        /// ホログラムには手応えが無いので、音が「押せた」の唯一の返事になる
        /// （振動は XRI の hover で既に出ている）。
        /// </summary>
        public static event Action Pressed;

        /// <summary>
        /// <paramref name="element"/> を「触れたら1回だけ <paramref name="action"/> を呼ぶ」状態にする。
        /// </summary>
        /// <param name="element">押される要素（Button・材料の行など）。</param>
        /// <param name="debounce">600ms の連打抑止。板ごとに1つ持つ。</param>
        /// <param name="key">連打抑止の鍵（ボタン名など）。</param>
        /// <param name="action">発火したときに呼ぶもの。</param>
        public static void Bind(VisualElement element, ClickDebounce debounce, string key, Action action)
        {
            if (element == null || action == null)
            {
                return;
            }

            // 「まだ指が離れていない」指の一覧。離れるまで次の押し下げを受けない。
            var held = new HashSet<int>();

            // TrickleDown で登録して、Button 自身の Clickable より先に受ける
            // （Clickable は PointerDown で StopPropagation するため）。
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!held.Add(evt.pointerId))
                {
                    return; // この指はまだ板から離れていない。
                }

                if (debounce != null && !debounce.TryAccept(key))
                {
                    return;
                }

                Pressed?.Invoke();
                action();
            }, TrickleDown.TrickleDown);

            element.RegisterCallback<PointerUpEvent>(evt => held.Remove(evt.pointerId), TrickleDown.TrickleDown);
            element.RegisterCallback<PointerLeaveEvent>(evt => held.Remove(evt.pointerId), TrickleDown.TrickleDown);
            element.RegisterCallback<PointerOutEvent>(evt => held.Remove(evt.pointerId), TrickleDown.TrickleDown);
            element.RegisterCallback<PointerCaptureOutEvent>(evt => held.Remove(evt.pointerId), TrickleDown.TrickleDown);
        }

        /// <summary>押し込みの見た目（USS の kitchen-button--pressed）。判定そのものには関わらない。</summary>
        public static void RegisterPressedVisual(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            element.RegisterCallback<PointerDownEvent>(_ => element.AddToClassList("kitchen-button--pressed"));
            element.RegisterCallback<PointerUpEvent>(_ => element.RemoveFromClassList("kitchen-button--pressed"));
            element.RegisterCallback<PointerLeaveEvent>(_ => element.RemoveFromClassList("kitchen-button--pressed"));
            element.RegisterCallback<PointerOutEvent>(_ => element.RemoveFromClassList("kitchen-button--pressed"));
            element.RegisterCallback<PointerCaptureOutEvent>(_ => element.RemoveFromClassList("kitchen-button--pressed"));
        }

        /// <summary>ボタン1つ分の配線（発火＋見た目）。</summary>
        public static void BindButton(VisualElement element, ClickDebounce debounce, string key, Action action)
        {
            Bind(element, debounce, key, action);
            RegisterPressedVisual(element);
        }
    }
}
