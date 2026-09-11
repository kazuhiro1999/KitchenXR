using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TLab.Android.WebView;
using System.Linq;
using UnityEngine.Events;
using Cysharp.Threading.Tasks;
using System;

public class YoutubePlayer : MonoBehaviour
{
    [SerializeField] TLabWebView m_webView;
    [SerializeField] TextAsset Html;

    private readonly float[] supportedPlaybackRates = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };

    public bool Initialized { get; private set; }
    public enum PlayerState
    {
        UNSTARTED = -1,
        ENDED = 0,
        PLAYING = 1,
        PAUSED = 2,
        BUFFERING = 3,
        CUED = 5
    }

    public PlayerState State { get; private set; }
    public bool loaded { get; private set; }
    public string videoId { get; private set; }
    public bool isPlaying { get; private set; }
    public float time { get; private set; }
    public float volume { get; private set; }
    public float playbackRate { get; private set; }
    public float duration { get; private set; }
    public bool loop { get; set; }

    // 範囲指定のループ再生用
    public float loopStartTime { get; set; }
    public float loopEndTime { get; set; }

    public UnityEvent WhenLoaded { get; } = new UnityEvent();
    public UnityEvent WhenPlayed { get; } = new UnityEvent();
    public UnityEvent WhenPaused { get; } = new UnityEvent();
    public UnityEvent<float> WhenVolumeChanged { get; } = new UnityEvent<float>();
    public UnityEvent<float> WhenPlaybackRateChanged { get; } = new UnityEvent<float>();

    private void Start() {
        Initialized = false;
        loopStartTime = 0;
        loopEndTime = -1;

        m_webView.Init();
        StartCoroutine(Init());
    }

    void Update() {
        m_webView.EvaluateJS("getCurrentTime();");

#if UNITY_ANDROID
        m_webView.UpdateFrame();
#endif
    }

    private IEnumerator Init() {
        if (Initialized) yield break;

        // WebViewが初期化されるまで待機
        yield return new WaitUntil(() => m_webView.state == TLabWebView.State.INITIALIZED);

        // youtube用のHTMLを読み込む
        LoadHtml(Html.text);
        Initialized = true;
    }

    public void LoadHtml(string html) {
        string baseUrl = "http://localhost";
        Debug.Log("html loaded.");
        m_webView.LoadHTML(html, baseUrl);
    }

    public async UniTask<bool> Load(string videoId, bool autoplay = true) {
        return await LoadVideo(videoId, autoplay).Timeout(TimeSpan.FromMilliseconds(5000));
    }

    public async UniTask<bool> LoadVideo(string videoId, bool autoplay = true) {
        if (videoId == this.videoId) {
            Debug.Log("YoutubePlayer: same video. return.");
            return true;
        }

        loaded = false;
        isPlaying = false;
        if (autoplay) {
            m_webView.EvaluateJS($"loadVideo('{videoId}');");
        }
        else {
            m_webView.EvaluateJS($"cueVideo('{videoId}');");
        }
        Debug.Log($"YoutubePlayer: send load {videoId}.");

        try {
            // タイムアウトを設定
            await UniTask.WaitUntil(() => loaded).Timeout(TimeSpan.FromSeconds(10f));

            this.videoId = videoId;
            return true;
        }
        catch (TimeoutException) {
            Debug.LogError($"YoutubePlayer: Timed out loading videoId: {videoId}");
            return false;
        }
    }

    public void Play() {
        if (isPlaying) return;
        
        m_webView.EvaluateJS("play();");
        Debug.Log("send play.");
        isPlaying = true;
    }

    public void Pause() {
        if (!isPlaying) return;

        m_webView.EvaluateJS("pause();");
        Debug.Log("send pause.");
        isPlaying = false;
    }

    public void SeekTo(float time) {
        m_webView.EvaluateJS($"seekTo({time});");
        Debug.Log("send seek.");
    }

    public void SetPlaybackRate(float speed) {
        if (!supportedPlaybackRates.Contains(speed)) {
            Debug.LogError($"playback rate {speed} is not supported.");
            return;
        }
        m_webView.EvaluateJS($"setPlaybackRate({speed});");
        Debug.Log($"send rate: {speed}");
    }

    public void SetVolume(float volume) {
        int clippedVolume = Mathf.Clamp(Mathf.RoundToInt(volume), 0, 100);
        m_webView.EvaluateJS($"setVolume({clippedVolume});");
        Debug.Log("send volume.");
    }

    public void SetLoopRange(float startTime, float endTime) {
        if (startTime < 0 || (endTime != -1 && endTime <= startTime)) {
            Debug.LogError("Invalid loop range");
            return;
        }

        loopStartTime = startTime;
        loopEndTime = endTime;
    }

    public void OnLoaded(string msg) {
        duration = float.Parse(msg);
        WhenLoaded?.Invoke();
    }

    public void OnTimeUpdated(string msg) {
        var currentTime = float.Parse(msg);
        time = currentTime;

        // とりあえずここにループ処理（再生中のみ）
        if (isPlaying && loop) {
            if (loopEndTime != -1 && time >= loopEndTime) {
                SeekTo(loopStartTime);
            }
            else if (loopEndTime == -1 && time >= duration - 1f) {
                SeekTo(0);
            }
        }
    }

    public void OnVolumeChanged(string msg) {
        var currentVolume = float.Parse(msg);
        volume = currentVolume;
        WhenVolumeChanged?.Invoke(volume);
    }

    public void OnPlaybackRateChanged(string msg) {
        var currentRate = float.Parse(msg);
        playbackRate = currentRate;
        WhenPlaybackRateChanged?.Invoke(playbackRate);
    }

    public void OnPlayerStateChange(string msg) {
        Debug.Log($"Player State Changed: {msg}");

        if (int.TryParse(msg, out var number)) {
            this.State = (PlayerState)number;
        }

        switch (msg) {
            case "-1": { loaded = false; break; }
            case "1": { isPlaying = true; WhenPlayed?.Invoke(); if (!loaded) { loaded = true; m_webView.EvaluateJS("getDuration();"); } break; }
            case "2": { isPlaying = false; WhenPaused?.Invoke(); break; }
            case "3": { break; }
            case "5": { if (!loaded) { loaded = true; m_webView.EvaluateJS("getDuration();"); } break; }
            default: { break; }
        }
    }

    public void OnJsEvent(string msg) {
        Debug.Log($"Received JS Event: {msg}");
    }

    public void OnPlayerError(string msg) {
        Debug.LogError($"Player Error: {msg}");
    }
}
