#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Controls;

/// <summary>
/// プリキュアの肌色を HSL（H 0-360 / S 0-100 / L 0-100）と RGB（R/G/B 0-255）の
/// 両方で入力・確認できる UserControl（v1.2.4 新設）。
/// <para>
/// 運用安定までは「HSL から復元した色」「RGB から復元した色」を並べて表示し、
/// 両者が同じ色（誤差の範囲内）かどうかを CIE76 ΔE で評価して
/// 「許容（緑）／要確認（黄）／不一致（赤）」のバッジで通知する。
/// 入力欄は 6 つすべて NumericUpDown。値変更のたびにプレビューと ΔE ラベルが
/// 即時更新される。
/// </para>
/// <para>
/// 用途上「両方とも未設定」のケースが多いため、各入力欄は「NULL チェック」付き：
/// チェック ON で NumericUpDown が無効化され、保存時に NULL として扱われる。
/// HSL 群と RGB 群はそれぞれ「3 つ揃っているか NULL のみ」のいずれかに揃う設計
/// （部分入力でも保存はできるが、プレビューと ΔE は 3 つ揃った側のみ評価する）。
/// </para>
/// </summary>
public sealed class SkinColorPickerControl : UserControl
{
    // ── HSL 入力 ──
    private readonly NumericUpDown _numH = new();
    private readonly NumericUpDown _numS = new();
    private readonly NumericUpDown _numL = new();
    private readonly CheckBox _chkHslNull = new();
    // ── RGB 入力 ──
    private readonly NumericUpDown _numR = new();
    private readonly NumericUpDown _numG = new();
    private readonly NumericUpDown _numB = new();
    private readonly CheckBox _chkRgbNull = new();
    // ── プレビューと ΔE バッジ ──
    private readonly Panel _panelHslPreview = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly Panel _panelRgbPreview = new() { BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _lblDeltaE = new();
    private readonly Label _lblBadge = new();

    /// <summary>外部からのプログラム的更新中はプレビュー更新を抑止するフラグ。</summary>
    private bool _suppressEvents;

    public SkinColorPickerControl()
    {
        InitializeUi();
        WireEvents();
        UpdatePreview();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 値の出し入れ（外部 API）
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>HSL 値をまとめて取得する。3 つすべてが入っているときのみ非 NULL。</summary>
    public (ushort? H, byte? S, byte? L) GetHsl()
        => _chkHslNull.Checked
            ? (null, null, null)
            : ((ushort)_numH.Value, (byte)_numS.Value, (byte)_numL.Value);

    /// <summary>RGB 値をまとめて取得する。3 つすべてが入っているときのみ非 NULL。</summary>
    public (byte? R, byte? G, byte? B) GetRgb()
        => _chkRgbNull.Checked
            ? (null, null, null)
            : ((byte)_numR.Value, (byte)_numG.Value, (byte)_numB.Value);

    /// <summary>HSL 値を設定する。NULL のいずれかが含まれていれば NULL 扱い。</summary>
    public void SetHsl(ushort? h, byte? s, byte? l)
    {
        _suppressEvents = true;
        try
        {
            bool isNull = !h.HasValue || !s.HasValue || !l.HasValue;
            _chkHslNull.Checked = isNull;
            if (!isNull)
            {
                _numH.Value = Clamp(h!.Value, 0, 360);
                _numS.Value = Clamp(s!.Value, 0, 100);
                _numL.Value = Clamp(l!.Value, 0, 100);
            }
            UpdateHslEnabled();
        }
        finally { _suppressEvents = false; }
        UpdatePreview();
    }

    /// <summary>RGB 値を設定する。NULL のいずれかが含まれていれば NULL 扱い。</summary>
    public void SetRgb(byte? r, byte? g, byte? b)
    {
        _suppressEvents = true;
        try
        {
            bool isNull = !r.HasValue || !g.HasValue || !b.HasValue;
            _chkRgbNull.Checked = isNull;
            if (!isNull)
            {
                _numR.Value = r!.Value;
                _numG.Value = g!.Value;
                _numB.Value = b!.Value;
            }
            UpdateRgbEnabled();
        }
        finally { _suppressEvents = false; }
        UpdatePreview();
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI 構築
    // ─────────────────────────────────────────────────────────────────────

    private void InitializeUi()
    {
        // ボックス全体のサイズはレイアウトコンテナ側で制御されるよう、明示的な Size は持たない
        // （Anchor / Dock を呼び出し側で指定する流儀）。
        Size = new Size(560, 200);

        // ── HSL 行 ──
        var lblHslHeader = new Label
        {
            Text = "HSL",
            Location = new Point(8, 8),
            Size = new Size(40, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        Controls.Add(lblHslHeader);

        SetupNumeric(_numH, 0, 360, 0, new Point(60, 6));
        AddSmallLabel("H", new Point(50, 9));
        SetupNumeric(_numS, 0, 100, 0, new Point(140, 6));
        AddSmallLabel("S", new Point(130, 9));
        SetupNumeric(_numL, 0, 100, 0, new Point(220, 6));
        AddSmallLabel("L", new Point(210, 9));

        _chkHslNull.Text = "未設定";
        _chkHslNull.Location = new Point(294, 8);
        _chkHslNull.Size = new Size(70, 20);
        Controls.Add(_chkHslNull);

        // ── HSL プレビュー ──
        _panelHslPreview.Location = new Point(380, 4);
        _panelHslPreview.Size = new Size(60, 28);
        _panelHslPreview.BackColor = Color.LightGray;
        Controls.Add(_panelHslPreview);
        var lblHslPrev = new Label
        {
            Text = "HSL→",
            Location = new Point(444, 8),
            Size = new Size(40, 20),
            Font = new Font(Font, FontStyle.Italic),
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lblHslPrev);

        // ── RGB 行 ──
        var lblRgbHeader = new Label
        {
            Text = "RGB",
            Location = new Point(8, 40),
            Size = new Size(40, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        Controls.Add(lblRgbHeader);

        SetupNumeric(_numR, 0, 255, 0, new Point(60, 38));
        AddSmallLabel("R", new Point(50, 41));
        SetupNumeric(_numG, 0, 255, 0, new Point(140, 38));
        AddSmallLabel("G", new Point(130, 41));
        SetupNumeric(_numB, 0, 255, 0, new Point(220, 38));
        AddSmallLabel("B", new Point(210, 41));

        _chkRgbNull.Text = "未設定";
        _chkRgbNull.Location = new Point(294, 40);
        _chkRgbNull.Size = new Size(70, 20);
        Controls.Add(_chkRgbNull);

        // ── RGB プレビュー ──
        _panelRgbPreview.Location = new Point(380, 36);
        _panelRgbPreview.Size = new Size(60, 28);
        _panelRgbPreview.BackColor = Color.LightGray;
        Controls.Add(_panelRgbPreview);
        var lblRgbPrev = new Label
        {
            Text = "RGB→",
            Location = new Point(444, 40),
            Size = new Size(40, 20),
            Font = new Font(Font, FontStyle.Italic),
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lblRgbPrev);

        // ── ΔE ラベル + バッジ ──
        var lblDeltaECaption = new Label
        {
            Text = "色差 ΔE (CIE76):",
            Location = new Point(8, 80),
            Size = new Size(120, 20),
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lblDeltaECaption);

        _lblDeltaE.Location = new Point(132, 80);
        _lblDeltaE.Size = new Size(80, 20);
        _lblDeltaE.Text = "—";
        Controls.Add(_lblDeltaE);

        _lblBadge.Location = new Point(220, 78);
        _lblBadge.Size = new Size(160, 24);
        _lblBadge.TextAlign = ContentAlignment.MiddleCenter;
        _lblBadge.BorderStyle = BorderStyle.FixedSingle;
        _lblBadge.Text = "(両方未設定)";
        _lblBadge.BackColor = SystemColors.Control;
        Controls.Add(_lblBadge);

        // 注釈文
        var lblNote = new Label
        {
            Text = "※ HSL と RGB の両方を入力すると、両方の色サンプルと色差 ΔE を表示します。"
                 + "運用が安定するまでは両者を併記して整合性を目視確認します。",
            Location = new Point(8, 110),
            Size = new Size(540, 40),
            ForeColor = SystemColors.GrayText,
            AutoSize = false
        };
        Controls.Add(lblNote);
    }

    private void SetupNumeric(NumericUpDown num, int min, int max, int initial, Point location)
    {
        num.Minimum = min;
        num.Maximum = max;
        num.Value = initial;
        num.Location = location;
        num.Size = new Size(70, 23);
        Controls.Add(num);
    }

    private void AddSmallLabel(string text, Point location)
    {
        var lbl = new Label
        {
            Text = text,
            Location = location,
            Size = new Size(20, 18),
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lbl);
    }

    private void WireEvents()
    {
        _numH.ValueChanged += OnAnyValueChanged;
        _numS.ValueChanged += OnAnyValueChanged;
        _numL.ValueChanged += OnAnyValueChanged;
        _numR.ValueChanged += OnAnyValueChanged;
        _numG.ValueChanged += OnAnyValueChanged;
        _numB.ValueChanged += OnAnyValueChanged;
        _chkHslNull.CheckedChanged += (_, __) => { UpdateHslEnabled(); OnAnyValueChanged(this, EventArgs.Empty); };
        _chkRgbNull.CheckedChanged += (_, __) => { UpdateRgbEnabled(); OnAnyValueChanged(this, EventArgs.Empty); };
    }

    private void OnAnyValueChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        UpdatePreview();
    }

    private void UpdateHslEnabled()
    {
        bool en = !_chkHslNull.Checked;
        _numH.Enabled = en; _numS.Enabled = en; _numL.Enabled = en;
    }

    private void UpdateRgbEnabled()
    {
        bool en = !_chkRgbNull.Checked;
        _numR.Enabled = en; _numG.Enabled = en; _numB.Enabled = en;
    }

    // ─────────────────────────────────────────────────────────────────────
    // プレビューと ΔE 評価
    // ─────────────────────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        // HSL プレビュー
        Color? hslColor = null;
        if (!_chkHslNull.Checked)
        {
            hslColor = HslToRgb((double)_numH.Value, (double)_numS.Value / 100.0, (double)_numL.Value / 100.0);
        }
        _panelHslPreview.BackColor = hslColor ?? Color.LightGray;

        // RGB プレビュー
        Color? rgbColor = null;
        if (!_chkRgbNull.Checked)
        {
            rgbColor = Color.FromArgb((int)_numR.Value, (int)_numG.Value, (int)_numB.Value);
        }
        _panelRgbPreview.BackColor = rgbColor ?? Color.LightGray;

        // ΔE 評価
        if (hslColor.HasValue && rgbColor.HasValue)
        {
            double deltaE = ComputeCie76DeltaE(hslColor.Value, rgbColor.Value);
            _lblDeltaE.Text = deltaE.ToString("F2");
            // しきい値: 知覚閾値 ΔE ≈ 2.3 / 軽微な違い ≈ 5.0
            if (deltaE < 2.3)
            {
                _lblBadge.Text = "✓ 許容範囲";
                _lblBadge.BackColor = Color.FromArgb(220, 255, 220);
                _lblBadge.ForeColor = Color.FromArgb(0, 100, 0);
            }
            else if (deltaE < 5.0)
            {
                _lblBadge.Text = "△ 要確認";
                _lblBadge.BackColor = Color.FromArgb(255, 248, 200);
                _lblBadge.ForeColor = Color.FromArgb(150, 100, 0);
            }
            else
            {
                _lblBadge.Text = "× 不一致";
                _lblBadge.BackColor = Color.FromArgb(255, 220, 220);
                _lblBadge.ForeColor = Color.FromArgb(160, 0, 0);
            }
        }
        else if (!hslColor.HasValue && !rgbColor.HasValue)
        {
            _lblDeltaE.Text = "—";
            _lblBadge.Text = "(両方未設定)";
            _lblBadge.BackColor = SystemColors.Control;
            _lblBadge.ForeColor = SystemColors.ControlText;
        }
        else
        {
            _lblDeltaE.Text = "—";
            _lblBadge.Text = "(片方のみ入力)";
            _lblBadge.BackColor = SystemColors.Control;
            _lblBadge.ForeColor = SystemColors.GrayText;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 色変換ヘルパ（ローカル static）
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// HSL（H ∈ [0,360], S/L ∈ [0,1]）を sRGB の <see cref="Color"/> に変換する。
    /// 標準的な HSL→RGB 公式（RGB は 0-255 の整数に丸める）。
    /// </summary>
    private static Color HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = h / 60.0;
        double x = c * (1 - Math.Abs((hp % 2) - 1));
        double r1, g1, b1;
        if (hp < 1)      { r1 = c; g1 = x; b1 = 0; }
        else if (hp < 2) { r1 = x; g1 = c; b1 = 0; }
        else if (hp < 3) { r1 = 0; g1 = c; b1 = x; }
        else if (hp < 4) { r1 = 0; g1 = x; b1 = c; }
        else if (hp < 5) { r1 = x; g1 = 0; b1 = c; }
        else             { r1 = c; g1 = 0; b1 = x; }
        double m = l - c / 2.0;
        int r = (int)Math.Round((r1 + m) * 255.0);
        int g = (int)Math.Round((g1 + m) * 255.0);
        int b = (int)Math.Round((b1 + m) * 255.0);
        return Color.FromArgb(Clamp(r, 0, 255), Clamp(g, 0, 255), Clamp(b, 0, 255));
    }

    /// <summary>
    /// 2 つの sRGB 色の CIE76 色差 ΔE を計算する。
    /// CIE76 は CIE Lab 空間でのユークリッド距離。CIE94 / CIEDE2000 ほど厳密ではないが
    /// 「概ね同じ色か」の判定には十分。人の知覚閾値は ΔE ≈ 2.3 とされる。
    /// </summary>
    private static double ComputeCie76DeltaE(Color a, Color b)
    {
        var (la, aa, ba) = SrgbToLab(a);
        var (lb, ab, bb) = SrgbToLab(b);
        double dl = la - lb;
        double da = aa - ab;
        double db = ba - bb;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>sRGB（ガンマ付き、0-255）を CIE Lab に変換する。D65 白色点。</summary>
    private static (double L, double a, double b) SrgbToLab(Color c)
    {
        // sRGB → 線形 RGB（ガンマ展開）
        double r = SrgbToLinear(c.R / 255.0);
        double g = SrgbToLinear(c.G / 255.0);
        double bl = SrgbToLinear(c.B / 255.0);

        // 線形 RGB → XYZ（D65）
        double x = (r * 0.4124564 + g * 0.3575761 + bl * 0.1804375) / 0.95047;
        double y = (r * 0.2126729 + g * 0.7151522 + bl * 0.0721750) / 1.00000;
        double z = (r * 0.0193339 + g * 0.1191920 + bl * 0.9503041) / 1.08883;

        // XYZ → Lab
        double fx = LabF(x);
        double fy = LabF(y);
        double fz = LabF(z);
        double L = 116.0 * fy - 16.0;
        double a = 500.0 * (fx - fy);
        double b2 = 200.0 * (fy - fz);
        return (L, a, b2);
    }

    private static double SrgbToLinear(double c)
        => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double LabF(double t)
        => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : (7.787 * t + 16.0 / 116.0);

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    private static decimal Clamp(decimal v, decimal lo, decimal hi) => v < lo ? lo : (v > hi ? hi : v);
}
