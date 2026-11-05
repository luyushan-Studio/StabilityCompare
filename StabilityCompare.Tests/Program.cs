using StabilityCompare;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); checks++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (Exception) { Check(true, name); return; } throw new Exception("FAILED: " + name); }
var same = CompareData.Compare([0, 1, -2, 3], [0, 1, -2, 3], 0, 0, 3);
Check(same.Rmse == 0 && same.MaxDifference == 0 && same.Correlation == 1, "identical wave and preserved zero");
var shifted = CompareData.Compare([0, 1, 2, 3], [1, 2, 3], 1, 0, 3);
Check(shifted.Start == 1 && shifted.End == 3 && shifted.Rmse == 0, "positive shift and unequal lengths");
var negative = CompareData.Compare([1, 2, 3], [0, 1, 2, 3], -1, 0, 2);
Check(negative.Rmse == 0 && negative.Difference.Length == 3, "negative shift");
var subset = CompareData.Compare([5, 10, 20, 99], [0, 8, 16, 0], 0, 1, 2);
Check(Math.Abs(subset.Rmse - Math.Sqrt(10)) < 1e-12 && subset.MaxDifference == 4 && subset.A.Mean == 15, "inclusive selected range metrics");
Check(CompareData.Compare([2, 2], [3, 3], 0, 0, 1).Correlation == null, "constant correlation undefined");
Reject(() => CompareData.Compare([1], [1], 5, 0, 0), "no overlap rejected");
Reject(() => CompareData.Compare([1, 2], [1, 2], 0, 1, 0), "reversed range rejected");
var temp = Path.Combine(Path.GetTempPath(), "StabilityCompareTests-" + Guid.NewGuid()); Directory.CreateDirectory(temp);
var path = Path.Combine(temp, "StabilityTime.csv");
File.WriteAllText(path, "Value\n0\n1.5\n-2e-3\n"); var first = CompareData.Read(path, default);
Check(first.Values.Length == 3 && first.Values[0] == 0 && first.Values[2] == -.002, "CSV invariant culture and zero");
File.WriteAllText(path, "Value\n5\n"); var second = CompareData.Read(path, default);
Check(second.Values.Length == 1 && second.Hash != first.Hash && first.Values.Length == 3, "reload replaces immutable snapshot");
foreach (string input in new[] { "Value\nNaN\n", "Value\nInfinity\n", "Value\n\n", "Wrong\n1\n", "Value\n", "Value\n1,2\n" })
{ File.WriteAllText(path, input); Reject(() => CompareData.Read(path, default), "invalid CSV rejected: " + input.Replace('\n', '|')); }
File.WriteAllText(path, "Value\n1\n"); using var cts = new CancellationTokenSource(); cts.Cancel(); Reject(() => CompareData.Read(path, cts.Token), "cancelled read");
if (args.Length > 0)
{
    var paths = Directory.GetFiles(args[0], "StabilityTime.csv", SearchOption.AllDirectories);
    Check(paths.Length == 21, "21 actual stability CSV files");
    foreach (var file in paths)
    {
        var wave = CompareData.Read(file, default); var result = CompareData.Compare(wave.Values, wave.Values, 0, 0, wave.Values.Length - 1);
        Check(wave.Values.Length == 320000 && result.MaxDifference == 0, "actual CSV self-compare " + Path.GetRelativePath(args[0], file));
    }
}
Console.WriteLine($"SUCCESS: {checks} checks");
