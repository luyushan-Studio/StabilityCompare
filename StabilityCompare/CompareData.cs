using System.Globalization;
using System.Security.Cryptography;

namespace StabilityCompare;

public sealed record Wave(string Path, double[] Values, string Hash, DateTime Modified);
public sealed record Summary(int Count, double Mean, double Rms, double Peak, double PeakToPeak);
public sealed record Comparison(int Start, int End, double[] Difference, Summary A, Summary B, double Rmse, double MaxDifference, double? Correlation);

public static class CompareData
{
    public static Wave Read(string path, CancellationToken token)
    {
        // A shared-read-only handle prevents writes during reading on Windows.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var modified = File.GetLastWriteTimeUtc(path);
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), true, 65536, true);
        if (reader.ReadLine()?.Trim() != "Value")
            throw new InvalidDataException($"{path}\n首行必须为 Value（单列 CSV）。");
        var values = new List<double>();
        string? line; int row = 1;
        while ((line = reader.ReadLine()) != null)
        {
            row++;
            if ((row & 4095) == 0) token.ThrowIfCancellationRequested();
            if (!double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new InvalidDataException($"{path}\n第 {row:N0} 行不是有效的单列有限数值。未跳过该行，以免时间位置改变。");
            values.Add(value);
        }
        token.ThrowIfCancellationRequested();
        if (values.Count == 0) throw new InvalidDataException($"{path}\n文件没有采样数据。");
        stream.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        return new Wave(path, values.ToArray(), hash, modified);
    }

    public static Summary Summarize(ReadOnlySpan<double> data)
    {
        double mean = 0, sumSq = 0, lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        int n = 0;
        foreach (double v in data)
        {
            n++; mean += (v - mean) / n; sumSq += v * v; lo = Math.Min(lo, v); hi = Math.Max(hi, v);
        }
        return new(n, mean, Math.Sqrt(sumSq / n), Math.Max(Math.Abs(lo), Math.Abs(hi)), hi - lo);
    }

    // B sample j is displayed at A-coordinate j + shift. Endpoints are inclusive.
    public static Comparison Compare(double[] a, double[] b, int shift, int requestedStart, int requestedEnd)
    {
        int start = Math.Max(Math.Max(0, shift), requestedStart);
        int end = (int)Math.Min(Math.Min(a.Length - 1L, b.Length - 1L + shift), requestedEnd);
        if (end < start) throw new InvalidOperationException("当前选区与偏移下没有共同采样区间。");
        var av = a.AsSpan(start, end - start + 1);
        var bv = b.AsSpan(start - shift, end - start + 1);
        var sa = Summarize(av); var sb = Summarize(bv);
        var diff = new double[av.Length];
        double ss = 0, max = 0, aa = 0, bb = 0, ab = 0;
        for (int i = 0; i < diff.Length; i++)
        {
            double d = av[i] - bv[i]; diff[i] = d; ss += d * d; max = Math.Max(max, Math.Abs(d));
            double da = av[i] - sa.Mean, db = bv[i] - sb.Mean;
            aa += da * da; bb += db * db; ab += da * db;
        }
        double? corr = aa > 0 && bb > 0 ? Math.Clamp(ab / Math.Sqrt(aa * bb), -1, 1) : null;
        return new(start, end, diff, sa, sb, Math.Sqrt(ss / diff.Length), max, corr);
    }
}
