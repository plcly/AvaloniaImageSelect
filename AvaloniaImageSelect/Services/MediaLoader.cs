using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;

namespace AvaloniaImageSelect.Services
{
    /// <summary>
    /// 加载结果。
    /// </summary>
    public sealed class MediaPreview
    {
        private MediaPreview(MediaKind kind, Bitmap? staticImage, FrameSequence? frames, string? errorMessage)
        {
            Kind = kind;
            StaticImage = staticImage;
            Frames = frames;
            ErrorMessage = errorMessage;
        }

        /// <summary>内容类型。</summary>
        public MediaKind Kind { get; }

        /// <summary>静态图片（普通照片）。</summary>
        public Bitmap? StaticImage { get; }

        /// <summary>帧序列（动图 / 视频 / 动态照片）。</summary>
        public FrameSequence? Frames { get; }

        /// <summary>加载失败时的提示信息。</summary>
        public string? ErrorMessage { get; }

        public bool IsAnimated => Frames != null && Frames.Count > 0;

        public static MediaPreview Static(Bitmap bitmap, MediaKind kind) =>
            new(kind, bitmap, null, null);

        public static MediaPreview Animated(FrameSequence frames) =>
            new(MediaKind.AnimatedImage, null, frames, null);

        public static MediaPreview Video(FrameSequence frames) =>
            new(MediaKind.Video, null, frames, null);

        public static MediaPreview MotionPhoto(FrameSequence frames) =>
            new(MediaKind.MotionPhoto, null, frames, null);

        public static MediaPreview Failed(string message) =>
            new(MediaKind.Image, CreatePlaceholder(), null, message);

        /// <summary>
        /// 无法预览时显示的灰色占位图，避免界面出现空白。
        /// </summary>
        private static Bitmap CreatePlaceholder()
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(320, 180),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Premul);

            using var buffer = bitmap.Lock();
            var pixelCount = buffer.Size.Width * buffer.Size.Height;
            for (var i = 0; i < pixelCount; i++)
            {
                Marshal.WriteInt32(buffer.Address, i * 4, unchecked((int)0xFF2B2B2B));
            }

            return bitmap;
        }
    }

    /// <summary>
    /// 根据文件类型选择合适的解码方式，把文件加载成可预览的内容。
    /// </summary>
    public static class MediaLoader
    {
        /// <summary>
        /// 加载一个文件用于预览。该方法是同步阻塞的，调用方应放到后台线程执行。
        /// </summary>
        public static MediaPreview Load(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                return MediaPreview.Failed("文件不存在");
            }

            // 1. 视频文件
            if (MediaTypes.IsVideo(path))
            {
                var frames = VideoFrameDecoder.Decode(path, token);
                token.ThrowIfCancellationRequested();
                return frames != null
                    ? MediaPreview.Video(frames)
                    : MediaPreview.Failed(BuildFfmpegMissingMessage());
            }

            // 2. 动图：GIF / APNG / MNG，以及可能是动态图的 WEBP
            if (MediaTypes.IsAnimatedImageCandidate(path) || IsPossiblyAnimatedWebp(path))
            {
                var frames = AnimatedImageDecoder.TryDecode(path, token);
                token.ThrowIfCancellationRequested();
                if (frames != null && frames.Count > 1)
                {
                    return MediaPreview.Animated(frames);
                }

                if (frames != null)
                {
                    // 只有一帧，按静态图处理。
                    return LoadStaticImage(path);
                }
            }

            // 3. 动态照片：JPG / HEIC 伴随或内嵌视频（苹果实况照片、安卓 Motion Photo）
            if (MediaTypes.IsMotionPhotoCandidate(path))
            {
                var motionVideo = MotionPhotoExtractor.TryGetMotionVideo(path);
                token.ThrowIfCancellationRequested();
                if (motionVideo != null)
                {
                    var frames = VideoFrameDecoder.Decode(motionVideo, token);
                    token.ThrowIfCancellationRequested();
                    if (frames != null)
                    {
                        return MediaPreview.MotionPhoto(frames);
                    }
                }
            }

            // 4. 静态图片
            return LoadStaticImage(path);
        }

        private static MediaPreview LoadStaticImage(string path)
        {
            var bitmap = TryDecodeStatic(path);
            var kind = MediaTypes.IsMagickImage(path) ? MediaKind.MagickImage : MediaKind.Image;
            return bitmap != null
                ? MediaPreview.Static(bitmap, kind)
                : MediaPreview.Failed("无法预览该文件，可能是已损坏或不受支持的格式");
        }

        /// <summary>
        /// 解码静态图片：普通格式交给 Avalonia，HEIC/HEIF/AVIF/WEBP 等交给 Magick.NET。
        /// </summary>
        private static Bitmap? TryDecodeStatic(string path)
        {
            if (MediaTypes.IsMagickImage(path))
            {
                return TryDecodeWithMagick(path);
            }

            try
            {
                return new Bitmap(path);
            }
            catch
            {
                // 部分扩展名正常但内容特殊的图片，回退到 Magick.NET 再试一次。
                return TryDecodeWithMagick(path);
            }
        }

        private static Bitmap? TryDecodeWithMagick(string path)
        {
            try
            {
                using var magickImage = new MagickImage(path);
                return magickImage.ToWriteableBitmap();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// WEBP 既可能是静态图也可能是动图，需要读取后判断。
        /// </summary>
        private static bool IsPossiblyAnimatedWebp(string path)
        {
            return string.Equals(Path.GetExtension(path), ".WEBP", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildFfmpegMissingMessage()
        {
            return "视频预览需要 FFmpeg：请安装 ffmpeg 后把 ffmpeg.exe 所在路径填到「设置」中，" +
                   "或把 ffmpeg.exe 放到程序目录，也可以设置环境变量 FFMPEG_PATH。";
        }
    }
}
