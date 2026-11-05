using System.Globalization;
using System.Text;
using ScottPlot.WinForms;

namespace StabilityCompare;

public sealed class MainForm : Form
{
    static readonly string DefaultRoot = Environment.GetEnvironmentVariable("STABILITYCOMPARE_DATA_ROOT")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    readonly TextBox root = new() { Width = 740, Text = DefaultRoot };
    readonly ComboBox productA = Combo(290), productB = Combo(290), channel = Combo(120);
    readonly NumericUpDown start = Number(0, int.MaxValue, 0), end = Number(0, int.MaxValue, 0), shift = Number(-100000000, 100000000, 0), rate = Number(1, 10000000, 32000);
    readonly CheckBox seconds = new() { Text = "已确认采样率，按秒显示", AutoSize = true };
    readonly FormsPlot overlay = new() { Dock = DockStyle.Fill }, difference = new() { Dock = DockStyle.Fill };
    readonly TabControl plotTabs = new() { Dock = DockStyle.Fill, Padding = new Point(18, 7) };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0) };
    readonly TextBox details = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = System.Drawing.Color.FromArgb(247, 249, 252), BorderStyle = BorderStyle.None };
    readonly DataGridView stats = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = System.Drawing.Color.White, BorderStyle = BorderStyle.None };
    readonly Button exportImage, exportStats;
    Wave? a, b; Comparison? comparison;
    CancellationTokenSource? loading; int generation; bool scanning, busy;
    string loadedLabelA = "", loadedLabelB = "";
    ScottPlot.Plottables.Crosshair? cursor;

    static ComboBox Combo(int width) => new() { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
    static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, Width = 115, ThousandsSeparator = true };
    static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 8, 4, 0) };
    static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(7, 4, 4, 0), BackColor = System.Drawing.Color.White };
    static Button Button(string text, EventHandler action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 29, Padding = new Padding(5, 0, 5, 0) };
        button.Click += action; return button;
    }

    public MainForm()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        Text = "稳态波形对比 · StabilityTime"; Width = 1400; Height = 960; MinimumSize = new Size(1120, 800);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 9F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = System.Drawing.Color.FromArgb(233, 238, 245) };
        foreach (float height in new[] { 44f, 44f, 44f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        Controls.Add(layout);
        var r1 = Row(); r1.Controls.AddRange([Label("数据目录"), root, Button("浏览…", Browse), Button("扫描目录", (_, _) => Scan())]); layout.Controls.Add(r1, 0, 0);
        var r2 = Row(); r2.Controls.AddRange([Label("产品 A"), productA, Label("产品 B"), productB, Label("同名通道"), channel, Button("加载 / 重新加载", async (_, _) => await LoadPair())]); layout.Controls.Add(r2, 0, 1);
        var r3 = Row(); r3.Controls.AddRange([Label("选区 起点"), start, Label("终点（含）"), end, Label("B 偏移点数"), shift, seconds, rate, Label("Hz"), Button("应用", async (_, _) => await Calculate()), Button("全区间", async (_, _) => { if (a != null) { start.Value = 0; end.Value = a.Values.Length - 1; await Calculate(); } })]); layout.Controls.Add(r3, 0, 2);
        var overlayPage = new TabPage("波形叠加") { Padding = new Padding(3) };
        var analysisPage = new TabPage("差值与统计") { Padding = new Padding(3) };
        plotTabs.TabPages.AddRange([overlayPage, analysisPage]); layout.Controls.Add(plotTabs, 0, 3);
        var overlayLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        overlayLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); overlayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        overlayLayout.Controls.Add(overlay, 0, 0);
        var overlayActions = Row();
        overlayActions.Controls.Add(Button("可见范围作为选区", async (_, _) => await UseVisibleRange()));
        overlayActions.Controls.Add(Button("放大到选区", (_, _) => ZoomSelection()));
        overlayActions.Controls.Add(Button("复位缩放", (_, _) => { overlay.Plot.Axes.AutoScale(); overlay.Refresh(); }));
        overlayActions.Controls.Add(Label("滚轮缩放 · 左键拖动 · 切换分页保留当前视图"));
        overlayLayout.Controls.Add(overlayActions, 0, 1); overlayPage.Controls.Add(overlayLayout);
        var analysisLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        analysisLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); analysisLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 146)); analysisLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        analysisLayout.Controls.Add(difference, 0, 0); analysisPage.Controls.Add(analysisLayout);
        var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64)); lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        lower.Controls.Add(stats, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12, 4, 0, 0) };
        actions.Controls.Add(Button("返回波形叠加", (_, _) => plotTabs.SelectedIndex = 0));
        exportImage = Button("导出双图 PNG", ExportImage); exportStats = Button("导出对比报告 CSV", ExportReport);
        actions.Controls.Add(exportImage); actions.Controls.Add(exportStats); actions.Controls.Add(Label("偏移为正：B 向右移动\n统计仅覆盖共同区间")); lower.Controls.Add(actions, 1, 0); analysisLayout.Controls.Add(lower, 0, 1);
        analysisLayout.Controls.Add(details, 0, 2); layout.Controls.Add(status, 0, 4);
        plotTabs.SelectedIndexChanged += (_, _) => BeginInvoke((Action)(() => { overlay.Refresh(); difference.Refresh(); }));
        stats.Columns.Add("Metric", "指标（共同选区）"); stats.Columns.Add("A", "产品 A"); stats.Columns.Add("B", "产品 B"); stats.Columns.Add("Diff", "差异");
        productA.SelectedIndexChanged += (_, _) => UpdateChannels(); productB.SelectedIndexChanged += (_, _) => UpdateChannels();
        channel.SelectedIndexChanged += async (_, _) => { if (!scanning && channel.SelectedItem != null) await LoadPair(); };
        root.TextChanged += (_, _) => { if (!scanning) { InvalidatePair(); status.Text = "目录已更改，请扫描目录。"; } };
        foreach (var input in new[] { start, end, shift, rate }) input.ValueChanged += (_, _) => MarkPending();
        seconds.CheckedChanged += (_, _) => MarkPending();
        overlay.MouseMove += (_, e) =>
        {
            if (cursor == null || a == null || busy) return;
            var c = overlay.Plot.GetCoordinates(new ScottPlot.Pixel(e.X, e.Y)); cursor.X = c.X; cursor.Y = c.Y; overlay.Refresh();
        };
        FormClosing += (_, _) => loading?.Cancel();
        EmptyPlots(); Shown += (_, _) => { overlay.Plot.ScaleFactor = DeviceDpi / 96f; difference.Plot.ScaleFactor = DeviceDpi / 96f; Scan(); };
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
    }

    void MarkPending()
    {
        if (a == null || busy) return;
        generation++; comparison = null; exportImage.Enabled = exportStats.Enabled = false;
        status.Text = "参数已更改，点击“应用”更新图表与统计。";
    }

    async Task UseVisibleRange()
    {
        if (a == null || comparison == null || busy) return;
        var lim = overlay.Plot.Axes.GetLimits(); double fs = seconds.Checked ? (double)rate.Value : 1;
        start.Value = Math.Clamp((decimal)Math.Ceiling(lim.Left * fs), 0, a.Values.Length - 1);
        end.Value = Math.Clamp((decimal)Math.Floor(lim.Right * fs), 0, a.Values.Length - 1);
        await Calculate(); ZoomSelection();
    }

    void ZoomSelection()
    {
        if (comparison == null || busy) return;
        double fs = seconds.Checked ? (double)rate.Value : 1;
        double left = comparison.Start / fs, right = comparison.End / fs;
        if (left == right) { left -= .5 / fs; right += .5 / fs; }
        overlay.Plot.Axes.SetLimitsX(left, right);
        overlay.Refresh();
    }

    void Browse(object? sender, EventArgs args)
    {
        using var dialog = new FolderBrowserDialog { Description = "选择包含产品文件夹的数据根目录", SelectedPath = root.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) { root.Text = dialog.SelectedPath; Scan(); }
    }

    void Scan()
    {
        scanning = true; InvalidatePair();
        try
        {
            var folders = Directory.GetDirectories(root.Text).Select(Path.GetFileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            productA.Items.Clear(); productB.Items.Clear(); channel.Items.Clear(); productA.Items.AddRange(folders!); productB.Items.AddRange(folders!);
            if (folders.Length > 0) { productA.SelectedIndex = 0; productB.SelectedIndex = folders.Length > 1 ? 1 : 0; }
            status.Text = $"找到 {folders.Length} 个产品目录，仅配对 StabilityTime.csv。";
        }
        catch (Exception ex) { status.Text = "目录扫描失败：" + ex.Message; }
        finally { scanning = false; }
        UpdateChannels();
    }

    void UpdateChannels()
    {
        if (scanning) return;
        InvalidatePair(); scanning = true;
        try
        {
            string? old = channel.SelectedItem as string; channel.Items.Clear();
            if (productA.SelectedItem == null || productB.SelectedItem == null) return;
            string pa = Path.Combine(root.Text, productA.Text), pb = Path.Combine(root.Text, productB.Text);
            var names = Directory.GetDirectories(pa).Select(Path.GetFileName).Where(n => n != null && File.Exists(Path.Combine(pa, n, "StabilityTime.csv")) && File.Exists(Path.Combine(pb, n, "StabilityTime.csv"))).OrderBy(n => n!.Length).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            channel.Items.AddRange(names!);
            if (names.Length > 0) channel.SelectedIndex = old != null && channel.Items.Contains(old) ? channel.Items.IndexOf(old) : 0;
            status.Text = names.Length == 0 ? "两个产品没有可配对的 StabilityTime.csv。" : $"找到 {names.Length} 个同名稳态通道，点击“加载 / 重新加载”。";
        }
        catch (Exception ex) { status.Text = "通道扫描失败：" + ex.Message; }
        finally { scanning = false; }
    }

    void InvalidatePair()
    {
        generation++; loading?.Cancel(); a = b = null; comparison = null; busy = false; SetInputs(true); details.Clear(); stats.Rows.Clear(); EmptyPlots(); exportImage.Enabled = exportStats.Enabled = false;
    }

    void SetInputs(bool enabled)
    {
        foreach (var input in new[] { start, end, shift, rate }) input.Enabled = enabled;
        seconds.Enabled = enabled;
    }

    void EmptyPlots()
    {
        cursor = null; overlay.Plot.Clear(); difference.Plot.Clear(); overlay.Plot.Title("A / B 稳态波形 · 等待加载"); difference.Plot.Title("A − B · 共同区间"); overlay.Refresh(); difference.Refresh();
    }

    async Task LoadPair()
    {
        if (channel.SelectedItem == null) return;
        InvalidatePair(); int version = generation; loading?.Dispose(); loading = new CancellationTokenSource(); var token = loading.Token;
        string pa = Path.Combine(root.Text, productA.Text, channel.Text, "StabilityTime.csv"), pb = Path.Combine(root.Text, productB.Text, channel.Text, "StabilityTime.csv");
        string la = $"A · {productA.Text} · {channel.Text}", lb = $"B · {productB.Text} · {channel.Text}";
        busy = true; SetInputs(false); status.Text = "正在从磁盘读取两个稳态文件…";
        try
        {
            var pair = await Task.Run(() => (CompareData.Read(pa, token), CompareData.Read(pb, token)), token);
            if (version != generation || IsDisposed) return;
            a = pair.Item1; b = pair.Item2; loadedLabelA = la; loadedLabelB = lb;
            start.Value = 0; end.Value = a.Values.Length - 1; shift.Value = 0;
            details.Text = $"A：{a.Path}\r\nB：{b.Path}\r\nA {a.Values.Length:N0} 点 / B {b.Values.Length:N0} 点；单位：原始数值。实际采样率须确认，界面默认值不代表历史文件采样率。";
            busy = false; await Calculate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == generation) { InvalidatePair(); status.Text = "加载失败：" + ex.Message; details.Text = ex.Message; } }
        finally { if (version == generation) { busy = false; SetInputs(true); } }
    }

    async Task Calculate()
    {
        if (a == null || b == null) return;
        int version = ++generation; var wa = a; var wb = b; int s = (int)start.Value, e = (int)end.Value, offset = (int)shift.Value; double fs = seconds.Checked ? (double)rate.Value : 1;
        bool useSeconds = seconds.Checked;
        comparison = null; exportImage.Enabled = exportStats.Enabled = false; busy = true; SetInputs(false); status.Text = "正在计算共同选区…";
        try
        {
            var result = await Task.Run(() => CompareData.Compare(wa.Values, wb.Values, offset, s, e));
            if (version != generation || IsDisposed) return;
            comparison = result;
            overlay.Plot.Clear(); difference.Plot.Clear();
            var sa = overlay.Plot.Add.Signal(wa.Values, 1 / fs); sa.LegendText = loadedLabelA; sa.Color = ScottPlot.Colors.DodgerBlue;
            var sb = overlay.Plot.Add.Signal(wb.Values, 1 / fs); sb.Data.XOffset = offset / fs; sb.LegendText = loadedLabelB; sb.Color = ScottPlot.Colors.DarkOrange;
            var zone = overlay.Plot.Add.HorizontalSpan(result.Start / fs, result.End / fs); zone.FillColor = ScottPlot.Colors.Green.WithAlpha(.08); zone.LineWidth = 0;
            cursor = overlay.Plot.Add.Crosshair(0, 0); cursor.LineColor = ScottPlot.Colors.Gray;
            overlay.Plot.ShowLegend(); overlay.Plot.Title("StabilityTime · A / B 叠加（完整数据）");
            var ds = difference.Plot.Add.Signal(result.Difference, 1 / fs); ds.Data.XOffset = result.Start / fs; ds.Color = ScottPlot.Colors.SeaGreen;
            difference.Plot.Title($"A − B · 点 {result.Start:N0} 至 {result.End:N0} · B 偏移 {offset:N0} 点");
            string axis = useSeconds ? $"相对时间（秒），采样率 {fs:N0} Hz" : "采样点序号（从 0 开始）";
            overlay.Plot.XLabel(axis); difference.Plot.XLabel(axis); overlay.Plot.YLabel("原始数值"); difference.Plot.YLabel("差值");
            overlay.Plot.Axes.AutoScale(); difference.Plot.Axes.AutoScale(); overlay.Refresh(); difference.Refresh();
            stats.Rows.Clear();
            AddStat("点数", result.A.Count.ToString("N0"), result.B.Count.ToString("N0"), $"完整长度差 {wa.Values.Length - wb.Values.Length:N0}");
            AddStat("均值", F(result.A.Mean), F(result.B.Mean), "RMSE " + F(result.Rmse));
            AddStat("RMS", F(result.A.Rms), F(result.B.Rms), "最大绝对差 " + F(result.MaxDifference));
            AddStat("绝对峰值 / 峰峰值", F(result.A.Peak) + " / " + F(result.A.PeakToPeak), F(result.B.Peak) + " / " + F(result.B.PeakToPeak), "相关系数 " + (result.Correlation.HasValue ? F(result.Correlation.Value) : "不适用"));
            status.Text = $"已加载 · 共同选区 {result.Difference.Length:N0} 点 · {(wa.Values.Length == wb.Values.Length ? "长度相同" : "长度不同，未补零或拉伸；差值仅算共同区间")} · {(wa.Hash == wb.Hash ? "文件内容完全相同" : "文件内容不同")}";
            exportImage.Enabled = exportStats.Enabled = true;
        }
        catch (Exception ex) { if (version == generation) { EmptyPlots(); stats.Rows.Clear(); status.Text = "无法比较：" + ex.Message; } }
        finally { if (version == generation) { busy = false; SetInputs(true); } }
    }

    static string F(double value) => value.ToString("G7", CultureInfo.InvariantCulture);
    void AddStat(string metric, string av, string bv, string delta) => stats.Rows.Add(metric, av, bv, delta);

    void ExportImage(object? sender, EventArgs args)
    {
        if (comparison == null) return;
        using var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = "稳态波形对比.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var top = new MemoryStream(overlay.Plot.GetImage(1600, 650).GetImageBytes()); using var bottom = new MemoryStream(difference.Plot.GetImage(1600, 450).GetImageBytes());
            using var ti = System.Drawing.Image.FromStream(top); using var bi = System.Drawing.Image.FromStream(bottom); using var bitmap = new Bitmap(1600, 1100); using var g = Graphics.FromImage(bitmap);
            g.DrawImage(ti, 0, 0); g.DrawImage(bi, 0, 650); bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png); status.Text = "双图已导出：" + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
    }

    void ExportReport(object? sender, EventArgs args)
    {
        if (comparison == null || a == null || b == null) return;
        using var dialog = new SaveFileDialog { Filter = "CSV 报告|*.csv", FileName = "稳态对比报告.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            static string Q(object? v) => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture)?.Replace("\"", "\"\"") + "\"";
            var lines = new List<string>(); void Row(params object?[] v) => lines.Add(string.Join(",", v.Select(Q)));
            Row("字段", "值"); Row("文件A", a.Path); Row("文件B", b.Path); Row("SHA256-A", a.Hash); Row("SHA256-B", b.Hash); Row("A修改时间UTC", a.Modified.ToString("O")); Row("B修改时间UTC", b.Modified.ToString("O")); Row("单位", "原始数值"); Row("A总点数", a.Values.Length); Row("B总点数", b.Values.Length); Row("采样率Hz", seconds.Checked ? rate.Value : "未确认"); Row("B偏移点数", shift.Value); Row("选区起点", comparison.Start); Row("选区终点含", comparison.End); Row("共同点数", comparison.Difference.Length); Row("RMSE", comparison.Rmse); Row("最大绝对差", comparison.MaxDifference); Row("相关系数", comparison.Correlation?.ToString("R", CultureInfo.InvariantCulture) ?? "不适用");
            Row("指标", "A", "B"); Row("均值", comparison.A.Mean, comparison.B.Mean); Row("RMS", comparison.A.Rms, comparison.B.Rms); Row("绝对峰值", comparison.A.Peak, comparison.B.Peak); Row("峰峰值", comparison.A.PeakToPeak, comparison.B.PeakToPeak);
            File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true)); status.Text = "报告已导出：" + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
    }
}
