using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AstroTracker.PlateSolving;

/// <summary>
/// Solves a real frame outside Unity: dotnet run -c Release -- solve file.rgb width height scale [raHours decDeg]
/// The file holds raw 24-bit pixels, top row first. Without a position the whole sky is searched.
/// </summary>
static class SolveFile
{
    public static int Run(StarCatalog catalog, string[] args)
    {
        CultureInfo plain = CultureInfo.InvariantCulture;
        int width = int.Parse(args[2]), height = int.Parse(args[3]);
        double scale = double.Parse(args[4], plain);
        int binning = width > 3000 ? 2 : 1;

        GrayImage image = GrayImage.FromRgb(File.ReadAllBytes(args[1]), width, height, 3, binning, false);
        List<DetectedStar> stars = StarDetector.Detect(image);
        Console.WriteLine($"{Path.GetFileName(args[1])}: {width} x {height}, {stars.Count} stars detected");
        for (int i = 0; i < Math.Min(8, stars.Count); i++)
            Console.WriteLine($"  star {i}: x {stars[i].X:F0}, y {stars[i].Y:F0}, flux {stars[i].Flux:F0}, area {stars[i].Area}");

        var options = new PlateSolverOptions { PixelScaleArcsec = scale };
        List<Vec3> hints = args.Length >= 7
            ? new List<Vec3> { Vec3.FromRaDecDegrees(double.Parse(args[5], plain) * 15, double.Parse(args[6], plain)) }
            : SkyAreas.Around(Vec3.FromRaDecDegrees(0, 90), 10);

        var clock = Stopwatch.StartNew();
        foreach (Vec3 hint in hints)
        {
            var index = new SolverIndex(catalog, hint, 10);
            PlateSolution solution = PlateSolver.Solve(stars, width, height, index, options);
            if (solution == null)
                continue;

            Console.WriteLine(string.Format(plain,
                "solved in {0:F1} s: RA {1:F4} h, Dec {2:F4} deg, scale {3:F3}, up {4:F1} deg, {5} matches, rms {6:F2} arcsec",
                clock.Elapsed.TotalSeconds, solution.RaDeg / 15, solution.DecDeg, solution.PixelScaleArcsec,
                solution.RotationDeg, solution.MatchedStars, solution.RmsArcsec));
            return 0;
        }
        Console.WriteLine($"no solution in {hints.Count} areas, {clock.Elapsed.TotalSeconds:F1} s");
        return 1;
    }
}
