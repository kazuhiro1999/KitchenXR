using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace KitchenXR.App
{
    /// <summary>
    /// 実機のログを端末内のファイルに残す（2026-09-13 主人「実機ログが確認できるようにローカルに
    /// ログを残すようにできませんか」）。
    ///
    /// Unity のログ（<see cref="Application.logMessageReceivedThreaded"/>）を全部
    /// <c>persistentDataPath/logs/kitchenxr.log</c> へ追記する。Quest では
    /// <c>/sdcard/Android/data/com.kazuhiro.kitchenxr/files/logs/kitchenxr.log</c> で、
    /// <c>adb pull</c> でも MQDH のファイル一覧でも取れる。USB で繋げるときは
    /// <c>adb logcat -s Unity</c> の方が早いが、繋がっていない実機確認のあとで「何が起きたか」を
    /// 見返すにはファイルが要る。
    ///
    /// 1MB を越えたら <c>kitchenxr.1.log</c>・<c>kitchenxr.2.log</c> へ送って、3世代だけ残す
    /// （一晩の調理で数百 KB。溜め続けると端末の容量を食う）。書き込みは毎行 flush する
    /// ——落ちる直前の行こそ読みたい。
    ///
    /// <see cref="PressSound"/> と同じく、起動時に自分で1つ立つ（シーンの組み立てに手を入れない）。
    /// </summary>
    public sealed class FileLog : MonoBehaviour
    {
        public const string DirectoryName = "logs";
        public const string FileName = "kitchenxr.log";
        public const long RotateBytes = 1024 * 1024;
        public const int Generations = 3;

        private static readonly object Gate = new object();
        private static StreamWriter _writer;
        private static string _path;

        public static string Path => _path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<FileLog>() != null)
            {
                return;
            }

            var go = new GameObject("FileLog");
            DontDestroyOnLoad(go);
            go.AddComponent<FileLog>();
        }

        private void Awake()
        {
            try
            {
                var directory = System.IO.Path.Combine(Application.persistentDataPath, DirectoryName);
                Directory.CreateDirectory(directory);
                _path = System.IO.Path.Combine(directory, FileName);
                Rotate(directory);
                _writer = new StreamWriter(_path, true, new UTF8Encoding(false)) { AutoFlush = true };
                _writer.WriteLine(
                    $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 起動 v{Application.version} {SystemInfo.deviceModel} =====");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KitchenXR] ログのファイルを開けませんでした: {e.Message}");
                _writer = null;
            }

            Application.logMessageReceivedThreaded += Handle;
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= Handle;
            lock (Gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        /// <summary>1MB を越えていたら世代を1つずつ後ろへずらす（一番古いものは消える）。</summary>
        private static void Rotate(string directory)
        {
            var current = System.IO.Path.Combine(directory, FileName);
            if (!File.Exists(current) || new FileInfo(current).Length < RotateBytes)
            {
                return;
            }

            for (var i = Generations - 1; i >= 1; i--)
            {
                var older = Generation(directory, i);
                var newer = i == 1 ? current : Generation(directory, i - 1);
                if (File.Exists(older))
                {
                    File.Delete(older);
                }

                if (File.Exists(newer))
                {
                    File.Move(newer, older);
                }
            }
        }

        private static string Generation(string directory, int index) =>
            System.IO.Path.Combine(directory, $"kitchenxr.{index}.log");

        private static void Handle(string condition, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                if (_writer == null)
                {
                    return;
                }

                try
                {
                    var mark = type == LogType.Error || type == LogType.Exception ? "E" :
                        type == LogType.Warning ? "W" : "I";
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {mark} {condition}");
                    if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
                    {
                        _writer.WriteLine(stackTrace.TrimEnd());
                    }
                }
                catch (Exception)
                {
                    // 書けなくても落とさない（ログのために本体を止めない）。
                }
            }
        }
    }
}
