using System;
using AstroTracker.PlateSolving;
using Unity.Collections;
using UnityEngine;

namespace AstroTracker
{
    public static class FrameImage
    {
        /// <summary>Decodes a JPG or PNG camera frame into a binned grayscale image for the star detector.</summary>
        /// <param name="width">Width of the original frame in pixels.</param>
        /// <param name="height">Height of the original frame in pixels.</param>
        public static GrayImage Decode(byte[] encoded, int binning, out int width, out int height)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(encoded, false))
                    throw new ArgumentException("Not a JPG or PNG image");
                width = texture.width;
                height = texture.height;

                // Texture rows run bottom to top.
                if (texture.format == TextureFormat.RGB24 || texture.format == TextureFormat.RGBA32)
                {
                    NativeArray<byte> raw = texture.GetRawTextureData<byte>();
                    int bytesPerPixel = texture.format == TextureFormat.RGB24 ? 3 : 4;
                    return GrayImage.FromRgb(raw.AsReadOnlySpan(), width, height, bytesPerPixel, binning, true);
                }

                Color32[] colors = texture.GetPixels32();
                var rgb = new byte[colors.Length * 3];
                for (int i = 0; i < colors.Length; i++)
                {
                    rgb[i * 3] = colors[i].r;
                    rgb[i * 3 + 1] = colors[i].g;
                    rgb[i * 3 + 2] = colors[i].b;
                }
                return GrayImage.FromRgb(rgb, width, height, 3, binning, true);
            }
            finally
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(texture);
                else
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
