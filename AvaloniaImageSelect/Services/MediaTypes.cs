using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AvaloniaImageSelect.Services
{
    /// <summary>
    /// 预览内容的类型。
    /// </summary>
    public enum MediaKind
    {
        /// <summary>普通静态图片（Avalonia 可直接解码）。</summary>
        Image,

        /// <summary>需要 Magick.NET 解码的静态图片（HEIC/HEIF/WEBP 等）。</summary>
        MagickImage,

        /// <summary>多帧动图（GIF / 动态 WEBP / APNG / MNG）。</summary>
        AnimatedImage,

        /// <summary>视频文件（MP4 / MOV / M4V / WEBM）。</summary>
        Video,

        /// <summary>动态照片：JPG 本身是静态图，但内嵌或伴随一段视频（实况照片 / Motion Photo）。</summary>
        MotionPhoto,
    }

    /// <summary>
    /// 程序支持的文件格式与类型判断。
    /// </summary>
    public static class MediaTypes
    {
        /// <summary>Avalonia 可直接解码的静态图片。</summary>
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".JPG", ".JPEG", ".PNG", ".BMP",
        };

        /// <summary>需要交给 Magick.NET 解码的图片（Avalonia 不认识这些格式）。</summary>
        private static readonly HashSet<string> MagickImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".HEIC", ".HEIF", ".HIF", ".AVIF", ".WEBP",
        };

        /// <summary>
        /// 可能是多帧动图的格式，需要读取后判断实际帧数。
        /// PNG 也包含在内，因为 APNG（动态 PNG）与普通 PNG 使用相同的扩展名。
        /// </summary>
        private static readonly HashSet<string> AnimatedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".GIF", ".APNG", ".PNG", ".MNG",
        };

        /// <summary>视频格式，需要 FFmpeg 抽帧后才能预览。</summary>
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".MP4", ".MOV", ".M4V", ".WEBM",
        };

        /// <summary>扫描目录时接受的全部扩展名。</summary>
        public static readonly string[] AllSupportedExtensions = ImageExtensions
            .Concat(MagickImageExtensions)
            .Concat(AnimatedImageExtensions)
            .Concat(VideoExtensions)
            .ToArray();

        public static bool IsSupported(string path) => AllSupportedExtensions.Contains(Path.GetExtension(path));

        public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

        public static bool IsAnimatedImageCandidate(string path) => AnimatedImageExtensions.Contains(Path.GetExtension(path));

        /// <summary>
        /// 是否需要通过 Magick.NET 解码（HEIC/HEIF/AVIF/WEBP 等 Avalonia 不支持的格式）。
        /// </summary>
        public static bool IsMagickImage(string path) => MagickImageExtensions.Contains(Path.GetExtension(path));

        /// <summary>
        /// 可能是动态照片的静态图片（JPG/JPEG/HEIC）。苹果实况照片与安卓 Motion Photo 都以静态图为主体。
        /// </summary>
        public static bool IsMotionPhotoCandidate(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".JPG", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".JPEG", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".HEIC", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取类型对应的中文名称，用于窗口标题提示。
        /// </summary>
        public static string GetDisplayName(MediaKind kind)
        {
            return kind switch
            {
                MediaKind.AnimatedImage => "动图",
                MediaKind.Video => "视频",
                MediaKind.MotionPhoto => "动态照片",
                _ => string.Empty,
            };
        }
    }
}
