using System.Collections;
using KitchenXR.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KitchenXR.Tests.PlayMode
{
    /// <summary>効果音は起動時に自分で立ち、ボタンの発火（PokePress.Pressed）で鳴る。</summary>
    public sealed class PressSoundTests
    {
        [UnityTest]
        public IEnumerator 起動時に効果音が立ち発火で鳴る()
        {
            yield return null; // AfterSceneLoad の Install を待つ。

            var sound = Object.FindAnyObjectByType<PressSound>();
            Assert.IsNotNull(sound, "PressSound が立っていません。");
            Assert.IsTrue(sound.HasClip, "クリップが読めていません。");

            var before = sound.PlayedCount;
            var pressed = typeof(PokePress).GetField("Pressed",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var handler = (System.Action)pressed.GetValue(null);
            Assert.IsNotNull(handler, "PressSound が Pressed を聞いていません。");
            handler.Invoke();

            Assert.AreEqual(before + 1, sound.PlayedCount, "発火しても鳴っていません。");
        }
    }
}
