using System;
using System.Collections.Generic;

namespace AstroTracker.PlateSolving
{
    public sealed class SolverIndexOptions
    {
        /// <summary>Smallest and largest quad diameter. Should span roughly 25%..85% of the short frame side.</summary>
        public double MinQuadDeg = 0.33;
        public double MaxQuadDeg = 1.1;
        /// <summary>Only the brightest star of every sky cell of this size takes part in quads.</summary>
        public double CellDeg = 0.3;
        /// <summary>How many of the brightest stars inside a pair's circle are combined into quads.</summary>
        public int MaxInsideStars = 4;
        /// <summary>Stars with a catalogue neighbour closer than this (0.1 arcsec) blend on the sensor and are skipped.</summary>
        public int MinProximity = 60;
        /// <summary>Extra radius of catalogue stars kept around the search area, for verifying frames at its edge.</summary>
        public double MarginDeg = 1.5;
    }

    /// <summary>
    /// Geometric hash of star quads for one area of the sky, built from the catalogue on demand.
    /// Rebuild it when the expected pointing leaves the area.
    /// </summary>
    public sealed class SolverIndex
    {
        internal const double CodeBin = 0.01;
        const int CodeBins = 150;
        const double CodeOffset = 0.25;

        public Vec3 Center { get; }
        public double RadiusDeg { get; }
        public int StarCount => StarDirection.Length;
        public int QuadCount => _quadKeys.Length;

        internal readonly Vec3[] StarDirection;
        internal readonly float[] StarMagnitude;
        /// <summary>Four star indices per quad, in the order the code was computed for.</summary>
        internal int[] QuadStars;
        /// <summary>Four code values per quad.</summary>
        internal float[] QuadCodes;
        /// <summary>Tangent-plane length of the quad's diameter, radians.</summary>
        internal float[] QuadSize;
        internal readonly double MinQuadSize, MaxQuadSize;

        readonly TangentPlane _plane;
        readonly PlaneGrid _starGrid;
        readonly double _stretch;
        int[] _quadKeys;

        public SolverIndex(StarCatalog catalog, Vec3 center, double radiusDeg, SolverIndexOptions options = null)
        {
            options ??= new SolverIndexOptions();
            Center = center;
            RadiusDeg = radiusDeg;

            double radius = Math.Min(60.0, radiusDeg + options.MarginDeg) * Angle.DegToRad;
            var stars = new List<CatalogStar>();
            catalog.Query(center, radius, stars);

            int n = stars.Count;
            StarDirection = new Vec3[n];
            StarMagnitude = new float[n];
            var xs = new double[n];
            var ys = new double[n];
            _plane = new TangentPlane(center);
            for (int i = 0; i < n; i++)
            {
                StarDirection[i] = stars[i].Direction;
                StarMagnitude[i] = stars[i].Magnitude;
                _plane.Project(stars[i].Direction, out xs[i], out ys[i]);
            }

            // The gnomonic projection stretches distances by up to this factor at the edge of the area.
            _stretch = 1.0 / (Math.Cos(radius) * Math.Cos(radius));
            double half = Math.Tan(radius);
            _starGrid = new PlaneGrid(xs, ys, n, half, 0.5 * Angle.DegToRad);

            MinQuadSize = 2 * Math.Tan(options.MinQuadDeg * Angle.DegToRad / 2);
            MaxQuadSize = 2 * Math.Tan(options.MaxQuadDeg * Angle.DegToRad / 2);
            BuildQuads(stars, xs, ys, half, options);
        }

