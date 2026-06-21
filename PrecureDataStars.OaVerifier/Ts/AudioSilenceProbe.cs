using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace PrecureDataStars.OaVerifier.Ts;

/// <summary>
/// TS の指定メディア時刻区間だけを音声デコードして「その窓の中に連続無音があるか」を判定する。
/// 30 秒の予告が中央の無音で 15+15 に割れていないかを、予告の中間部に絞って高速に確かめるためのもの。
/// 全尺デコードは重いので、対象区間（予告の中点 ± 数秒）だけを <c>:start-time</c>/<c>:stop-time</c> で
/// 切り出してデコードし、窓内の RMS から連続無音を探す。
///
/// <para>スレッド方針：<see cref="Probe"/> を背景スレッドで 1 回呼ぶ。映像を持たない解析専用
/// <see cref="MediaPlayer"/>（独立 <see cref="LibVLC"/>）で音声のみをデコードし、PCM を audio コールバックで
/// 受けて RMS を畳み込む。VideoView を持たないのでメイン再生のような UI スレッドデッドロックは起きない。
/// 終了は EndReached をイベントで受けてから呼び出しスレッド側で停止・破棄する。</para>
/// </summary>
internal sealed class AudioSilenceProbe
{
    // ── 解析パラメータ（実測で調整可能）──
    private const int BucketMs = 20;                     // エンベロープの時間分解能（1 バケット 20ms）
    private const double SilenceDbfs = -50.0;            // この値未満（RMS, dBFS）を無音とみなす
    private const uint SampleRate = 48000;               // 固定デコード形式（S16N / 48kHz / 2ch にリサンプル）
    private const uint Channels = 2;
    private const float DecodeRate = 8.0f;               // 音声のみ高速デコード倍率（窓が短いので十分速い）
    private static readonly double SilenceAmp = 32768.0 * Math.Pow(10.0, SilenceDbfs / 20.0); // 振幅換算閾値

    /// <summary>プローブ結果。<see cref="Decoded"/>=false はデコード不可（PCM 未取得）。</summary>
    public sealed record Result(bool Decoded, bool SilenceFound, long AtMsInWindow, int Buckets, int SilentBuckets);

    // ── 畳み込み状態（audio コールバックは単一スレッドから順次呼ばれる）──
    private readonly List<double> _sumSq = new();
    private readonly List<long> _cnt = new();
    private long _firstPtsUs = long.MinValue;
    private short[] _pcm = new short[8192];

    // コールバックデリゲートは GC で回収されないようフィールドで保持する。
    private MediaPlayer.LibVLCAudioPlayCb? _playCb;
    private MediaPlayer.LibVLCAudioPauseCb? _pauseCb;
    private MediaPlayer.LibVLCAudioResumeCb? _resumeCb;
    private MediaPlayer.LibVLCAudioFlushCb? _flushCb;
    private MediaPlayer.LibVLCAudioDrainCb? _drainCb;

    /// <summary>メディア時刻区間 [<paramref name="fromMs"/>, <paramref name="toMs"/>] だけを音声デコードし、
    /// 窓の両端から <paramref name="edgeMarginMs"/> を除いた範囲に連続 <paramref name="minSilenceMs"/> 以上の
    /// 無音があれば <see cref="Result.SilenceFound"/>=true で返す。<paramref name="program"/> にフルセグの
    /// program_number を渡すとワンセグでなくフルセグ音声を解析する。</summary>
    public Result Probe(string path, int? program, long fromMs, long toMs,
                        long edgeMarginMs, long minSilenceMs, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || toMs <= fromMs) return new Result(false, false, 0, 0, 0);

        // 解析専用の独立 LibVLC（メイン再生の LibVLC ライフサイクルと切り離す）。映像・字幕は無効化。
        using var libvlc = new LibVLC("--no-video", "--no-spu", "--quiet");
        using var media = new Media(libvlc, path, FromType.FromPath);
        media.AddOption(":no-video");
        media.AddOption(":no-spu");
        if (program is int prog) media.AddOption($":program={prog}");
        // 対象区間だけを切り出してデコード（秒指定）。stop-time で末尾に達すると EndReached が出る。
        media.AddOption($":start-time={fromMs / 1000.0}");
        media.AddOption($":stop-time={toMs / 1000.0}");

