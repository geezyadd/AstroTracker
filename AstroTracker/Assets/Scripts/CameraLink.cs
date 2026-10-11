using System;
using System.Collections.Generic;
using System.Globalization;
#if UNITY_EDITOR
using System.IO;
using System.Linq;
#elif UNITY_ANDROID
using System.Threading;
using UnityEngine;
using UnityEngine.Scripting;
#endif

namespace AstroTracker
{
    public sealed class CameraPhoto
    {
        public int Handle;
        public string Name;
        /// <summary>Camera clock time of the shot.</summary>
        public DateTime Taken;
        public long SizeBytes;
        /// <summary>Small JPG preview on disk, or null when the camera gave none.</summary>
        public string ThumbnailPath;
    }

    /// <summary>
    /// Photos on a camera connected by USB. Works on Android through CameraUsb.java; in the editor it
    /// lists a memory card's DCIM folder instead, so the screen can be tried without a tablet.
    /// </summary>
    public static class CameraLink
    {
        /// <summary>Failure reasons reported to the error callbacks.</summary>
        public const string NoCamera = "no_camera", PermissionDenied = "permission_denied", OpenFailed = "open_failed",
            NoPhotos = "no_photos", DownloadFailed = "download_failed", Unsupported = "unsupported";

        public static bool Supported
        {
            get
            {
#if UNITY_EDITOR || UNITY_ANDROID
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Newest JPG photos, newest first. Callbacks arrive on the main thread.</summary>
        public static void ListPhotos(int limit, Action<List<CameraPhoto>> onDone, Action<string> onError)
        {
#if UNITY_EDITOR
            string card = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady && drive.DriveType != DriveType.Network)
                .Select(drive => Path.Combine(drive.RootDirectory.FullName, "DCIM"))
                .FirstOrDefault(Directory.Exists);
            if (card == null)
            {
                onError(NoCamera);
                return;
            }

            _editorFiles = new DirectoryInfo(card).GetFiles("*.jpg", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTime).Take(limit).ToList();
            if (_editorFiles.Count == 0)
            {
                onError(NoPhotos);
                return;
            }
            onDone(_editorFiles.Select((file, index) => new CameraPhoto
            {
                Handle = index, Name = file.Name, Taken = file.LastWriteTime, SizeBytes = file.Length,
            }).ToList());
#elif UNITY_ANDROID
            Call("listPhotos", limit, data => onDone(Parse(data)), onError);
#else
            onError(Unsupported);
#endif
        }

        /// <summary>Copies a photo to local storage and reports the path of the copy.</summary>
        public static void Download(CameraPhoto photo, Action<string> onDone, Action<string> onError)
        {
#if UNITY_EDITOR
            if (_editorFiles != null && photo.Handle < _editorFiles.Count && _editorFiles[photo.Handle].Exists)
                onDone(_editorFiles[photo.Handle].FullName);
            else
                onError(DownloadFailed);
#elif UNITY_ANDROID
            Call("download", photo.Handle, onDone, onError);
#else
            onError(Unsupported);
#endif
        }

        /// <summary>Reads the tab-separated listing produced by CameraUsb.listPhotos.</summary>
        public static List<CameraPhoto> Parse(string listing)
        {
            var photos = new List<CameraPhoto>();
            foreach (string line in listing.Split('\n'))
            {
                string[] columns = line.Split('\t');
                if (columns.Length < 5 || !int.TryParse(columns[0], out int handle))
                    continue;

                long.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds);
                long.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size);
                photos.Add(new CameraPhoto
                {
                    Handle = handle,
                    Name = columns[1],
                    // Cameras keep local time without a zone; show it as the camera does.
                    Taken = new DateTime(1970, 1, 1).AddMilliseconds(milliseconds),
                    SizeBytes = size,
                    ThumbnailPath = columns[4].Length > 0 ? columns[4] : null,
                });
            }
            return photos;
        }

#if UNITY_EDITOR
        static List<FileInfo> _editorFiles;
#elif UNITY_ANDROID
        static void Call(string method, int argument, Action<string> onDone, Action<string> onError)
        {
            try
            {
                var callback = new Callback(SynchronizationContext.Current, onDone, onError);
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var camera = new AndroidJavaClass("com.astrotracker.CameraUsb");
                camera.CallStatic(method, activity, argument, callback);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                onError("error");
            }
        }

        /// <summary>Receives the result from CameraUsb.java on a Java thread.</summary>
        sealed class Callback : AndroidJavaProxy
        {
            readonly SynchronizationContext _mainThread;
            readonly Action<string> _onDone, _onError;

            public Callback(SynchronizationContext mainThread, Action<string> onDone, Action<string> onError)
                : base("com.astrotracker.CameraUsb$Callback")
            {
                _mainThread = mainThread;
                _onDone = onDone;
                _onError = onError;
            }

            [Preserve]
            void onResult(string data) => _mainThread.Post(_ => _onDone(data), null);

            [Preserve]
            void onFailed(string reason) => _mainThread.Post(_ => _onError(reason), null);
        }
#endif
    }
}