        void BuildQuads(List<CatalogStar> stars, double[] xs, double[] ys, double half, SolverIndexOptions options)
        {
            // Brightest star per cell: an even spread that mirrors what a frame's brightest stars look like.
            double cell = options.CellDeg * Angle.DegToRad;
            int cells = (int)Math.Ceiling(2 * half / cell);
            var best = new int[cells * cells];
            for (int i = 0; i < best.Length; i++)
                best[i] = -1;
            for (int i = 0; i < stars.Count; i++)
            {
                if (stars[i].Proximity < options.MinProximity)
                    continue;
                int cx = Math.Max(0, Math.Min(cells - 1, (int)((xs[i] + half) / cell)));
                int cy = Math.Max(0, Math.Min(cells - 1, (int)((ys[i] + half) / cell)));
                int c = cy * cells + cx;
                if (best[c] < 0 || stars[i].Magnitude < stars[best[c]].Magnitude)
                    best[c] = i;
            }

            var chosen = new List<int>();
            foreach (int i in best)
                if (i >= 0)
                    chosen.Add(i);
            int m = chosen.Count;
            var cx2 = new double[m];
            var cy2 = new double[m];
            for (int i = 0; i < m; i++)
            {
                cx2[i] = xs[chosen[i]];
                cy2[i] = ys[chosen[i]];
            }

            double maxQuad = options.MaxQuadDeg * Angle.DegToRad;
            double cosMax = Math.Cos(maxQuad), cosMin = Math.Cos(options.MinQuadDeg * Angle.DegToRad);
            var grid = new PlaneGrid(cx2, cy2, m, half, Math.Tan(maxQuad) * _stretch * 1.01);

            var quadStars = new List<int>();
            var quadCodes = new List<float>();
            var quadSize = new List<float>();
            var keys = new List<int>();

            int keep = Math.Max(2, options.MaxInsideStars);
            var inside = new int[keep];
            var insideX = new double[keep];
            var insideY = new double[keep];

            for (int a = 0; a < m; a++)
            {
                Vec3 da = StarDirection[chosen[a]];
                int gx = grid.CellOf(cx2[a]), gy = grid.CellOf(cy2[a]);
                int gx0 = Math.Max(0, gx - 1), gx1 = Math.Min(grid.Size - 1, gx + 1);
                int gy0 = Math.Max(0, gy - 1), gy1 = Math.Min(grid.Size - 1, gy + 1);

                for (int by = gy0; by <= gy1; by++)
                for (int bx = gx0; bx <= gx1; bx++)
                for (int bi = grid.Start[by * grid.Size + bx]; bi < grid.Start[by * grid.Size + bx + 1]; bi++)
                {
                    int b = grid.Items[bi];
                    if (b <= a)
                        continue;
                    Vec3 db = StarDirection[chosen[b]];
                    double dot = da.Dot(db);
                    if (dot < cosMax || dot > cosMin)
                        continue;

                    // Local plane at the pair's midpoint: there A = -B and the circle on AB is centred on the origin.
                    var local = new TangentPlane((da + db).Normalized());
                    local.Project(da, out double ax, out double ay);
                    double r2 = ax * ax + ay * ay;

                    int count = 0;
                    for (int ky = gy0; ky <= gy1; ky++)
                    for (int kx = gx0; kx <= gx1; kx++)
                    for (int ki = grid.Start[ky * grid.Size + kx]; ki < grid.Start[ky * grid.Size + kx + 1]; ki++)
                    {
                        int k = grid.Items[ki];
                        if (k == a || k == b)
                            continue;
                        if (!local.Project(StarDirection[chosen[k]], out double px, out double py) ||
                            px * px + py * py > r2)
                            continue;

                        // Keep the brightest few, ordered by magnitude.
                        float mag = StarMagnitude[chosen[k]];
                        int pos = count;
                        while (pos > 0 && StarMagnitude[chosen[inside[pos - 1]]] > mag)
                            pos--;
                        if (pos >= keep)
                            continue;
                        for (int s = Math.Min(count, keep - 1); s > pos; s--)
                        {
                            inside[s] = inside[s - 1];
                            insideX[s] = insideX[s - 1];
                            insideY[s] = insideY[s - 1];
                        }
                        inside[pos] = k;
                        insideX[pos] = px;
                        insideY[pos] = py;
                        if (count < keep)
                            count++;
                    }

                    for (int c = 0; c < count; c++)
                    for (int d = c + 1; d < count; d++)
                    {
                        QuadCode.Raw(ax, ay, -ax, -ay, insideX[c], insideY[c], out double xc, out double yc);
                        QuadCode.Raw(ax, ay, -ax, -ay, insideX[d], insideY[d], out double xd, out double yd);
                        QuadCode.Canonicalize(ref xc, ref yc, ref xd, ref yd, out bool swapAB, out bool swapCD);

                        quadStars.Add(chosen[swapAB ? b : a]);
                        quadStars.Add(chosen[swapAB ? a : b]);
                        quadStars.Add(chosen[inside[swapCD ? d : c]]);
                        quadStars.Add(chosen[inside[swapCD ? c : d]]);
                        quadCodes.Add((float)xc);
                        quadCodes.Add((float)yc);
                        quadCodes.Add((float)xd);
                        quadCodes.Add((float)yd);
                        quadSize.Add((float)(2 * Math.Sqrt(r2)));
                        keys.Add(Key(Bin(xc), Bin(yc), Bin(xd), Bin(yd)));
                    }
                }
            }

            // Sort everything by hash key so a lookup is a binary search.
            int quads = keys.Count;
            _quadKeys = keys.ToArray();
            var order = new int[quads];
            for (int i = 0; i < quads; i++)
                order[i] = i;
            Array.Sort(_quadKeys, order);

            QuadStars = new int[quads * 4];
            QuadCodes = new float[quads * 4];
            QuadSize = new float[quads];
            for (int i = 0; i < quads; i++)
            {
                int src = order[i];
                for (int j = 0; j < 4; j++)
                {
                    QuadStars[i * 4 + j] = quadStars[src * 4 + j];
                    QuadCodes[i * 4 + j] = quadCodes[src * 4 + j];
                }
                QuadSize[i] = quadSize[src];
            }
        }

