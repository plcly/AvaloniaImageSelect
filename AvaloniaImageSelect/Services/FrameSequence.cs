using System;
using System.Collections.Generic;

namespace AvaloniaImageSelect.Services
{
    /// <summary>
    /// 动图 / 视频的单帧。帧内容保存为编码后的图片字节（JPEG 或 PNG），
    /// 播放时才解码成 Bitmap，避免一次性把所有帧解码进内存。
    /// </summary>
    public sealed class Frame
    {
        public Frame(byte[] data, int delayMs)
        {
            Data = data;
            DelayMs = delayMs;
        }

        /// <summary>编码后的图片数据（JPEG 或 PNG）。</summary>
        public byte[] Data { get; }

        /// <summary>该帧停留时长（毫秒）。</summary>
        public int DelayMs { get; }
    }

    /// <summary>
    /// 一序列帧，用于循环播放。
    /// </summary>
    public sealed class FrameSequence
    {
        private readonly List<Frame> _frames;

        public FrameSequence(List<Frame> frames)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }

        public int Count => _frames.Count;

        public Frame this[int index] => _frames[index];

        /// <summary>
        /// 帧序列总的字节数，用于日志/排查内存占用。
        /// </summary>
        public long TotalBytes
        {
            get
            {
                long total = 0;
                foreach (var frame in _frames)
                {
                    total += frame.Data.Length;
                }

                return total;
            }
        }
    }
}
