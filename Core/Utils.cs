using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core
{
    public class Utils
    {
        public static readonly Dictionary<(string, string), string> gameMap = new()
        {
            {("OS", "nap"), "U5hbdsT9W7"},
            {("CN", "nap"), "x6znKlJ0xK"},
            {("OS", "hkrpg"), "4ziysqXOQ8"},
            {("CN", "hkrpg"), "64kMb5iAWu"},
            {("OS", "hk4e"), "gopR6Cufr3"},
            {("CN", "hk4e"), "1Z8W5NHUQb"},
            {("OS", "bh3"), "5TIVvvcwtM"},
            {("CN", "bh3"), "osvnlOc0S8"},
        };

        public static readonly Dictionary<string, Dictionary<string, string>> sophonMap = new()
        {
            {"OS", new() {
                { "apiBase", "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameBranches" },
                { "sophonBase", "https://sg-public-api.hoyoverse.com/downloader/sophon_chunk/api/getBuild" },
                { "launcherId", "VYTpXlbWo8" },
                { "platApp", "ddxf6vlr1reo" }
            } },
            {"CN", new() {
                { "apiBase", "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches" },
                { "sophonBase", "https://api-takumi.mihoyo.com/downloader/sophon_chunk/api/getBuild" },
                { "launcherId", "jGHBHlcOq1" },
                { "platApp", "ddxf5qt290cg" }
            } }
        };

        public static string FormatSize(long bytes)
        {
            double value;
            string unit;

            if (bytes >= 1L << 30)
            {
                value = bytes / (double)(1L << 30);
                unit = "GB";
            }
            else if (bytes >= 1L << 20)
            {
                value = bytes / (double)(1L << 20);
                unit = "MB";
            }
            else if (bytes >= 1L << 10)
            {
                value = bytes / (double)(1L << 10);
                unit = "KB";
            }
            else
            {
                return $"{bytes} B";
            }

            return $"{value:0.##} {unit}";
        }

        public static string GetMd5(byte[] data) => GetMd5(data.AsSpan());

        public static string GetMd5(ReadOnlySpan<byte> data)
        {
            Span<byte> hash = stackalloc byte[16];
            MD5.HashData(data, hash);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        // better hashing
        public static async Task<string> GetFileMd5Async(
            string path,
            Action<long>? onBytesRead = null,
            CancellationToken cancellationToken = default)
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                bufferSize: 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(4 << 20);
            try
            {
                int read;
                while ((read = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    onBytesRead?.Invoke(read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return Convert.ToHexString(hasher.GetCurrentHash()).ToLowerInvariant();
        }
    }
}