        static int Bin(double code)
        {
            return Math.Max(0, Math.Min(CodeBins - 1, (int)Math.Floor((code + CodeOffset) / CodeBin)));
        }

        static int Key(int b0, int b1, int b2, int b3) => ((b0 * CodeBins + b1) * CodeBins + b2) * CodeBins + b3;

        /// <summary>Appends the quads whose code is within <paramref name="tolerance"/> on every axis.</summary>
        internal void FindQuads(double c0, double c1, double c2, double c3, double tolerance, List<int> results)
        {
            tolerance = Math.Min(tolerance, CodeBin);
            int b0 = Bin(c0), b1 = Bin(c1), b2 = Bin(c2), b3 = Bin(c3);
            int lo3 = Math.Max(0, b3 - 1), hi3 = Math.Min(CodeBins - 1, b3 + 1);

            for (int i0 = Math.Max(0, b0 - 1); i0 <= Math.Min(CodeBins - 1, b0 + 1); i0++)
            for (int i1 = Math.Max(0, b1 - 1); i1 <= Math.Min(CodeBins - 1, b1 + 1); i1++)
            for (int i2 = Math.Max(0, b2 - 1); i2 <= Math.Min(CodeBins - 1, b2 + 1); i2++)
            {
                int last = Key(i0, i1, i2, hi3);
                int lo = 0, hi = _quadKeys.Length, first = Key(i0, i1, i2, lo3);
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (_quadKeys[mid] < first)
                        lo = mid + 1;
                    else
                        hi = mid;
                }

                for (int q = lo; q < _quadKeys.Length && _quadKeys[q] <= last; q++)
                {
                    int c = q * 4;
                    if (Math.Abs(QuadCodes[c] - c0) <= tolerance && Math.Abs(QuadCodes[c + 1] - c1) <= tolerance &&
                        Math.Abs(QuadCodes[c + 2] - c2) <= tolerance && Math.Abs(QuadCodes[c + 3] - c3) <= tolerance)
                        results.Add(q);
                }
            }
        }

