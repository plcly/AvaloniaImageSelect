using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AvaloniaImageSelect.Services
{
    /// <summary>
    /// 动图 / 视频帧播放器。
    /// <para>
    /// 用一个 DispatcherTimer 按每帧的时长切换 Bitmap，实现循环播放。
    /// 帧数据以压缩字节保存在 <see cref="FrameSequence"/> 中，只有当前帧会被解码成 Bitmap，
    /// 因此播放长视频时的内存占用也能保持恒定。
    /// </para>
    /// </summary>
    public sealed class MediaAnimationPlayer : IDisposable
    {
        /// <summary>最小帧间隔，避免异常小的延时把 UI 线程打满。</summary>
        private const int MinDelayMs = 16;

        /// <summary>每播放多少帧主动回收一次内存。</summary>
        private const int CollectInterval = 30;

        private readonly DispatcherTimer _timer;
        private FrameSequence? _frames;
        private Action<Bitmap>? _onFrameChanged;
        private int _index;
        private long _renderedFrames;
        private bool _disposed;

        public MediaAnimationPlayer()
        {
            _timer = new DispatcherTimer();
            _timer.Tick += OnTick;
        }

        /// <summary>是否正在播放动画。</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 开始播放帧序列。只有一帧时等价于显示一张静态图。
        /// </summary>
        /// <param name="frames">帧序列。</param>
        /// <param name="onFrameChanged">每帧解码完成后回调，用于把 Bitmap 交给界面。</param>
        public void Play(FrameSequence frames, Action<Bitmap> onFrameChanged)
        {
            if (_disposed)
            {
                return;
            }

            Stop();

            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _onFrameChanged = onFrameChanged ?? throw new ArgumentNullException(nameof(onFrameChanged));
            _index = 0;
            _renderedFrames = 0;

            // 先渲染首帧，避免等待第一个计时周期时出现空白。
            RenderCurrentFrame();

            if (_frames.Count <= 1)
            {
                return;
            }

            _timer.Interval = GetDelay(_index);
            _timer.Start();
            IsPlaying = true;
        }

        /// <summary>
        /// 停止播放并释放当前帧序列。
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
            IsPlaying = false;
            _frames = null;
            _onFrameChanged = null;
            _index = 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _timer.Tick -= OnTick;
            _timer.Stop();
            _frames = null;
            _onFrameChanged = null;
            _disposed = true;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_frames == null || _frames.Count == 0)
            {
                Stop();
                return;
            }

            _index = (_index + 1) % _frames.Count;
            RenderCurrentFrame();

            // 每一帧的时长可能不同（动图常见），所以每次都重新设置间隔。
            _timer.Interval = GetDelay(_index);
        }

        private void RenderCurrentFrame()
        {
            var frames = _frames;
            var callback = _onFrameChanged;
            if (frames == null || callback == null || frames.Count == 0)
            {
                return;
            }

            var frame = frames[_index];
            try
            {
                using var stream = new MemoryStream(frame.Data, false);
                callback(new Bitmap(stream));
            }
            catch
            {
                // 个别帧损坏时跳过，不影响整体播放。
            }

            // 位图占用的是非托管内存，定期触发一次回收，防止长时间播放时内存持续增长。
            _renderedFrames++;
            if (_renderedFrames % CollectInterval == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private TimeSpan GetDelay(int index)
        {
            var delayMs = _frames != null ? _frames[index].DelayMs : MinDelayMs;
            return TimeSpan.FromMilliseconds(Math.Max(MinDelayMs, delayMs));
        }
    }
}
