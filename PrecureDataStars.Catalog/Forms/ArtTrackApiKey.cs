using System.Configuration;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 配信音源（アートトラック）の取り込みで使う YouTube Data API キーを App.config から読む窓口。
/// キーは PC ごとの設定なのでリポジトリには含めず、未設定のまま機能を開いたときは
/// 例外ではなく案内メッセージで止める（設定漏れは運用上ふつうに起こるため）。
/// </summary>
internal static class ArtTrackApiKey
{
    /// <summary>App.config の <c>YouTube.ApiKey</c> を読む。 未設定・プレースホルダのままなら案内を出して <c>null</c> を返す。 呼び出し側は <c>null</c> のときそのまま処理を中断する。</summary>
    /// <param name="owner">メッセージボックスの親ウィンドウ。</param>
    public static string? TryGet(IWin32Window owner)
    {
        string? key = ConfigurationManager.AppSettings["YouTube.ApiKey"];

        // App.config.sample のプレースホルダをそのままコピーした状態も未設定として扱う。
        if (string.IsNullOrWhiteSpace(key) || key == "YOUR_YOUTUBE_DATA_API_KEY")
        {
            MessageBox.Show(owner,
                "App.config に YouTube.ApiKey が設定されていません。\r\n\r\n"
                + "Google Cloud コンソールで YouTube Data API v3 を有効化し、\r\n"
                + "API キーを発行して App.config の appSettings に設定してください。",
                "配信音源の取り込み", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return key;
    }
}