        /// <summary>Appends the indices of all catalogue stars within <paramref name="radiusRad"/> of a direction.</summary>
        internal void FindStars(Vec3 direction, double radiusRad, List<int> results)
        {
            if (!_plane.Project(direction, out double x, out double y))
                return;
            double reach = Math.Tan(radiusRad) * _stretch;
            double cosRadius = Math.Cos(radiusRad);
            int x0 = _starGrid.CellOf(x - reach), x1 = _starGrid.CellOf(x + reach);
            int y0 = _starGrid.CellOf(y - reach), y1 = _starGrid.CellOf(y + reach);

            for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                int cell = cy * _starGrid.Size + cx;
                for (int i = _starGrid.Start[cell]; i < _starGrid.Start[cell + 1]; i++)
                {
                    int star = _starGrid.Items[i];
                    if (StarDirection[star].Dot(direction) >= cosRadius)
                        results.Add(star);
                }
            }
        }
    }

    /// <summary>Points on a tangent plane bucketed into square cells.</summary>
    sealed class PlaneGrid
    {
        public readonly int Size;
        /// <summary>Items of cell c are Items[Start[c] .. Start[c + 1]).</summary>
        public readonly int[] Start, Items;
        readonly double _half, _cell;

        public PlaneGrid(double[] xs, double[] ys, int count, double half, double cell)
        {
            _half = half;
            _cell = cell;
            Size = Math.Max(1, (int)Math.Ceiling(2 * half / cell));

            Start = new int[Size * Size + 1];
            var cellOf = new int[count];
            for (int i = 0; i < count; i++)
            {
                cellOf[i] = CellOf(ys[i]) * Size + CellOf(xs[i]);
                Start[cellOf[i] + 1]++;
            }
            for (int c = 0; c < Size * Size; c++)
                Start[c + 1] += Start[c];

            Items = new int[count];
            var next = new int[Size * Size];
            for (int i = 0; i < count; i++)
                Items[Start[cellOf[i]] + next[cellOf[i]]++] = i;
        }

        public int CellOf(double coordinate)
        {
            return Math.Max(0, Math.Min(Size - 1, (int)Math.Floor((coordinate + _half) / _cell)));
        }
    }

    /// <summary>
    /// Scale- and rotation-invariant description of four stars. A and B, the most distant pair, define
    /// a frame where A = (0, 0) and B = (1, 1); the code is the position of C and D in that frame.
    /// </summary>
    static class QuadCode
    {
        /// <summary>Position of P in the frame of A and B.</summary>
        public static void Raw(double ax, double ay, double bx, double by, double px, double py,
            out double x, out double y)
        {
            double ux = bx - ax, uy = by - ay;
            double norm = ux * ux + uy * uy;
            double vx = px - ax, vy = py - ay;
            double re = (vx * ux + vy * uy) / norm;
            double im = (vy * ux - vx * uy) / norm;
            x = re - im;
            y = re + im;
        }

        /// <summary>Applies an A/B swap to raw coordinates.</summary>
        public static void SwapAB(ref double xc, ref double yc, ref double xd, ref double yd)
        {
            xc = 1 - xc;
            yc = 1 - yc;
            xd = 1 - xd;
            yd = 1 - yd;
        }

        public static void SwapCD(ref double xc, ref double yc, ref double xd, ref double yd)
        {
            double t = xc;
            xc = xd;
            xd = t;
            t = yc;
            yc = yd;
            yd = t;
        }

        /// <summary>Picks the one labelling of the four stars with xC + xD ≤ 1 and xC ≤ xD.</summary>
        public static void Canonicalize(ref double xc, ref double yc, ref double xd, ref double yd,
            out bool swapAB, out bool swapCD)
        {
            swapAB = xc + xd > 1;
            if (swapAB)
                SwapAB(ref xc, ref yc, ref xd, ref yd);
            swapCD = xc > xd;
            if (swapCD)
                SwapCD(ref xc, ref yc, ref xd, ref yd);
        }
    }
}
