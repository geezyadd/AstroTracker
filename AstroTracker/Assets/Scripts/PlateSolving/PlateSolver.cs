using System;
using System.Collections.Generic;

namespace AstroTracker.PlateSolving
{
    public sealed class PlateSolverOptions
    {
        /// <summary>Expected scale of the original (unbinned) frame.</summary>
        public double PixelScaleArcsec = 1.37;
        /// <summary>Allowed relative error of the expected scale.</summary>
        public double ScaleTolerance = 0.1;
        /// <summary>How many of the brightest detected stars are combined into quads.</summary>
        public int MaxQuadStars = 30;
        /// <summary>How many of the brightest detected stars are checked against the catalogue.</summary>
        public int MaxVerifyStars = 80;
        public double CodeTolerance = 0.01;
        /// <summary>How far, in original pixels, a detected star may be from its predicted position.</summary>
        public double MatchRadiusPixels = 5;
        /// <summary>Catalogue stars that must line up with detected ones before a solution is accepted.</summary>
        public int MinMatches = 8;
    }

    public sealed class PlateSolution
    {
        /// <summary>Sky position of the frame centre, degrees, at the catalogue epoch.</summary>
        public double RaDeg, DecDeg;
        public double PixelScaleArcsec;
        /// <summary>Position angle of the frame's up direction, degrees from north through east.</summary>
        public double RotationDeg;
        /// <summary>True when the frame is a mirror image of the sky.</summary>
        public bool Mirrored;
        public int MatchedStars;
        public double RmsArcsec;
        /// <summary>Quads looked up and candidate matches verified before the solution was found.</summary>
        public int QuadsTried, CandidatesVerified;

        internal TangentPlane Plane;
        internal Affine Fit;
        internal double CenterX, CenterY;

        /// <summary>Sky direction of a point given in original pixels.</summary>
        public Vec3 PixelToSky(double x, double y)
        {
            Fit.Apply(x - CenterX, y - CenterY, out double xi, out double eta);
            return Plane.Unproject(xi, eta);
        }

        /// <summary>Returns false when the direction is too far from the frame to project.</summary>
        public bool SkyToPixel(Vec3 direction, out double x, out double y)
        {
            x = y = 0;
            if (!Plane.Project(direction, out double xi, out double eta))
                return false;
            Fit.Invert(xi, eta, out double u, out double v);
            x = u + CenterX;
            y = v + CenterY;
            return true;
        }
    }

    /// <summary>Maps pixel offsets from the frame centre to tangent-plane coordinates.</summary>
    struct Affine
    {
        // xi = A u + B v + C, eta = D u + E v + F
        public double A, B, C, D, E, F;

        public double Determinant => A * E - B * D;

        public void Apply(double u, double v, out double xi, out double eta)
        {
            xi = A * u + B * v + C;
            eta = D * u + E * v + F;
        }

        public void Invert(double xi, double eta, out double u, out double v)
        {
            double det = Determinant, dx = xi - C, dy = eta - F;
            u = (E * dx - B * dy) / det;
            v = (A * dy - D * dx) / det;
        }
    }

    public static class PlateSolver
    {
        /// <summary>
        /// Finds where the frame points. Returns null when no arrangement of the detected stars matches the index.
        /// </summary>
        /// <param name="stars">Detected stars in original pixels, brightest first.</param>
        public static PlateSolution Solve(IReadOnlyList<DetectedStar> stars, int width, int height,
            SolverIndex index, PlateSolverOptions options = null)
        {
            var run = new Run(stars, width, height, index, options ?? new PlateSolverOptions());
            return run.Search();
        }

        sealed class Run
        {
            const int MaxPasses = 10;

            readonly SolverIndex _index;
            readonly PlateSolverOptions _options;
            readonly int _width, _height;
            readonly double[] _x, _y;
            readonly int _quadStars, _verifyStars;
            readonly double _scale;

            readonly List<int> _found = new List<int>();
            readonly List<int> _nearby = new List<int>();
            readonly List<int> _matchImage = new List<int>();
            readonly List<int> _matchCatalog = new List<int>();
            readonly int[] _image = new int[4];
            int _quadsTried, _candidates;

