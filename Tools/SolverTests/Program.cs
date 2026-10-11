using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AstroTracker.PlateSolving;

// Usage: dotnet run -c Release -- [trials] [seed]
//        dotnet run -c Release -- solve file.rgb width height scale [raHours decDeg]   (see SolveFile.cs)
// Simulates frames of the 130 PDS + EOS 600D from the catalogue and checks that the solver finds them.
static class Program
{
    const int Width = 5184, Height = 3456;
    const double PixelScale = 1.37;

    static Random _random;

    static int Main(string[] args)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "AstroTracker", "Assets", "Resources", "StarCatalog", "tycho2.bytes");
        var catalog = new StarCatalog(File.ReadAllBytes(path));
        if (args.Length > 0 && args[0] == "solve")
            return SolveFile.Run(catalog, args);

        int trials = args.Length > 0 ? int.Parse(args[0]) : 200;
        _random = new Random(args.Length > 1 ? int.Parse(args[1]) : 1);
        Console.WriteLine($"catalogue: {catalog.StarCount} stars, epoch J{catalog.Epoch:F2}");

        bool lists = StarListTest(catalog, trials);
        bool noise = RandomStarsTest(catalog, Math.Max(10, trials / 4));
        bool frames = RenderedFrameTest(catalog, Math.Max(3, trials / 20));
        bool mount = MountTests.Run();
        return lists && noise && frames && mount ? 0 : 1;
    }

    /// <summary>Solver alone: star positions taken from the catalogue and degraded.</summary>
    static bool StarListTest(StarCatalog catalog, int trials)
    {
        int solved = 0, wrong = 0;
        var buildMs = new List<double>();
        var solveMs = new List<double>();
        var errors = new List<double>();
        var quads = new List<double>();

        for (int t = 0; t < trials; t++)
        {
            Camera camera = RandomCamera();
            double limit = 10.0 + 1.5 * _random.NextDouble();
            List<DetectedStar> stars = SimulateDetections(catalog, camera, limit, out int real);

            var clock = Stopwatch.StartNew();
            var index = new SolverIndex(catalog, RandomHint(camera.Center, 7.0), 10.0);
            buildMs.Add(clock.Elapsed.TotalMilliseconds);
            quads.Add(index.QuadCount);

            clock.Restart();
            PlateSolution solution = PlateSolver.Solve(stars, Width, Height, index);
            solveMs.Add(clock.Elapsed.TotalMilliseconds);

            double dec = camera.Center.Dec * Angle.RadToDeg;
            if (solution == null)
            {
                Console.WriteLine($"  #{t}: no solution ({real} real stars, limit {limit:F1}, dec {dec:F0})");
                continue;
            }
            double error = Check(solution, camera);
            if (error > 5)
            {
                wrong++;
                double off = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg).AngleTo(camera.Center) * Angle.RadToArcsec;
                Console.WriteLine($"  #{t}: WRONG solution, {off:F1} arcsec off, {solution.MatchedStars} matches, dec {dec:F2}, " +
                                  $"rotation {solution.RotationDeg:F3} vs {camera.RotationDeg:F3}, mirrored {solution.Mirrored} vs {camera.Mirrored}, " +
                                  $"scale {solution.PixelScaleArcsec:F4} vs {camera.Scale * Angle.RadToArcsec:F4}");
                continue;
            }
            solved++;
            errors.Add(error);
        }

        Console.WriteLine($"star lists: solved {solved}/{trials}, wrong {wrong}");
        Console.WriteLine($"  index build ms: median {Median(buildMs):F0}, max {Max(buildMs):F0}; " +
                          $"quads median {Median(quads):F0}, max {Max(quads):F0}");
        Console.WriteLine($"  solve ms: median {Median(solveMs):F1}, max {Max(solveMs):F0}");
        if (errors.Count > 0)
            Console.WriteLine($"  centre error arcsec: median {Median(errors):F2}, max {Max(errors):F2}");
        return wrong == 0 && solved >= trials * 0.95;
    }

    /// <summary>Frames with no real stars must not produce a solution.</summary>
    static bool RandomStarsTest(StarCatalog catalog, int trials)
    {
        int falsePositives = 0;
        double worstMs = 0;
        for (int t = 0; t < trials; t++)
        {
            var stars = new List<DetectedStar>();
            for (int i = 0; i < 80; i++)
            {
                stars.Add(new DetectedStar
                {
                    X = (float)(_random.NextDouble() * (Width - 1)),
                    Y = (float)(_random.NextDouble() * (Height - 1)),
                    Flux = 80 - i,
                });
            }
            var index = new SolverIndex(catalog, RandomCamera().Center, 10.0);
            var clock = Stopwatch.StartNew();
            if (PlateSolver.Solve(stars, Width, Height, index) != null)
                falsePositives++;
            worstMs = Math.Max(worstMs, clock.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"random stars: {falsePositives} false solutions in {trials} frames, slowest {worstMs:F0} ms");
        return falsePositives == 0;
    }

    /// <summary>Whole pipeline: a rendered frame goes through the detector and then the solver.</summary>
    static bool RenderedFrameTest(StarCatalog catalog, int trials)
    {
        int solved = 0;
        for (int t = 0; t < trials; t++)
        {
            Camera camera = RandomCamera();
            GrayImage image = Render(catalog, camera, 2, out List<(double x, double y, float mag)> truth);

            var clock = Stopwatch.StartNew();
            List<DetectedStar> stars = StarDetector.Detect(image);
            double detectMs = clock.Elapsed.TotalMilliseconds;

            // Centroid accuracy against the positions the stars were drawn at.
            var offsets = new List<double>();
            foreach (DetectedStar star in stars)
            {
                double best = double.MaxValue;
                foreach (var s in truth)
                    best = Math.Min(best, Math.Sqrt((s.x - star.X) * (s.x - star.X) + (s.y - star.Y) * (s.y - star.Y)));
                if (best < 6)
                    offsets.Add(best);
            }

            var index = new SolverIndex(catalog, RandomHint(camera.Center, 7.0), 10.0);
            PlateSolution solution = PlateSolver.Solve(stars, Width, Height, index);
            double error = solution == null ? double.NaN : Check(solution, camera);
            if (error < 5)
                solved++;

            string outcome = solution == null
                ? "no solution"
                : $"{solution.MatchedStars} matches, error {error:F2} arcsec, rms {solution.RmsArcsec:F2} arcsec";
            Console.WriteLine($"  frame #{t}: {stars.Count} detected ({offsets.Count} real of {truth.Count} drawn, " +
                              $"centroid median {(offsets.Count > 0 ? Median(offsets) : 0):F2} px), " +
                              $"detect {detectMs:F0} ms, {outcome}");
        }
        Console.WriteLine($"rendered frames: solved {solved}/{trials}");
        return solved == trials;
    }

    sealed class Camera
    {
        public Vec3 Center;
        public double RotationDeg;
        public bool Mirrored;
        public double Scale;
        public TangentPlane Plane;
        public Affine Fit;

        public bool Project(Vec3 direction, out double x, out double y)
        {
            x = y = 0;
            if (!Plane.Project(direction, out double xi, out double eta))
                return false;
            Fit.Invert(xi, eta, out double u, out double v);
            x = u + (Width - 1) * 0.5;
            y = v + (Height - 1) * 0.5;
            return x >= 0 && x <= Width - 1 && y >= 0 && y <= Height - 1;
        }
    }

    static Camera RandomCamera()
    {
        // Uniform over the part of the sky that rises well above Kyiv's horizon.
        double dec = Math.Asin(-0.5 + 1.5 * _random.NextDouble());
        var camera = new Camera
        {
            Center = Vec3.FromRaDec(2 * Math.PI * _random.NextDouble(), dec),
            RotationDeg = 360 * _random.NextDouble(),
            Mirrored = _random.Next(4) == 0,
            Scale = PixelScale * (0.97 + 0.06 * _random.NextDouble()) * Angle.ArcsecToRad,
        };
        double theta = camera.RotationDeg * Angle.DegToRad, s = camera.Scale;
        double sign = camera.Mirrored ? -1 : 1;
        camera.Plane = new TangentPlane(camera.Center);
        camera.Fit = new Affine
        {
            A = -s * Math.Cos(theta) * sign,
            B = -s * Math.Sin(theta),
            D = s * Math.Sin(theta) * sign,
            E = -s * Math.Cos(theta),
        };
        return camera;
    }

    static Vec3 RandomHint(Vec3 center, double maxOffsetDeg)
    {
        double r = Math.Tan(maxOffsetDeg * Angle.DegToRad * Math.Sqrt(_random.NextDouble()));
        double phi = 2 * Math.PI * _random.NextDouble();
        return new TangentPlane(center).Unproject(r * Math.Cos(phi), r * Math.Sin(phi));
    }

    static List<DetectedStar> SimulateDetections(StarCatalog catalog, Camera camera, double limit, out int real)
    {
        var nearby = new List<CatalogStar>();
        catalog.Query(camera.Center, 1.4 * Angle.DegToRad, nearby);

        var stars = new List<DetectedStar>();
        foreach (CatalogStar star in nearby)
        {
            if (!camera.Project(star.Direction, out double x, out double y))
                continue;
            double mag = star.Magnitude + 0.35 * Gaussian();
            if (mag > limit || _random.NextDouble() < 0.1)
                continue;
            stars.Add(new DetectedStar
            {
                X = (float)(x + 0.7 * Gaussian()),
                Y = (float)(y + 0.7 * Gaussian()),
                Flux = (float)Math.Pow(10, -0.4 * mag),
            });
        }
        real = stars.Count;

        // Hot pixels, galaxies, satellites: detections with no catalogue counterpart.
        int spurious = Math.Max(2, real * 15 / 100);
        for (int i = 0; i < spurious && real > 0; i++)
        {
            stars.Add(new DetectedStar
            {
                X = (float)(_random.NextDouble() * (Width - 1)),
                Y = (float)(_random.NextDouble() * (Height - 1)),
                Flux = stars[_random.Next(real)].Flux,
            });
        }
        stars.Sort((a, b) => b.Flux.CompareTo(a.Flux));
        return stars;
    }

    static GrayImage Render(StarCatalog catalog, Camera camera, int binning,
        out List<(double x, double y, float mag)> truth)
    {
        int w = Width / binning, h = Height / binning;
        var pixels = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = 20f + 10f * x / w + 6f * y / h + 2f * (float)Gaussian();

        var nearby = new List<CatalogStar>();
        catalog.Query(camera.Center, 1.4 * Angle.DegToRad, nearby);
        truth = new List<(double, double, float)>();
        const double sigma = 1.2;
        foreach (CatalogStar star in nearby)
        {
            if (!camera.Project(star.Direction, out double fx, out double fy))
                continue;
            truth.Add((fx, fy, star.Magnitude));

            double cx = (fx + 0.5) / binning - 0.5, cy = (fy + 0.5) / binning - 0.5;
            double peak = 200 * Math.Pow(10, -0.4 * (star.Magnitude - 7.0));
            int reach = 8;
            for (int y = Math.Max(0, (int)cy - reach); y <= Math.Min(h - 1, (int)cy + reach); y++)
                for (int x = Math.Max(0, (int)cx - reach); x <= Math.Min(w - 1, (int)cx + reach); x++)
                {
                    double r2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    pixels[y * w + x] += (float)(peak * Math.Exp(-r2 / (2 * sigma * sigma)));
                }
        }

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Math.Max(0f, Math.Min(255f, pixels[i]));
        return new GrayImage(pixels, w, h, binning);
    }

    /// <summary>Distance between the solved and the true frame centre in arcsec; huge if orientation is wrong.</summary>
    static double Check(PlateSolution solution, Camera camera)
    {
        double error = Vec3.FromRaDecDegrees(solution.RaDeg, solution.DecDeg).AngleTo(camera.Center) * Angle.RadToArcsec;
        double rotation = Math.Abs(solution.RotationDeg - camera.RotationDeg) % 360;
        rotation = Math.Min(rotation, 360 - rotation);
        if (solution.Mirrored != camera.Mirrored || rotation > 0.1 ||
            Math.Abs(solution.PixelScaleArcsec * Angle.ArcsecToRad / camera.Scale - 1) > 0.005)
            return 1e9;
        return error;
    }

    static double Gaussian()
    {
        double u = 1.0 - _random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * _random.NextDouble());
    }

    static double Median(List<double> values)
    {
        var sorted = new List<double>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    static double Max(List<double> values)
    {
        double max = double.MinValue;
        foreach (double v in values)
            max = Math.Max(max, v);
        return max;
    }
}
