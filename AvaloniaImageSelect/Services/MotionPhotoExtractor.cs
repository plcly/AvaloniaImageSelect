using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AvaloniaImageSelect.Services
{
    /// <summary>
    /// 动态照片（实况照片 / Motion Photo）的视频来源解析。
    /// <para>
    /// 两种常见形态：
    /// 1. 苹果实况照片：IMG_1234.JPG（或 HEIC）+ 同名的 IMG_1234.MOV 视频文件；
    /// 2. 安卓 Motion Photo：单个 JPG 文件，在 JPEG 结束标记（FFD9）之后直接追加了一段 MP4 数据。
    /// </para>
    /// </summary>
    public static class MotionPhotoExtractor
    {
        private static readonly byte[] FtypMarker = { (byte)'f', (byte)'t', (byte)'y', (byte)'p' };

        /// <summary>小于该大小的数据不认为是有效视频。</summary>
        private const int MinVideoBytes = 8 * 1024;

        /// <summary>超过该大小的文件不做内嵌视频扫描，避免把超大文件读进内存。</summary>
        private const long MaxScanBytes = 256L * 1024 * 1024;

        /// <summary>源文件路径 -> 抽出的临时视频文件路径（缓存，避免来回切换时重复抽取）。</summary>
        private static readonly Dictionary<string, string> ExtractedVideos = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>由临时文件反查源文件，便于清理。</summary>
        private static readonly Dictionary<string, string> TempToSource = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 尝试找到与静态照片关联的视频：优先同名伴随视频（实况照片），其次文件内嵌视频（Motion Photo）。
        /// </summary>
        /// <returns>可播放的视频文件路径，找不到时返回 null。</returns>
        public static string? TryGetMotionVideo(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                return null;
            }

            var companion = TryFindCompanionVideo(imagePath);
            if (companion != null)
            {
                return companion;
            }

            return TryExtractEmbeddedVideo(imagePath);
        }

        /// <summary>
        /// 查找同名的伴随视频文件（苹果实况照片常见的 JPG + MOV 组合）。
        /// </summary>
        public static string? TryFindCompanionVideo(string imagePath)
        {
            var directory = Path.GetDirectoryName(imagePath);
            var baseName = Path.GetFileNameWithoutExtension(imagePath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseName))
            {
                return null;
            }

            string[] videoExtensions = { ".MOV", ".MP4", ".M4V", ".WEBM" };
            foreach (var extension in videoExtensions)
            {
                var candidate = Path.Combine(directory, baseName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 从 JPG/HEIC 中提取内嵌的 MP4 数据，写入临时文件后返回路径。
        /// </summary>
        public static string? TryExtractEmbeddedVideo(string imagePath)
        {
            lock (ExtractedVideos)
            {
                if (ExtractedVideos.TryGetValue(imagePath, out var cached) && File.Exists(cached))
                {
                    return cached;
                }
            }

            string? tempFile = null;
            try
            {
                var info = new FileInfo(imagePath);
                if (!info.Exists || info.Length < MinVideoBytes + 1024 || info.Length > MaxScanBytes)
                {
                    return null;
                }

                var data = File.ReadAllBytes(imagePath);

                // 只有 JPEG 才可能是「内嵌视频的动态照片」。
                // HEIC/AVIF 本身就是 ISO BMFF 容器，文件开头同样有 ftyp 标记，
                // 不先排除掉的话会把整张图片误判成一段视频。
                if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
                {
                    return null;
                }

                var ftypIndex = LastIndexOf(data, FtypMarker);
                if (ftypIndex < 8 || ftypIndex + 8 > data.Length)
                {
                    return null;
                }

                // 再校验 ftyp 后面的 brand 字段，排除 HEIC / AVIF 这类图片容器。
                if (IsImageBrand(ReadAscii(data, ftypIndex + 4, 4)))
                {
                    return null;
                }

                // ISO BMFF 的 box 结构是 [4 字节长度][4 字节类型]，ftyp 是第一个 box，所以数据从 ftyp 前 4 字节开始。
                var start = ftypIndex - 4;
                var boxSize = ReadUInt32BigEndian(data, start);

                int length;
                if (boxSize >= 16 && start + boxSize <= data.Length)
                {
                    length = (int)boxSize;
                }
                else
                {
                    // 部分厂商写入的 box 长度不可靠（或为 0，表示一直延伸到文件结尾），直接取到文件末尾。
                    length = data.Length - start;
                }

                length = (int)Math.Min(length, data.Length - start);
                if (length < MinVideoBytes)
                {
                    return null;
                }

                tempFile = WriteTempVideo(imagePath, data, start, length);

                lock (ExtractedVideos)
                {
                    ExtractedVideos[imagePath] = tempFile;
                    TempToSource[tempFile] = imagePath;
                }

                return tempFile;
            }
            catch
            {
                // 解析失败按普通静态照片处理，不影响主流程。
                return null;
            }
        }

        /// <summary>
        /// 清理抽取到临时目录的视频文件。
        /// </summary>
        public static void Cleanup()
        {
            lock (ExtractedVideos)
            {
                foreach (var tempFile in ExtractedVideos.Values)
                {
                    TryDelete(tempFile);
                }

                ExtractedVideos.Clear();
                TempToSource.Clear();
            }
        }

        private static string WriteTempVideo(string imagePath, byte[] data, int start, int length)
        {
            var tempFile = GetTempVideoPath(imagePath);
            var directory = Path.GetDirectoryName(tempFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data, start, length);
            }

            return tempFile;
        }

        private static string GetTempVideoPath(string imagePath)
        {
            var baseName = Path.GetFileNameWithoutExtension(imagePath);
            var builder = new StringBuilder();
            foreach (var c in baseName)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            var safeName = builder.ToString();
            if (safeName.Length > 40)
            {
                safeName = safeName.Substring(0, 40);
            }

            var hash = Fnv1aHash(imagePath);
            return Path.Combine(Path.GetTempPath(), "AvaloniaImageSelect", $"{safeName}_{hash:X8}.mp4");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 临时文件删不掉也无妨，操作系统会在临时目录清理时回收。
            }
        }

        private static int LastIndexOf(byte[] data, byte[] pattern)
        {
            for (var i = data.Length - pattern.Length; i >= 0; i--)
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

        /// <summary>
        /// 判断 ISO BMFF 的 brand 是否是图片容器（HEIC / HEIF / AVIF 等），
        /// 这类容器不是视频，不能当作动态照片的视频部分。
        /// </summary>
        private static bool IsImageBrand(string brand)
        {
            switch (brand.ToLowerInvariant())
            {
                case "heic":
                case "heix":
                case "heim":
                case "heis":
                case "hevc":
                case "hevx":
                case "hevm":
                case "hevs":
                case "mif1":
                case "msf1":
                case "avif":
                case "avis":
                    return true;
                default:
                    return false;
            }
        }

        private static string ReadAscii(byte[] data, int offset, int length)
        {
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                var value = data[offset + i];
                chars[i] = value >= 32 && value < 127 ? (char)value : ' ';
            }

            return new string(chars);
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24)
                   | ((uint)data[offset + 1] << 16)
                   | ((uint)data[offset + 2] << 8)
                   | data[offset + 3];
        }

        /// <summary>
        /// FNV-1a 哈希，用于生成稳定的临时文件名（跨进程运行也可复用同一个临时文件）。
        /// </summary>
        private static uint Fnv1aHash(string value)
        {
            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;
                var hash = offsetBasis;
                foreach (var c in value)
                {
                    hash ^= c;
                    hash *= prime;
                }

                return hash;
            }
        }
    }
}
