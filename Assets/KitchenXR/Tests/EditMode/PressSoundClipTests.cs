using KitchenXR.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace KitchenXR.Tests.EditMode
{
    /// <summary>効果音のクリップが Resources に在ること（無いと実機で黙って鳴らない）。</summary>
    public sealed class PressSoundClipTests
    {
        [Test]
        public void 効果音のクリップがResourcesにある()
        {
            var clip = Resources.Load<AudioClip>(PressSound.ClipResourcePath);
            Assert.IsNotNull(clip, $"Resources/{PressSound.ClipResourcePath} が読めません。");
            Assert.Greater(clip.length, 0.02f, "短すぎて聞こえません。");
            Assert.Less(clip.length, 0.3f, "長すぎます（連打で重なる）。");
        }
    }
}
