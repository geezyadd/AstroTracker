using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace AstroTracker
{
    /// <summary>Overlay that lists the photos on the connected camera and lets the user take one of them.</summary>
    sealed class CameraPanel
    {
        const int MaxPhotos = 40;

        readonly VisualElement _panel, _list;
        readonly Label _title, _status;
        readonly Button _refresh, _close;
        readonly Action<string, string> _picked;
        readonly List<Texture2D> _thumbnails = new List<Texture2D>();
        bool _busy;
        Func<string> _statusText;

        /// <param name="picked">Receives the local path and the name of the photo the user chose.</param>
        public CameraPanel(VisualElement root, Action<string, string> picked)
        {
            _picked = picked;
            _panel = root.Q<VisualElement>("camera-panel");
            _list = root.Q<VisualElement>("camera-list");
            _title = root.Q<Label>("camera-title");
            _status = root.Q<Label>("camera-status");
            _refresh = root.Q<Button>("camera-refresh");
            _close = root.Q<Button>("camera-close");

            _refresh.clickable = new Clickable(Reload);
            _close.clickable = new Clickable(Close);
            _panel.style.display = DisplayStyle.None;
            ShowStatus(null);
        }

        public void Open()
        {
            _panel.style.display = DisplayStyle.Flex;
            Reload();
        }

        public void Close()
        {
            _panel.style.display = DisplayStyle.None;
            ClearList();
        }

        public void ApplyLanguage()
        {
            _title.text = Localization.Get("cameraTitle");
            _refresh.text = Localization.Get("cameraRefresh");
            _close.text = Localization.Get("cameraClose");
            _status.text = _statusText?.Invoke() ?? "";
        }

        void ShowStatus(Func<string> text)
        {
            _statusText = text;
            _status.text = text?.Invoke() ?? "";
            _status.style.display = text == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            _refresh.SetEnabled(!busy);
            _list.SetEnabled(!busy);
        }

        void ClearList()
        {
            _list.Clear();
            foreach (Texture2D texture in _thumbnails)
                UnityEngine.Object.Destroy(texture);
            _thumbnails.Clear();
        }

        void Reload()
        {
            if (_busy)
                return;
            ClearList();
            SetBusy(true);
            ShowStatus(() => Localization.Get("cameraLoading"));
            CameraLink.ListPhotos(MaxPhotos,
                photos =>
                {
                    SetBusy(false);
                    ShowStatus(null);
                    foreach (CameraPhoto photo in photos)
                        _list.Add(Row(photo));
                },
                reason =>
                {
                    SetBusy(false);
                    ShowStatus(() => ErrorText(reason));
                });
        }

        VisualElement Row(CameraPhoto photo)
        {
            var row = new Button(() => Download(photo));
            row.AddToClassList("photo-row");

            var thumbnail = new VisualElement();
            thumbnail.AddToClassList("photo-thumbnail");
            if (photo.ThumbnailPath != null && File.Exists(photo.ThumbnailPath))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (texture.LoadImage(File.ReadAllBytes(photo.ThumbnailPath), true))
                {
                    _thumbnails.Add(texture);
                    thumbnail.style.backgroundImage = new StyleBackground(texture);
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
            row.Add(thumbnail);

            var name = new Label(photo.Name);
            name.AddToClassList("photo-name");
            var details = new Label(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss}  ·  {1:F1} MB",
                photo.Taken, photo.SizeBytes / 1e6));
            details.AddToClassList("photo-details");

            var text = new VisualElement();
            text.AddToClassList("photo-text");
            text.Add(name);
            text.Add(details);
            row.Add(text);
            return row;
        }

        void Download(CameraPhoto photo)
        {
            if (_busy)
                return;
            SetBusy(true);
            ShowStatus(() => string.Format(Localization.Get("cameraDownloading"), photo.Name));
            CameraLink.Download(photo,
                path =>
                {
                    SetBusy(false);
                    ShowStatus(null);
                    Close();
                    _picked(path, photo.Name);
                },
                reason =>
                {
                    SetBusy(false);
                    ShowStatus(() => ErrorText(reason));
                });
        }

        static string ErrorText(string reason)
        {
            switch (reason)
            {
                case CameraLink.NoCamera: return Localization.Get("cameraNone");
                case CameraLink.PermissionDenied: return Localization.Get("cameraDenied");
                case CameraLink.OpenFailed: return Localization.Get("cameraOpenFailed");
                case CameraLink.NoPhotos: return Localization.Get("cameraEmpty");
                case CameraLink.DownloadFailed: return Localization.Get("cameraDownloadFailed");
                case CameraLink.Unsupported: return Localization.Get("cameraUnsupported");
                default: return Localization.Get("cameraError");
            }
        }
    }
}
