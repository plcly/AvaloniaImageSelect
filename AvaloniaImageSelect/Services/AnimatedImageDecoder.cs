using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ImageMagick;

namespace AvaloniaImageSelect.Services
{
    /// <summary>
    /// 多帧动图解码（GIF / 动态 WEBP / APNG / MNG）。
    /// <para>
    /// 使用项目已引用的 Magick.NET 逐帧解码，帧内容保存为编码后的图片字节，
    /// 播放时再按需解码，避免一次性把所有帧展开成位图导致内存暴涨。
    /// </para>
    /// </summary>
    public static class AnimatedImageDecoder
    {
        /// <summary>最多保留的帧数，防止超大动图耗尽内存。</summary>
        private const int MaxFrames = 240;

        /// <summary>帧数据的总大小上限（约 256MB）。</summary>
        private const long MaxTotalBytes = 256L * 1024 * 1024;

        /// <summary>帧的最大边长，超过则等比缩小。</summary>
        private const double MaxDimension = 1280;

        /// <summary>
        /// 尝试把文件当作多帧动图解码。
        /// </summary>
        /// <returns>帧序列；如果文件只有一帧（普通静态图）或解码失败则返回 null。</returns>
        public static FrameSequence? TryDecode(string path, CancellationToken token)
        {
            try
            {
                using var collection = new MagickImageCollection(path);
                if (collection.Count <= 1)
                {
                    // 只有一帧，属于静态图片，交给静态解码路径处理。
                    return null;
                }

                try
                {
                    // 把经过帧间优化（只存差异部分）的动图还原成完整帧。
                    collection.Coalesce();
                }
                catch
                {
                    // 个别动图合并失败时，退化为按原始帧处理。
                }

                var count = collection.Count;
                var stride = (int)Math.Ceiling((double)count / MaxFrames);
                var frames = new List<Frame>();
                long totalBytes = 0;

                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    // 帧数过多时等距抽帧，保证动图整体时长与观感。
                    if (i % stride != 0 && i != count - 1)
                    {
                        continue;
                    }

                    var frame = collection[i];
                    ShrinkIfNeeded(frame);

                    // GIF 的 AnimationDelay 单位是 1/100 秒（uint），部分动图该值为 0，按浏览器习惯取 100ms。
                    var delayMs = frame.AnimationDelay <= 1 ? 100 : (int)(frame.AnimationDelay * 10);
                    delayMs = Math.Max(20, delayMs * stride);

                    using var stream = new MemoryStream();
                    // 有透明通道的帧用 PNG，否则用体积更小的 JPEG。
                    frame.Write(stream, frame.HasAlpha ? MagickFormat.Png : MagickFormat.Jpeg);

                    var data = stream.ToArray();
                    frames.Add(new Frame(data, delayMs));
                    totalBytes += data.Length;

                    if (frames.Count >= MaxFrames || totalBytes >= MaxTotalBytes)
                    {
                        break;
                    }
                }

                return frames.Count > 0 ? new FrameSequence(frames) : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 帧尺寸过大时等比缩小，控制内存占用。
        /// </summary>
        private static void ShrinkIfNeeded(IMagickImage<ushort> frame)
        {
            var width = (double)frame.Width;
            var height = (double)frame.Height;
            if (width <= MaxDimension && height <= MaxDimension)
            {
                return;
            }

            var scale = Math.Min(MaxDimension / width, MaxDimension / height);
            frame.Resize(new Percentage(scale * 100));
        }
    }
}
