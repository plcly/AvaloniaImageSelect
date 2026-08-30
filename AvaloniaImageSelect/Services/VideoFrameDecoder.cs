using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaImageSelect.Services
{
    /// <summary>
    /// 视频抽帧解码（MP4 / MOV / M4V / WEBM）。
    /// <para>
    /// 通过调用 FFmpeg 把视频按固定帧率转成 JPEG 帧并循环播放，
    /// 这样不需要引入额外的 Avalonia 视频控件，对 Avalonia 版本没有依赖。
    /// 帧数据保存在内存中（压缩后的 JPEG），播放时逐帧解码。
    /// </para>
    /// </summary>
    public static class VideoFrameDecoder
    {
        /// <summary>抽帧帧率。</summary>
        private const int FrameRate = 12;

        /// <summary>最多预览的秒数，避免长视频占用过多内存。</summary>
        private const int MaxSeconds = 20;

        /// <summary>帧宽度上限，高度按比例自动缩放（保持为偶数）。</summary>
        private const int MaxWidth = 960;

        /// <summary>最多保留的帧数。</summary>
        private const int MaxFrames = FrameRate * MaxSeconds + 8;

        /// <summary>帧数据总大小上限（约 256MB）。</summary>
        private const long MaxTotalBytes = 256L * 1024 * 1024;

        private static readonly byte[] SoiMarker = { 0xFF, 0xD8 };

        /// <summary>
        /// 按帧率计算每帧停留时长。
        /// </summary>
        private static int FrameDelayMs => 1000 / FrameRate;

        /// <summary>
        /// 定位可用的 ffmpeg 可执行文件。
        /// 查找顺序：设置中配置的路径 -> 环境变量 -> 程序运行目录 -> 系统 PATH。
        /// </summary>
        public static string? FindFfmpeg()
        {
            var configured = GetConfiguredFfmpeg();
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            var fromEnv = Environment.GetEnvironmentVariable("AIS_FFMPEG_PATH")
                          ?? Environment.GetEnvironmentVariable("FFMPEG_PATH");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                var resolved = ResolveExecutable(fromEnv);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            var baseDirectory = AppContext.BaseDirectory;
            string[] relativeCandidates =
            {
                "ffmpeg.exe", "ffmpeg",
                Path.Combine("ffmpeg", "ffmpeg.exe"), Path.Combine("ffmpeg", "ffmpeg"),
                Path.Combine("tools", "ffmpeg.exe"), Path.Combine("tools", "ffmpeg"),
                Path.Combine("tools", "ffmpeg", "ffmpeg.exe"), Path.Combine("tools", "ffmpeg", "ffmpeg"),
            };
            foreach (var relative in relativeCandidates)
            {
                var candidate = Path.Combine(baseDirectory, relative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVariable))
            {
                foreach (var directory in pathVariable.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    var resolved = ResolveExecutable(Path.Combine(directory.Trim(), "ffmpeg"));
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 把视频解码成帧序列。找不到 ffmpeg 或解码失败时返回 null。
        /// </summary>
        public static FrameSequence? Decode(string videoPath, CancellationToken token)
        {
            var ffmpeg = FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg))
            {
                return null;
            }

            Process? process = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                startInfo.ArgumentList.Add("-hide_banner");
                startInfo.ArgumentList.Add("-loglevel");
                startInfo.ArgumentList.Add("error");
                startInfo.ArgumentList.Add("-nostdin");
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(videoPath);
                startInfo.ArgumentList.Add("-an");
                startInfo.ArgumentList.Add("-sn");
                startInfo.ArgumentList.Add("-vf");
                startInfo.ArgumentList.Add($"fps={FrameRate},scale={MaxWidth}:-2:flags=fast_bilinear");
                startInfo.ArgumentList.Add("-t");
                startInfo.ArgumentList.Add(MaxSeconds.ToString());
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("image2pipe");
                startInfo.ArgumentList.Add("-vcodec");
                startInfo.ArgumentList.Add("mjpeg");
                startInfo.ArgumentList.Add("-");

                process = Process.Start(startInfo);
                if (process == null)
                {
                    return null;
                }

                // 持续读取 stderr，防止输出缓冲区写满导致 ffmpeg 卡住。
                _ = process.StandardError.ReadToEndAsync();

                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                var soiCount = 0;

                while (!token.IsCancellationRequested)
                {
                    var read = process.StandardOutput.BaseStream.Read(chunk, 0, chunk.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    buffer.Write(chunk, 0, read);
                    soiCount += CountSoi(chunk, read);

                    if (soiCount > MaxFrames || buffer.Length >= MaxTotalBytes)
                    {
                        break;
                    }
                }

                if (token.IsCancellationRequested)
                {
                    return null;
                }

                var frames = SplitJpegFrames(buffer.ToArray(), MaxFrames);
                if (frames.Count == 0)
                {
                    return null;
                }

                return new FrameSequence(frames.Select(data => new Frame(data, FrameDelayMs)).ToList());
            }
            catch
            {
                return null;
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    catch
                    {
                        // 进程已经退出，忽略。
                    }

                    process.Dispose();
                }
            }
        }

        /// <summary>
        /// 从设置（config.db）中读取 ffmpeg 路径。
        /// </summary>
        private static string? GetConfiguredFfmpeg()
        {
            try
            {
                var service = App.Provider.GetRequiredService<SqliteService>();
                var configured = service.GetFfmpegPath();
                return string.IsNullOrWhiteSpace(configured) ? null : ResolveExecutable(configured);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 把给定路径解析为可执行的 ffmpeg：可以是文件路径，也可以是包含 ffmpeg 的目录。
        /// </summary>
        private static string? ResolveExecutable(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var trimmed = path.Trim().Trim('"');
            if (File.Exists(trimmed))
            {
                return trimmed;
            }

            if (Directory.Exists(trimmed))
            {
                foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
                {
                    var candidate = Path.Combine(trimmed, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 统计缓冲区中 JPEG 起始标记（FFD8）出现的次数，用于提前结束读取。
        /// </summary>
        private static int CountSoi(byte[] buffer, int length)
        {
            var count = 0;
            for (var i = 0; i < length - 1; i++)
            {
                if (buffer[i] == SoiMarker[0] && buffer[i + 1] == SoiMarker[1])
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 把 ffmpeg 输出的连续 JPEG 流切分成单帧。
        /// 按 JPEG 标记结构逐段跳过，确保帧边界是真正的 EOI（FFD9），而不是数据中碰巧出现的字节。
        /// </summary>
        private static List<byte[]> SplitJpegFrames(byte[] data, int maxFrames)
        {
            var frames = new List<byte[]>();
            var position = 0;

            while (position < data.Length - 1 && frames.Count < maxFrames)
            {
                var start = IndexOf(data, SoiMarker, position);
                if (start < 0)
                {
                    break;
                }

                var end = FindEoi(data, start + 2);
                if (end < 0)
                {
                    break;
                }

                var length = end + 2 - start;
                var frame = new byte[length];
                Array.Copy(data, start, frame, 0, length);
                frames.Add(frame);

                position = end + 2;
            }

            return frames;
        }

        /// <summary>
        /// 从 SOI 之后开始按标记结构查找 EOI，返回 EOI 标记的起始下标。
        /// </summary>
        private static int FindEoi(byte[] data, int position)
        {
            while (position < data.Length - 1)
            {
                if (data[position] != 0xFF)
                {
                    position++;
                    continue;
                }

                var marker = data[position + 1];

                if (marker == 0xD9)
                {
                    // EOI：帧结束。
                    return position;
                }

                if (marker == 0x00 || marker == 0xFF)
                {
                    // 填充字节，直接跳过。
                    position += 2;
                    continue;
                }

                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    // TEM 与 RSTn：后面不跟长度字段。
                    position += 2;
                    continue;
                }

                if (position + 3 >= data.Length)
                {
                    return -1;
                }

                // 其它标记后面跟 2 字节长度（长度包含这两个字节本身）。
                var segmentLength = (data[position + 2] << 8) | data[position + 3];
                if (segmentLength < 2)
                {
                    return -1;
                }

                position += 2 + segmentLength;
            }

            return -1;
        }

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex)
        {
            for (var i = startIndex; i <= data.Length - pattern.Length; i++)
            {
                var matched = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