        using var mp = new MediaPlayer(libvlc) { EnableKeyInput = false, EnableMouseInput = false };

        // PCM を S16N / 48kHz / 2ch 固定で受ける。play コールバックで RMS を畳み込む。
        _playCb = OnAudioPlay;
        _pauseCb = static (_, _) => { };
        _resumeCb = static (_, _) => { };
        _flushCb = static (_, _) => { };
        _drainCb = static _ => { };
        mp.SetAudioCallbacks(_playCb, _pauseCb, _resumeCb, _flushCb, _drainCb);
        mp.SetAudioFormat("S16N", SampleRate, Channels);

        using var done = new ManualResetEventSlim(false);
        void OnEnd(object? s, EventArgs e) => done.Set();
        mp.EndReached += OnEnd;
        mp.EncounteredError += OnEnd;
        try
        {
            mp.Play();
            mp.SetRate(DecodeRate);
            var sw = Stopwatch.StartNew();
            while (!done.IsSet)
            {
                if (ct.IsCancellationRequested) break;
                if (sw.Elapsed > TimeSpan.FromSeconds(60)) break; // 窓は短いので 60s 上限で十分
                done.Wait(100);
            }
        }
        catch { return new Result(false, false, 0, 0, 0); }
        finally
        {
            mp.EndReached -= OnEnd;
            mp.EncounteredError -= OnEnd;
            try { mp.Stop(); } catch { }
        }

        if (ct.IsCancellationRequested) return new Result(false, false, 0, 0, 0);
        return Evaluate(edgeMarginMs, minSilenceMs);
    }

    /// <summary>（audio スレッド）1 バッファぶんの PCM を受け取り、窓先頭=0 起点のバケットごとに二乗和を畳み込む。</summary>
    private void OnAudioPlay(IntPtr data, IntPtr samples, uint count, long pts)
    {
        if (count == 0) return;
        if (_firstPtsUs == long.MinValue) _firstPtsUs = pts; // 窓先頭の pts を 0 起点に正規化

        int total = checked((int)(count * Channels));
        if (_pcm.Length < total) _pcm = new short[total];
        Marshal.Copy(samples, _pcm, 0, total);

        long baseMs = (pts - _firstPtsUs) / 1000;
        int ch = (int)Channels;
        for (int i = 0; i < count; i++)
        {
            long tMs = baseMs + (long)i * 1000 / SampleRate;
            int b = (int)(tMs / BucketMs);
            if (b < 0) continue;
            while (_sumSq.Count <= b) { _sumSq.Add(0); _cnt.Add(0); }
            int off = i * ch;
            double e = 0;
            for (int c = 0; c < ch; c++) { int v = _pcm[off + c]; e += (double)v * v; }
            _sumSq[b] += e;
            _cnt[b] += ch;
        }
    }

    /// <summary>畳み込み結果から、窓端マージンを除いた範囲の連続無音を判定する。</summary>
    private Result Evaluate(long edgeMarginMs, long minSilenceMs)
    {
        int n = _sumSq.Count;
        if (n == 0) return new Result(false, false, 0, 0, 0);

        var silent = new bool[n];
        int silentCount = 0;
        for (int b = 0; b < n; b++)
        {
            long cnt = _cnt[b];
            // サンプルの無いバケット（デコード未到達/ドロップ）は安全側で「非無音」とし、空振り警告を避ける。
            bool s = cnt > 0 && Math.Sqrt(_sumSq[b] / cnt) < SilenceAmp;
            silent[b] = s;
            if (s) silentCount++;
        }

        int edge = (int)(edgeMarginMs / BucketMs);
        int bs = Math.Min(n - 1, edge);
        int be = Math.Max(0, n - 1 - edge);
        int need = (int)Math.Ceiling(minSilenceMs / (double)BucketMs);
        int run = 0;
        long atMs = -1;
        for (int i = bs; i <= be; i++)
        {
            if (silent[i])
            {
                if (++run >= need) { atMs = (long)(i - run + 1) * BucketMs; break; }
            }
            else run = 0;
        }
        return new Result(true, atMs >= 0, atMs < 0 ? 0 : atMs, n, silentCount);
    }
}