            public Run(IReadOnlyList<DetectedStar> stars, int width, int height, SolverIndex index,
                PlateSolverOptions options)
            {
                _index = index;
                _options = options;
                _width = width;
                _height = height;
                _scale = options.PixelScaleArcsec * Angle.ArcsecToRad;
                _verifyStars = Math.Min(stars.Count, Math.Max(options.MaxVerifyStars, options.MaxQuadStars));
                _quadStars = Math.Min(stars.Count, options.MaxQuadStars);

                // Offsets from the frame centre keep the fits well conditioned.
                _x = new double[_verifyStars];
                _y = new double[_verifyStars];
                for (int i = 0; i < _verifyStars; i++)
                {
                    _x[i] = stars[i].X - (width - 1) * 0.5;
                    _y[i] = stars[i].Y - (height - 1) * 0.5;
                }
            }

            public PlateSolution Search()
            {
                // Each new star is combined with the brighter ones, so the brightest quads are tried first.
                for (int d = 3; d < _quadStars; d++)
                for (int c = 2; c < d; c++)
                for (int b = 1; b < c; b++)
                for (int a = 0; a < b; a++)
                {
                    PlateSolution solution = TryQuad(a, b, c, d);
                    if (solution != null)
                        return solution;
                }
                return null;
            }

            PlateSolution TryQuad(int s0, int s1, int s2, int s3)
            {
                _image[0] = s0;
                _image[1] = s1;
                _image[2] = s2;
                _image[3] = s3;

                // A and B are the two stars farthest apart.
                int ia = 0, ib = 1;
                double longest = -1;
                for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                {
                    double dx = _x[_image[i]] - _x[_image[j]], dy = _y[_image[i]] - _y[_image[j]];
                    double d2 = dx * dx + dy * dy;
                    if (d2 > longest)
                    {
                        longest = d2;
                        ia = i;
                        ib = j;
                    }
                }

                double size = Math.Sqrt(longest) * _scale;
                double slack = 1 + _options.ScaleTolerance;
                if (size * slack < _index.MinQuadSize || size > _index.MaxQuadSize * slack)
                    return null;

                int ic = -1, id = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (i == ia || i == ib)
                        continue;
                    if (ic < 0)
                        ic = i;
                    else
                        id = i;
                }

                int a = _image[ia], b = _image[ib], c = _image[ic], d = _image[id];
                double mx = (_x[a] + _x[b]) * 0.5, my = (_y[a] + _y[b]) * 0.5, r2 = longest * 0.25;
                if (Sq(_x[c] - mx) + Sq(_y[c] - my) > r2 || Sq(_x[d] - mx) + Sq(_y[d] - my) > r2)
                    return null;

                QuadCode.Raw(_x[a], _y[a], _x[b], _y[b], _x[c], _y[c], out double xc, out double yc);
                QuadCode.Raw(_x[a], _y[a], _x[b], _y[b], _x[d], _y[d], out double xd, out double yd);
                _quadsTried++;

                // A mirrored frame swaps the two code axes; A and B stay where they are.
                PlateSolution solution = Lookup(a, b, c, d, xc, yc, xd, yd, size, false);
                return solution ?? Lookup(a, b, c, d, yc, xc, yd, xd, size, true);
            }

            PlateSolution Lookup(int a, int b, int c, int d, double xc, double yc, double xd, double yd,
                double size, bool mirrored)
            {
                double tolerance = _options.CodeTolerance;

                // Near the boundaries of the canonical labelling noise can flip it, so try both sides.
                for (int abVariant = 0; abVariant < 2; abVariant++)
                {
                    bool swapAB = xc + xd > 1;
                    if (abVariant == 1)
                    {
                        if (Math.Abs(xc + xd - 1) > 2 * tolerance)
                            break;
                        swapAB = !swapAB;
                    }
                    double x1 = xc, y1 = yc, x2 = xd, y2 = yd;
                    if (swapAB)
                        QuadCode.SwapAB(ref x1, ref y1, ref x2, ref y2);

                    for (int cdVariant = 0; cdVariant < 2; cdVariant++)
                    {
                        bool swapCD = x1 > x2;
                        if (cdVariant == 1)
                        {
                            if (Math.Abs(x1 - x2) > 2 * tolerance)
                                break;
                            swapCD = !swapCD;
                        }
                        double c0 = x1, c1 = y1, c2 = x2, c3 = y2;
                        if (swapCD)
                            QuadCode.SwapCD(ref c0, ref c1, ref c2, ref c3);

                        _found.Clear();
                        _index.FindQuads(c0, c1, c2, c3, tolerance, _found);
                        for (int i = 0; i < _found.Count; i++)
                        {
                            int quad = _found[i];
                            if (Math.Abs(size / _index.QuadSize[quad] - 1) > _options.ScaleTolerance)
                                continue;

                            _matchImage.Clear();
                            _matchCatalog.Clear();
                            _matchImage.Add(swapAB ? b : a);
                            _matchImage.Add(swapAB ? a : b);
                            _matchImage.Add(swapCD ? d : c);
                            _matchImage.Add(swapCD ? c : d);
                            for (int j = 0; j < 4; j++)
                                _matchCatalog.Add(_index.QuadStars[quad * 4 + j]);

                            _candidates++;
                            PlateSolution solution = Verify(mirrored);
                            if (solution != null)
                                return solution;
                        }
                    }
                }
                return null;
            }

