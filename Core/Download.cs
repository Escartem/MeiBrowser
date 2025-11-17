using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZstdNet;

namespace Core
{
    public class Download
    {
        private static readonly HttpClient http = new();

        public async Task DownloadFilesAsync(List<SophonManifestAssetProperty> assets, string downloadUrl, IProgress<double> progress, string savePath)
        {
            Console.WriteLine("Start download..");
            long totalSize = assets.Sum(a => a.AssetSize);
            long downloaded = 0;

            async Task<bool> TryDownloadFile(SophonManifestAssetProperty asset)
            {
                try
                {
                    await DownloadFileAsync(asset, downloadUrl, chunkSize =>
                    {
                        lock (progress)
                        {
                            downloaded += chunkSize;
                            progress.Report(downloaded);
                        }
                    }, savePath);
                    return Utils.GetMd5(await File.ReadAllBytesAsync(Path.Combine(savePath, asset.AssetName.Replace('/', Path.DirectorySeparatorChar)))) == asset.AssetHashMd5;
                }
                catch
                {
                    return false;
                }
            }

            var failed = new ConcurrentBag<SophonManifestAssetProperty>();
            await Parallel.ForEachAsync(assets, async (asset, _) =>
            {
                int retries = 3;
                bool ok = false;
                while (retries-- > 0 && !ok)
                {
                    ok = await TryDownloadFile(asset);
                    if (!ok)
                    {
                        Console.WriteLine($"Retry {3 - retries}/3 for {asset.AssetName}");
                        await Task.Delay(2000);
                    }
                }
                if (!ok) failed.Add(asset);
            });

            if (failed.Count > 0)
            {
                Console.WriteLine("Rechecking failed files..");
                foreach (var asset in failed.ToList())
                {
                    int retries = 3;
                    bool ok = false;
                    while (retries-- > 0 && !ok)
                    {
                        ok = await TryDownloadFile(asset);
                        if (!ok)
                        {
                            Console.WriteLine($"Final retry {3 - retries}/3 for {asset.AssetName}");
                            await Task.Delay(2000);
                        }
                    }
                }
            }

            Console.WriteLine("Verifying all file hashes..");
            var broken = assets.Where(a =>
                Utils.GetMd5(File.ReadAllBytes(Path.Combine(savePath, a.AssetName.Replace('/', Path.DirectorySeparatorChar)))) != a.AssetHashMd5
            ).ToList();

            if (broken.Count > 0)
            {
                Console.WriteLine($"Redownloading {broken.Count} broken files..");
                await DownloadFilesAsync(broken, downloadUrl, progress, savePath);
            }
        }

        private async Task DownloadFileAsync(SophonManifestAssetProperty asset, string downloadUrl, Action<long> onChunkDone, string savePath)
        {
            Console.WriteLine($"Download file {asset.AssetName}");
            string name = asset.AssetName;
            string normalized = name.Replace('/', Path.DirectorySeparatorChar);
            string relDir = Path.GetDirectoryName(normalized);
            string fullDir = string.IsNullOrEmpty(relDir) ? savePath : Path.Combine(savePath, relDir);
            Directory.CreateDirectory(fullDir);

            string filePath = Path.Combine(savePath, normalized);
            if (File.Exists(filePath))
            {
                var md5 = Utils.GetMd5(File.ReadAllBytes(filePath));
                if (md5 == asset.AssetHashMd5)
                {
                    onChunkDone(asset.AssetSize);
                    return;
                }
            }

            // TODO: restart / retry if fail instead of skipping chunk silently
            using var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (fs.Length < asset.AssetSize) fs.SetLength(asset.AssetSize);

            foreach (var chunk in asset.AssetChunks)
            {
                fs.Seek((long)chunk.ChunkOnFileOffset, SeekOrigin.Begin);
                byte[] existing = new byte[chunk.ChunkSizeDecompressed];
                fs.Read(existing, 0, existing.Length);
                if (Utils.GetMd5(existing) == chunk.ChunkDecompressedHashMd5)
                {
                    onChunkDone(existing.Length);
                    continue;
                }

                byte[] res;
                try { res = await http.GetByteArrayAsync($"{downloadUrl}/{chunk.ChunkName}"); }
                catch { Console.WriteLine($"Failed to download chunk"); continue; }

                byte[] decompressed;
                try
                {
                    using var msIn = new MemoryStream(res);
                    using var msOut = new MemoryStream();
                    using var dctx = new ZstdNet.DecompressionStream(msIn);
                    dctx.CopyTo(msOut);
                    decompressed = msOut.ToArray();
                }
                catch { decompressed = new byte[chunk.ChunkSizeDecompressed]; }

                if (Utils.GetMd5(decompressed) != chunk.ChunkDecompressedHashMd5)
                    decompressed = new byte[chunk.ChunkSizeDecompressed];

                fs.Seek((long)chunk.ChunkOnFileOffset, SeekOrigin.Begin);
                fs.Write(decompressed, 0, decompressed.Length);
                fs.Flush();
                onChunkDone(decompressed.Length);
            }

            fs.Flush();
            fs.Close();

            if (Utils.GetMd5(await File.ReadAllBytesAsync(filePath)) != asset.AssetHashMd5)
                Console.WriteLine("Final file MD5 mismatch");
        }
    }
}
