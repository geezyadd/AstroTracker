using System;
using System.Collections.Generic;

namespace AstroTracker.PlateSolving
{
    /// <summary>Single-channel image, row 0 at the top. May be a binned copy of the camera frame.</summary>
    public sealed class GrayImage
    {
        public readonly float[] Pixels;
        public readonly int Width, Height;
        /// <summary>How many original pixels one pixel of this image covers along each axis.</summary>
        public readonly int Binning;

        public GrayImage(float[] pixels, int width, int height, int binning = 1)
        {
            if (pixels.Length != width * height)
                throw new ArgumentException("Pixel buffer does not match the image size");
            Pixels = pixels;
            Width = width;
            Height = height;
            Binning = binning;
        }

        /// <summary>
        /// Averages the first three channels of an interleaved 8-bit buffer over binning × binning blocks.
        /// </summary>
        /// <param name="bottomUp">True when the first row of the buffer is the bottom of the picture.</param>
        public static GrayImage FromRgb(ReadOnlySpan<byte> data, int width, int height, int bytesPerPixel,
            int binning, bool bottomUp)
        {
            int outWidth = width / binning, outHeight = height / binning;
            var pixels = new float[outWidth * outHeight];
            float norm = 1f / (3 * binning * binning);

            for (int oy = 0; oy < outHeight; oy++)
            {
                for (int by = 0; by < binning; by++)
                {
                    int sy = oy * binning + by;
                    int row = (bottomUp ? height - 1 - sy : sy) * width * bytesPerPixel;
                    for (int ox = 0; ox < outWidth; ox++)
                    {
                        int p = row + ox * binning * bytesPerPixel;
                        int sum = 0;
                        for (int bx = 0; bx < binning; bx++, p += bytesPerPixel)
                            sum += data[p] + data[p + 1] + data[p + 2];
                        pixels[oy * outWidth + ox] += sum * norm;
                    }
                }
            }
            return new GrayImage(pixels, outWidth, outHeight, binning);
        }
    }

    public struct DetectedStar
    {
        /// <summary>Centroid in original (unbinned) pixels; (0, 0) is the centre of the top-left pixel.</summary>
        public float X, Y;
        public float Flux;
        public float Peak;
        public int Area;
    }

    public sealed class StarDetectorOptions
    {
        /// <summary>Size of the background estimation tiles, in pixels of the image given to the detector.</summary>
        public int TileSize = 64;
        /// <summary>Detection threshold in units of the background noise.</summary>
        public float Threshold = 4f;
        /// <summary>Lower bound for the noise estimate, for frames whose sky is clipped to black.</summary>
        public float MinNoise = 0.5f;
        public int MinArea = 5;
        public int MaxArea = 5000;
        public int MaxStars = 200;
    }

    public static class StarDetector
    {
        /// <summary>Finds stars and returns them brightest first.</summary>
        public static List<DetectedStar> Detect(GrayImage image, StarDetectorOptions options = null)
        {
            options ??= new StarDetectorOptions();
            int w = image.Width, h = image.Height;

            float[] values = Smooth(image.Pixels, w, h);
            byte[] mask = SubtractBackground(values, w, h, options);

            var stars = new List<DetectedStar>();
            var stack = new int[1024];
            for (int seed = 0; seed < mask.Length; seed++)
            {
                if (mask[seed] == 0)
                    continue;

                int top = 0, area = 0;
                double sum = 0, sumX = 0, sumY = 0;
                float peak = 0;
                stack[top++] = seed;
                mask[seed] = 0;

                while (top > 0)
                {
                    int p = stack[--top];
                    int px = p % w, py = p / w;
                    float v = values[p];
                    area++;
                    sum += v;
                    sumX += v * px;
                    sumY += v * py;
                    if (v > peak)
                        peak = v;

                    for (int ny = Math.Max(0, py - 1); ny <= Math.Min(h - 1, py + 1); ny++)
                    {
                        for (int nx = Math.Max(0, px - 1); nx <= Math.Min(w - 1, px + 1); nx++)
                        {
                            int q = ny * w + nx;
                            if (mask[q] == 0)
                                continue;
                            mask[q] = 0;
                            if (top == stack.Length)
                                Array.Resize(ref stack, stack.Length * 2);
                            stack[top++] = q;
                        }
                    }
                }

                if (area < options.MinArea || area > options.MaxArea)
                    continue;

                int bin = image.Binning;
                stars.Add(new DetectedStar
                {
                    X = (float)((sumX / sum + 0.5) * bin - 0.5),
                    Y = (float)((sumY / sum + 0.5) * bin - 0.5),
                    Flux = (float)sum,
                    Peak = peak,
                    Area = area,
                });
            }

            stars.Sort((a, b) => b.Flux.CompareTo(a.Flux));
            if (stars.Count > options.MaxStars)
                stars.RemoveRange(options.MaxStars, stars.Count - options.MaxStars);
            return stars;
        }

