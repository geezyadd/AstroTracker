using System;
using System.IO;
#if !UNITY_EDITOR && UNITY_ANDROID
using System.Threading;
using UnityEngine;
using UnityEngine.Scripting;
#elif !UNITY_EDITOR && UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

namespace AstroTracker
{
    /// <summary>Lets the user choose a camera frame: the system picker on Android, a file dialog on a PC.</summary>
    public static class ImagePicker
    {
        /// <summary>Path of a readable image file and the name to show for it.</summary>
        public delegate void PickedHandler(string path, string displayName);

        /// <summary>
        /// Both callbacks arrive on the main thread. <paramref name="onPicked"/> is not called when the
        /// user closes the picker without choosing.
        /// </summary>
        public static void Pick(PickedHandler onPicked, Action<string> onError)
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("Camera frame", "", "jpg,jpeg,png");
            if (!string.IsNullOrEmpty(path))
                onPicked(path, Path.GetFileName(path));
#elif UNITY_ANDROID
            try
            {
                var callback = new PickCallback(SynchronizationContext.Current, onPicked, onError);
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var picker = new AndroidJavaClass("com.astrotracker.ImagePickerFragment");
                picker.CallStatic("pick", activity, callback);
            }
            catch (Exception e)
            {
                onError(e.Message);
            }
#elif UNITY_STANDALONE_WIN
            var dialog = new OpenFileName
            {
                filter = "Images\0*.jpg;*.jpeg;*.png\0All files\0*.*\0",
                file = new string('\0', MaxPath),
                maxFile = MaxPath,
                title = "Camera frame",
                flags = ExplorerStyle | FileMustExist | PathMustExist | NoChangeDir,
            };
            dialog.structSize = Marshal.SizeOf(dialog);
            if (GetOpenFileName(dialog))
            {
                string path = dialog.file.TrimEnd('\0');
                onPicked(path, Path.GetFileName(path));
            }
#else
            onError("Choosing a file is not supported on this platform");
#endif
        }

#if !UNITY_EDITOR && UNITY_ANDROID
        /// <summary>Receives the result from ImagePickerFragment.java on a Java thread.</summary>
        sealed class PickCallback : AndroidJavaProxy
        {
            readonly SynchronizationContext _mainThread;
            readonly PickedHandler _onPicked;
            readonly Action<string> _onError;

            public PickCallback(SynchronizationContext mainThread, PickedHandler onPicked, Action<string> onError)
                : base("com.astrotracker.ImagePickerFragment$Callback")
            {
                _mainThread = mainThread;
                _onPicked = onPicked;
                _onError = onError;
            }

            [Preserve]
            void onPicked(string path, string name)
            {
                if (!string.IsNullOrEmpty(path))
                    _mainThread.Post(_ => _onPicked(path, name), null);
            }

            [Preserve]
            void onFailed(string message)
            {
                _mainThread.Post(_ => _onError(message), null);
            }
        }
#elif !UNITY_EDITOR && UNITY_STANDALONE_WIN
        const int MaxPath = 1024;
        const int NoChangeDir = 0x00000008, PathMustExist = 0x00000800, FileMustExist = 0x00001000,
            ExplorerStyle = 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        sealed class OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public string filter;
            public string customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public string templateName;
            public IntPtr reserved;
            public int reserved2;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileName([In, Out] OpenFileName dialog);
#endif
    }
}
