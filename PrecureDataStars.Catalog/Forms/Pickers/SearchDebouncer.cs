using System;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// テキストボックスの逐次入力を一定時間まとめて単一のコールバックに変換するデバウンサー
/// （v1.2.0 工程 C 追加）。
/// <para>
/// ピッカーダイアログの検索ボックスでキーストロークごとに DB 検索が走らないように、
/// <see cref="Trigger"/> が呼ばれてから <c>delayMs</c> ミリ秒間 静止していたら 1 度だけ
/// コールバックを発火する。新たな <see cref="Trigger"/> 呼び出しが入ったらタイマーは
/// リスタートされる。
/// </para>
/// <para>
/// WinForms の UI スレッド前提で <see cref="Timer"/> を内部利用する。コールバックも
/// UI スレッドで実行されるため、UI の更新は同期的に行ってよい。
/// </para>
/// </summary>
public sealed class SearchDebouncer : IDisposable
{
    private readonly Timer _timer;
    private readonly Action _callback;

    /// <summary>
    /// 新しいインスタンスを生成する。
    /// </summary>
    /// <param name="delayMs">最後の <see cref="Trigger"/> から発火までの待機時間（ミリ秒）。</param>
    /// <param name="callback">発火時に実行する処理（UI スレッド）。</param>
    public SearchDebouncer(int delayMs, Action callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _timer = new Timer { Interval = Math.Max(1, delayMs) };
        _timer.Tick += OnTick;
    }

    /// <summary>テキスト変更等のたびに呼ぶ。タイマーをリセットして発火を遅延させる。</summary>
    public void Trigger()
    {
        // Stop() してから Start() することで Interval を再カウントさせる
        _timer.Stop();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _callback();
    }

    /// <summary>内部 <see cref="Timer"/> を解放する。</summary>
    public void Dispose()
    {
        _timer.Tick -= OnTick;
        _timer.Dispose();
    }
}