        /// <summary>3×3 binomial blur; suppresses single-pixel noise before thresholding.</summary>
        static float[] Smooth(float[] src, int w, int h)
        {
            var tmp = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float left = src[row + Math.Max(0, x - 1)], right = src[row + Math.Min(w - 1, x + 1)];
                    tmp[row + x] = (left + 2 * src[row + x] + right) * 0.25f;
                }
            }

            var dst = new float[src.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w, up = Math.Max(0, y - 1) * w, down = Math.Min(h - 1, y + 1) * w;
                for (int x = 0; x < w; x++)
                    dst[row + x] = (tmp[up + x] + 2 * tmp[row + x] + tmp[down + x]) * 0.25f;
            }
            return dst;
        }

        /// <summary>
        /// Replaces the values with their excess over the local sky and returns a mask of the pixels
        /// that stand out of the noise.
        /// </summary>
        static byte[] SubtractBackground(float[] values, int w, int h, StarDetectorOptions options)
        {
            int tile = options.TileSize;
            int nx = Math.Max(1, (w + tile - 1) / tile), ny = Math.Max(1, (h + tile - 1) / tile);
            var sky = new float[nx * ny];
            var noise = new float[nx * ny];
            var samples = new float[(tile / 2 + 1) * (tile / 2 + 1)];

            for (int ty = 0; ty < ny; ty++)
            {
                for (int tx = 0; tx < nx; tx++)
                {
                    int count = 0;
                    int yEnd = Math.Min(h, (ty + 1) * tile), xEnd = Math.Min(w, (tx + 1) * tile);
                    for (int y = ty * tile; y < yEnd; y += 2)
                        for (int x = tx * tile; x < xEnd; x += 2)
                            samples[count++] = values[y * w + x];

                    float median = Median(samples, count);
                    for (int i = 0; i < count; i++)
                        samples[i] = Math.Abs(samples[i] - median);
                    sky[ty * nx + tx] = median;
                    noise[ty * nx + tx] = Math.Max(options.MinNoise, 1.4826f * Median(samples, count));
                }
            }

            // A tile dominated by a bright star or a nebula takes the level of its neighbours.
            sky = MedianFilter(sky, nx, ny);
            noise = MedianFilter(noise, nx, ny);

            var x0 = new int[w];
            var x1 = new int[w];
            var fx = new float[w];
            for (int x = 0; x < w; x++)
                TilePosition(x, tile, nx, out x0[x], out x1[x], out fx[x]);

            var mask = new byte[values.Length];
            for (int y = 0; y < h; y++)
            {
                TilePosition(y, tile, ny, out int y0, out int y1, out float fy);
                int r0 = y0 * nx, r1 = y1 * nx, row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float f = fx[x];
                    int a = x0[x], b = x1[x];
                    float skyHere = Lerp(Lerp(sky[r0 + a], sky[r0 + b], f), Lerp(sky[r1 + a], sky[r1 + b], f), fy);
                    float noiseHere = Lerp(Lerp(noise[r0 + a], noise[r0 + b], f),
                        Lerp(noise[r1 + a], noise[r1 + b], f), fy);

                    float v = values[row + x] - skyHere;
                    values[row + x] = v;
                    if (v > options.Threshold * noiseHere)
                        mask[row + x] = 1;
                }
            }
            return mask;
        }

        /// <summary>Neighbouring tile centres around a pixel and the interpolation weight between them.</summary>
        static void TilePosition(int pixel, int tile, int tileCount, out int lower, out int upper, out float t)
        {
            float g = (pixel + 0.5f) / tile - 0.5f;
            g = Math.Max(0f, Math.Min(tileCount - 1, g));
            lower = (int)g;
            upper = Math.Min(tileCount - 1, lower + 1);
            t = g - lower;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Median(float[] buffer, int count)
        {
            Array.Sort(buffer, 0, count);
            return buffer[count / 2];
        }

        static float[] MedianFilter(float[] src, int nx, int ny)
        {
            var dst = new float[src.Length];
            var window = new float[9];
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int count = 0;
                    for (int yy = Math.Max(0, y - 1); yy <= Math.Min(ny - 1, y + 1); yy++)
                        for (int xx = Math.Max(0, x - 1); xx <= Math.Min(nx - 1, x + 1); xx++)
                            window[count++] = src[yy * nx + xx];
                    dst[y * nx + x] = Median(window, count);
                }
            }
            return dst;
        }
    }
}
