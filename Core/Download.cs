using System;
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
            long totalSize = 0;
            foreach (var a in assets)
                totalSize += a.AssetSize;

            long downloaded = 0;

            var tasks = new List<Task>();
            foreach (var asset in assets)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await DownloadFileAsync(asset, downloadUrl, chunkSize =>
                    {
                        lock (progress)
                        {
                            downloaded += chunkSize;
                            ((IProgress<double>)progress).Report(downloaded);
                        }
                    }, savePath);
                }));
            }

            await Task.WhenAll(tasks);
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
            byte[] fileArr = new byte[asset.AssetSize];
            foreach (var chunk in asset.AssetChunks)
            {
                string url = $"{downloadUrl}/{chunk.ChunkName}";
                var res = await http.GetByteArrayAsync(url);

                byte[] decompressed = new byte[chunk.ChunkSizeDecompressed];

                using (var msIn = new MemoryStream(res))
                using (var msOut = new MemoryStream())
                using (var dctx = new ZstdNet.DecompressionStream(msIn))
                {
                    dctx.CopyTo(msOut);
                    decompressed = msOut.ToArray();
                }

                string md5 = Utils.GetMd5(decompressed);
                if (md5 != chunk.ChunkDecompressedHashMd5)
                    continue;

                Buffer.BlockCopy(decompressed, 0, fileArr, (int)chunk.ChunkOnFileOffset, decompressed.Length);
                onChunkDone(decompressed.Length);
                decompressed = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            if (Utils.GetMd5(fileArr) != asset.AssetHashMd5)
                return;

            await File.WriteAllBytesAsync(filePath, fileArr);
        }
    }
}