            /// <summary>
            /// Starts from the four pairs in the match lists, predicts where the other catalogue stars
            /// should fall and accepts the hypothesis if enough detected stars are there.
            /// </summary>
            PlateSolution Verify(bool mirrored)
            {
                Vec3 sum = new Vec3(0, 0, 0);
                for (int i = 0; i < 4; i++)
                    sum += _index.StarDirection[_matchCatalog[i]];
                var plane = new TangentPlane(sum.Normalized());
                Affine fit = FitSimilarity(plane, mirrored);

                // The four quad stars pin the frame down only near the quad. Each pass refits on the
                // matches found so far, which brings stars farther out within reach of the next one.
                int required = Math.Max(4, _options.MinMatches), previous = 0;
                for (int pass = 0; pass < MaxPasses; pass++)
                {
                    double radius = _options.MatchRadiusPixels * (pass == 0 ? 2 : 1);
                    if (!Match(plane, fit, radius) || _matchImage.Count < Math.Min(required, 6))
                        return null;
                    bool settled = pass >= 2 && _matchImage.Count <= previous;
                    previous = _matchImage.Count;

                    // Re-centre the projection on the frame so the fit absorbs no projection error.
                    // A full affine fit is only trusted once there are enough stars to constrain it.
                    plane = new TangentPlane(plane.Unproject(fit.C, fit.F));
                    fit = _matchImage.Count >= 12 ? FitAffine(plane) : FitSimilarity(plane, mirrored);
                    if (settled)
                        break;
                }
                if (_matchImage.Count < required)
                    return null;

                double scale = Math.Sqrt(Math.Abs(fit.Determinant));
                if (Math.Abs(scale / _scale - 1) > _options.ScaleTolerance)
                    return null;

                double residual = 0;
                for (int i = 0; i < _matchImage.Count; i++)
                {
                    plane.Project(_index.StarDirection[_matchCatalog[i]], out double xi, out double eta);
                    fit.Apply(_x[_matchImage[i]], _y[_matchImage[i]], out double fx, out double fy);
                    residual += Sq(fx - xi) + Sq(fy - eta);
                }

                Vec3 center = plane.Unproject(fit.C, fit.F);
                double rotation = Math.Atan2(-fit.B, -fit.E) * Angle.RadToDeg;
                return new PlateSolution
                {
                    RaDeg = center.Ra * Angle.RadToDeg,
                    DecDeg = center.Dec * Angle.RadToDeg,
                    PixelScaleArcsec = scale * Angle.RadToArcsec,
                    RotationDeg = rotation < 0 ? rotation + 360 : rotation,
                    Mirrored = fit.Determinant < 0,
                    MatchedStars = _matchImage.Count,
                    RmsArcsec = Math.Sqrt(residual / _matchImage.Count) * Angle.RadToArcsec,
                    QuadsTried = _quadsTried,
                    CandidatesVerified = _candidates,
                    Plane = plane,
                    Fit = fit,
                    CenterX = (_width - 1) * 0.5,
                    CenterY = (_height - 1) * 0.5,
                };
            }

            /// <summary>Rebuilds the match lists: every detected star paired with the catalogue star predicted nearest to it.</summary>
            bool Match(TangentPlane plane, Affine fit, double radiusPixels)
            {
                double det = fit.Determinant;
                if (Math.Abs(det) < 1e-18)
                    return false;

                double scale = Math.Sqrt(Math.Abs(det));
                double halfDiagonal = 0.5 * Math.Sqrt((double)_width * _width + (double)_height * _height);
                Vec3 center = plane.Unproject(fit.C, fit.F);
                _nearby.Clear();
                _index.FindStars(center, (halfDiagonal + radiusPixels) * scale, _nearby);

                int count = _nearby.Count;
                if (count == 0)
                    return false;
                var u = new double[count];
                var v = new double[count];
                var owner = new int[count];
                var ownerDistance = new double[count];
                for (int i = 0; i < count; i++)
                {
                    owner[i] = -1;
                    if (plane.Project(_index.StarDirection[_nearby[i]], out double xi, out double eta))
                        fit.Invert(xi, eta, out u[i], out v[i]);
                    else
                        u[i] = v[i] = double.MaxValue;
                }

                double limit = radiusPixels * radiusPixels;
                for (int s = 0; s < _verifyStars; s++)
                {
                    int best = -1;
                    double bestDistance = limit;
                    for (int i = 0; i < count; i++)
                    {
                        double d2 = Sq(u[i] - _x[s]) + Sq(v[i] - _y[s]);
                        if (d2 <= bestDistance)
                        {
                            bestDistance = d2;
                            best = i;
                        }
                    }
                    // A catalogue star goes to the nearest of the detected stars that claim it.
                    if (best >= 0 && (owner[best] < 0 || bestDistance < ownerDistance[best]))
                    {
                        owner[best] = s;
                        ownerDistance[best] = bestDistance;
                    }
                }

                _matchImage.Clear();
                _matchCatalog.Clear();
                for (int i = 0; i < count; i++)
                {
                    if (owner[i] < 0)
                        continue;
                    _matchImage.Add(owner[i]);
                    _matchCatalog.Add(_nearby[i]);
                }
                return true;
            }

