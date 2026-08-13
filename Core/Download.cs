using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZstdNet;

namespace Core
{
    public class Download
    {
        private static readonly HttpClient http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 32,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(20)
            };
            // Per request timeouts are applied with a linked token instead, so a slow but healthy
            // multi gigabyte transfer is never killed halfway through.
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// Sophon marks directory entries with this asset type; they hold no data
        private const int AssetTypeDirectory = 64;

        private const string PartSuffix = ".meipart";

        public async Task<DownloadResult> DownloadFilesAsync(
            IReadOnlyList<SophonManifestAssetProperty> assets,
            string downloadUrl,
            IProgress<DownloadProgress>? progress,
            string savePath,
            CancellationToken cancellationToken = default,
            DownloadOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(assets);
            if (string.IsNullOrWhiteSpace(savePath))
                throw new ArgumentException("No destination folder was given.", nameof(savePath));

            options ??= new DownloadOptions();
            string root = Path.GetFullPath(savePath);
            Directory.CreateDirectory(root);

            var files = assets.Where(a => a.AssetType != AssetTypeDirectory).ToList();
            foreach (var dir in assets.Where(a => a.AssetType == AssetTypeDirectory))
            {
                if (TryResolvePath(root, dir.AssetName, out var dirPath, out _))
                    Directory.CreateDirectory(dirPath);
            }

            long totalSize = files.Sum(a => Math.Max(0, a.AssetSize));
            // Only files that carry an MD5 get hashed, so only those add verification work
            long verifiableSize = files.Where(a => !string.IsNullOrEmpty(a.AssetHashMd5)).Sum(a => Math.Max(0, a.AssetSize));
            Console.WriteLine($"Starting download of {files.Count} file(s), {Utils.FormatSize(totalSize)}");

            using var tracker = new ProgressTracker(progress, totalSize, verifiableSize, files.Count);
            tracker.Report(force: true);

            var failures = new ConcurrentBag<DownloadFailure>();
            int skipped = 0;
            bool cancelled = false;

            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.MaxParallelFiles),
                CancellationToken = cancellationToken
            };

            try
            {
                await Parallel.ForEachAsync(files, parallel, async (asset, ct) =>
                {
                    var scope = tracker.CreateScope(asset, !string.IsNullOrEmpty(asset.AssetHashMd5), options.MaxAttemptsPerFile);

                    if (!TryResolvePath(root, asset.AssetName, out string filePath, out string? pathError))
                    {
                        Console.WriteLine($"Refusing {asset.AssetName}: {pathError}");
                        failures.Add(new DownloadFailure(asset.AssetName, pathError!));
                        tracker.FileFinished(scope, success: false);
                        return;
                    }

                    string? lastError = null;
                    for (int attempt = 1; attempt <= options.MaxAttemptsPerFile; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();
                        scope.Rewind();
                        scope.SetAttempt(attempt);
                        scope.SetState(DownloadFileState.Downloading);

                        try
                        {
                            bool wasAlreadyComplete = await DownloadOneAsync(asset, filePath, downloadUrl, scope, options, ct);
                            scope.Complete();
                            if (wasAlreadyComplete) Interlocked.Increment(ref skipped);
                            tracker.FileFinished(scope, success: true);
                            return;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastError = Describe(ex);
                            Console.WriteLine($"Attempt {attempt}/{options.MaxAttemptsPerFile} failed for {asset.AssetName}: {lastError}");

                            if (attempt < options.MaxAttemptsPerFile)
                            {
                                scope.SetState(DownloadFileState.Retrying, lastError);
                                await DelayForRetry(options, attempt, ct);
                            }
                        }
                    }

                    scope.Rewind();
                    scope.SetState(DownloadFileState.Failed, lastError);
                    failures.Add(new DownloadFailure(asset.AssetName, lastError ?? "unknown error"));
                    tracker.FileFinished(scope, success: false);
                });
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                Console.WriteLine("Download cancelled.");
            }

            tracker.Report(force: true);

            int succeeded = files.Count - failures.Count;
            if (cancelled)
                Console.WriteLine("Download stopped before finishing.");
            else if (failures.IsEmpty)
                Console.WriteLine($"Done: {succeeded} file(s) ready ({skipped} already present).");
            else
                Console.WriteLine($"Done with {failures.Count} failure(s) out of {files.Count}.");

            return new DownloadResult(cancelled ? 0 : succeeded, skipped, failures.ToList(), cancelled);
        }

        private static async Task<bool> DownloadOneAsync(
            SophonManifestAssetProperty asset,
            string filePath,
            string downloadUrl,
            ProgressTracker.FileScope scope,
            DownloadOptions options,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            bool hasHash = !string.IsNullOrEmpty(asset.AssetHashMd5);

            if (File.Exists(filePath) && await IsFileCompleteAsync(asset, filePath, hasHash, scope, options, ct))
            {
                Console.WriteLine($"Already have {asset.AssetName}");
                return true;
            }

            scope.SetState(DownloadFileState.Downloading);
            if (asset.AssetChunks.Count > 0)
                await DownloadChunkedAsync(asset, filePath, downloadUrl, scope, options, ct);
            else
                await DownloadWholeAsync(asset, filePath, downloadUrl, scope, options, ct);

            await VerifyFinalAsync(asset, filePath, hasHash, scope, ct);
            return false;
        }

        private static async Task<bool> IsFileCompleteAsync(
            SophonManifestAssetProperty asset, string filePath, bool hasHash,
            ProgressTracker.FileScope scope, DownloadOptions options, CancellationToken ct)
        {
            var info = new FileInfo(filePath);
            if (info.Length != asset.AssetSize) return false;

            if (!hasHash) return asset.AssetSize > 0;
            if (!options.VerifyExistingFiles) return true;

            // Check file on disk
            scope.SetState(DownloadFileState.Verifying);
            string actual = await HashWithProgressAsync(filePath, asset.AssetSize, scope, creditTransfer: true, ct);

            if (actual == Normalize(asset.AssetHashMd5))
                return true;

            scope.Rewind();
            return false;
        }

        private static async Task VerifyFinalAsync(
            SophonManifestAssetProperty asset, string filePath, bool hasHash, ProgressTracker.FileScope scope, CancellationToken ct)
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                throw new IOException($"{asset.AssetName} is missing after the download.");

            if (asset.AssetSize > 0 && info.Length != asset.AssetSize)
                throw new IOException($"{asset.AssetName} is {info.Length} bytes, expected {asset.AssetSize}.");

            if (!hasHash) return;

            scope.SetState(DownloadFileState.Verifying);
            string actual = await HashWithProgressAsync(filePath, asset.AssetSize, scope, creditTransfer: false, ct);
            if (actual != Normalize(asset.AssetHashMd5))
            {
                // Drop the bad result
                TryDelete(filePath);
                throw new InvalidDataException($"{asset.AssetName} failed its MD5 check (got {actual}, expected {asset.AssetHashMd5}).");
            }
        }

        private static async Task<string> HashWithProgressAsync(
            string filePath, long expectedSize, ProgressTracker.FileScope scope, bool creditTransfer, CancellationToken ct)
        {
            long read = 0;
            long total = Math.Max(1, expectedSize);

            scope.RewindVerification();

            string hash = await Utils.GetFileMd5Async(filePath, bytes =>
            {
                read += bytes;
                scope.CountDisk(bytes);
                scope.AdvanceVerify(bytes);
                if (creditTransfer)
                    scope.Advance(bytes);
                scope.SetDetail($"checking {Math.Min(100, read * 100 / total)}%");
            }, ct);

            return hash;
        }

        #region whole file transfers

        private static async Task DownloadWholeAsync(
            SophonManifestAssetProperty asset,
            string filePath,
            string downloadUrl,
            ProgressTracker.FileScope scope,
            DownloadOptions options,
            CancellationToken ct)
        {
            string url = BuildFileUrl(downloadUrl, asset.AssetName);
            string partPath = filePath + PartSuffix;
            Console.WriteLine($"Downloading {asset.AssetName} from {url}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.RequestTimeout);

            using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            res.EnsureSuccessStatusCode();

            long? announced = res.Content.Headers.ContentLength;
            long written = 0;

            try
            {
                await using (var source = await res.Content.ReadAsStreamAsync(timeout.Token))
                await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                                          bufferSize: 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
                    try
                    {
                        int read;
                        while (true)
                        {
                            // Each read gets its own budget so a stalled connection fails fast instead of hanging the whole run
                            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            readTimeout.CancelAfter(options.RequestTimeout);

                            read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), readTimeout.Token);
                            if (read <= 0) break;
                            scope.CountNetwork(read);

                            await target.WriteAsync(buffer.AsMemory(0, read), ct);
                            scope.CountDisk(read);
                            written += read;
                            scope.Advance(read);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    await target.FlushAsync(ct);
                }

                if (announced.HasValue && written != announced.Value)
                    throw new IOException($"Connection dropped after {written} of {announced.Value} bytes.");

                File.Move(partPath, filePath, overwrite: true);
            }
            catch
            {
                TryDelete(partPath);
                throw;
            }
        }

        private static string BuildFileUrl(string downloadUrl, string assetName)
        {
            string trimmed = downloadUrl.TrimEnd('/');
            if (trimmed.EndsWith(assetName, StringComparison.Ordinal))
                return trimmed;
            return $"{trimmed}/{assetName.Replace('\\', '/').TrimStart('/')}";
        }

        #endregion

        #region chunked (sophon) transfers

        private static async Task DownloadChunkedAsync(
            SophonManifestAssetProperty asset,
            string filePath,
            string downloadUrl,
            ProgressTracker.FileScope scope,
            DownloadOptions options,
            CancellationToken ct)
        {
            Console.WriteLine($"Downloading {asset.AssetName} ({asset.AssetChunks.Count} chunks)");

            await using var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                                                bufferSize: 1 << 20, FileOptions.Asynchronous);
            if (fs.Length != asset.AssetSize)
                fs.SetLength(asset.AssetSize);

            int chunkIndex = 0;
            foreach (var chunk in asset.AssetChunks)
            {
                ct.ThrowIfCancellationRequested();
                scope.SetDetail($"chunk {++chunkIndex}/{asset.AssetChunks.Count}");

                int size = checked((int)chunk.ChunkSizeDecompressed);
                if (size < 0 || chunk.ChunkOnFileOffset < 0 || chunk.ChunkOnFileOffset + size > asset.AssetSize)
                    throw new InvalidDataException($"Chunk {chunk.ChunkName} does not fit inside {asset.AssetName}.");

                if (await ChunkAlreadyOnDiskAsync(fs, chunk, size, scope, ct))
                {
                    scope.Advance(size);
                    continue;
                }

                byte[] data = await FetchChunkAsync(chunk, downloadUrl, size, scope, options, ct);

                fs.Seek(chunk.ChunkOnFileOffset, SeekOrigin.Begin);
                await fs.WriteAsync(data.AsMemory(0, size), ct);
                scope.CountDisk(size);
                scope.Advance(size);
            }

            await fs.FlushAsync(ct);
        }

        private static async Task<bool> ChunkAlreadyOnDiskAsync(
            FileStream fs, SophonManifestAssetChunk chunk, int size, ProgressTracker.FileScope scope, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(chunk.ChunkDecompressedHashMd5)) return false;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                fs.Seek(chunk.ChunkOnFileOffset, SeekOrigin.Begin);

                int filled = 0;
                while (filled < size)
                {
                    int read = await fs.ReadAsync(buffer.AsMemory(filled, size - filled), ct);
                    if (read <= 0) return false;
                    scope.CountDisk(read);
                    filled += read;
                }

                return Utils.GetMd5(buffer.AsSpan(0, size)) == Normalize(chunk.ChunkDecompressedHashMd5);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static async Task<byte[]> FetchChunkAsync(
            SophonManifestAssetChunk chunk, string downloadUrl, int size,
            ProgressTracker.FileScope scope, DownloadOptions options, CancellationToken ct)
        {
            string url = downloadUrl.Replace("$0", chunk.ChunkName);
            string? lastError = null;

            for (int attempt = 1; attempt <= options.MaxAttemptsPerChunk; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(options.RequestTimeout);

                    using var res = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, timeout.Token);
                    res.EnsureSuccessStatusCode();
                    byte[] compressed = await res.Content.ReadAsByteArrayAsync(timeout.Token);

                    scope.CountNetwork(compressed.Length);

                    byte[] plain = Decompress(compressed, size);
                    if (plain.Length != size)
                        throw new InvalidDataException($"chunk decompressed to {plain.Length} bytes, expected {size}");

                    if (!string.IsNullOrEmpty(chunk.ChunkDecompressedHashMd5) &&
                        Utils.GetMd5(plain.AsSpan(0, size)) != Normalize(chunk.ChunkDecompressedHashMd5))
                        throw new InvalidDataException("chunk MD5 mismatch");

                    return plain;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = Describe(ex);
                    Console.WriteLine($"Chunk {chunk.ChunkName} attempt {attempt}/{options.MaxAttemptsPerChunk}: {lastError}");
                    if (attempt < options.MaxAttemptsPerChunk)
                        await DelayForRetry(options, attempt, ct);
                }
            }

            throw new IOException($"Chunk {chunk.ChunkName} could not be downloaded ({lastError}).");
        }

        private static byte[] Decompress(byte[] compressed, int expectedSize)
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var output = new MemoryStream(expectedSize);
            using var zstd = new DecompressionStream(input);
            zstd.CopyTo(output);
            return output.ToArray();
        }

        #endregion

        #region helpers

        // Maps a manifest asset name onto a real path and rejects anything that would escape the destination folder
        private static bool TryResolvePath(string root, string assetName, out string fullPath, out string? error)
        {
            fullPath = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(assetName))
            {
                error = "empty asset name";
                return false;
            }

            string relative = assetName.Replace('/', Path.DirectorySeparatorChar)
                                       .Replace('\\', Path.DirectorySeparatorChar)
                                       .TrimStart(Path.DirectorySeparatorChar);
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(root, relative));
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    error = "asset path escapes the destination folder";
                    return false;
                }
                fullPath = candidate;
                return true;
            }
            catch (Exception ex)
            {
                error = $"invalid path ({ex.Message})";
                return false;
            }
        }

        private static async Task DelayForRetry(DownloadOptions options, int attempt, CancellationToken ct)
        {
            // Exponential backoff with a little jitter so parallel retries do not hit the CDN in lockstep
            double seconds = options.RetryBaseDelay.TotalSeconds * Math.Pow(2, attempt - 1);
            seconds = Math.Min(seconds, 30) * (0.8 + Random.Shared.NextDouble() * 0.4);
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        }

        private static string Normalize(string hash) => hash.Trim().ToLowerInvariant();

        private static string Describe(Exception ex) => ex switch
        {
            HttpRequestException http when http.StatusCode.HasValue => $"HTTP {(int)http.StatusCode.Value} {http.StatusCode}",
            HttpRequestException http => http.Message,
            TaskCanceledException or OperationCanceledException => "request timed out",
            _ => $"{ex.GetType().Name}: {ex.Message}"
        };

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* nothing useful to do */ }
        }

        #endregion
    }
}