            /// <summary>Least-squares rotation, scale and shift (optionally mirrored) through the matched pairs.</summary>
            Affine FitSimilarity(TangentPlane plane, bool mirrored)
            {
                int n = _matchImage.Count;
                double sign = mirrored ? -1 : 1;
                double mzx = 0, mzy = 0, mwx = 0, mwy = 0;
                var wx = new double[n];
                var wy = new double[n];
                for (int i = 0; i < n; i++)
                {
                    plane.Project(_index.StarDirection[_matchCatalog[i]], out wx[i], out wy[i]);
                    mzx += _x[_matchImage[i]];
                    mzy += sign * _y[_matchImage[i]];
                    mwx += wx[i];
                    mwy += wy[i];
                }
                mzx /= n;
                mzy /= n;
                mwx /= n;
                mwy /= n;

                // Complex form: w = alpha * z + gamma, with z conjugated for a mirrored frame.
                double re = 0, im = 0, norm = 0;
                for (int i = 0; i < n; i++)
                {
                    double zx = _x[_matchImage[i]] - mzx, zy = sign * _y[_matchImage[i]] - mzy;
                    double dx = wx[i] - mwx, dy = wy[i] - mwy;
                    re += dx * zx + dy * zy;
                    im += dy * zx - dx * zy;
                    norm += zx * zx + zy * zy;
                }
                re /= norm;
                im /= norm;

                return new Affine
                {
                    A = re,
                    B = -im * sign,
                    C = mwx - (re * mzx - im * mzy),
                    D = im,
                    E = re * sign,
                    F = mwy - (im * mzx + re * mzy),
                };
            }

            /// <summary>Least-squares affine transform through the matched pairs.</summary>
            Affine FitAffine(TangentPlane plane)
            {
                int n = _matchImage.Count;
                double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0;
                double bx0 = 0, bx1 = 0, bx2 = 0, by0 = 0, by1 = 0, by2 = 0;
                for (int i = 0; i < n; i++)
                {
                    double x = _x[_matchImage[i]], y = _y[_matchImage[i]];
                    plane.Project(_index.StarDirection[_matchCatalog[i]], out double xi, out double eta);
                    sxx += x * x;
                    sxy += x * y;
                    syy += y * y;
                    sx += x;
                    sy += y;
                    bx0 += x * xi;
                    bx1 += y * xi;
                    bx2 += xi;
                    by0 += x * eta;
                    by1 += y * eta;
                    by2 += eta;
                }

                // Normal equations with the symmetric matrix [sxx sxy sx; sxy syy sy; sx sy n], solved by cofactors.
                double c00 = syy * n - sy * sy, c01 = sx * sy - sxy * n, c02 = sxy * sy - syy * sx;
                double c11 = sxx * n - sx * sx, c12 = sxy * sx - sxx * sy, c22 = sxx * syy - sxy * sxy;
                double det = sxx * c00 + sxy * c01 + sx * c02;

                return new Affine
                {
                    A = (c00 * bx0 + c01 * bx1 + c02 * bx2) / det,
                    B = (c01 * bx0 + c11 * bx1 + c12 * bx2) / det,
                    C = (c02 * bx0 + c12 * bx1 + c22 * bx2) / det,
                    D = (c00 * by0 + c01 * by1 + c02 * by2) / det,
                    E = (c01 * by0 + c11 * by1 + c12 * by2) / det,
                    F = (c02 * by0 + c12 * by1 + c22 * by2) / det,
                };
            }

            static double Sq(double v) => v * v;
        }
    }
}
