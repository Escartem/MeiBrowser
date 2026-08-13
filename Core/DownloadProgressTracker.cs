using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Core
{
    // Every file owns a <see cref="FileScope"/> that remembers what it contributed, so a retry can
    // subtract its own partial progress before starting over.

    // A heartbeat keeps pushing snapshots even while no bytes are moving, so the UI never looks
    // frozen during a long hash check.
    internal sealed class ProgressTracker : IDisposable
    {
        // Hashing runs roughly an order of magnitude faster than a transfer of the same size
        private const double VerifyWeight = 0.15;

        private readonly IProgress<DownloadProgress>? sink;
        private readonly long totalBytes;
        private readonly long verifiableBytes;
        private readonly double workTotal;
        private readonly int totalFiles;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly object reportLock = new();
        private readonly ConcurrentDictionary<int, FileScope> active = new();
        private readonly Timer? heartbeat;

        private int nextScopeId;
        private long completedBytes;
        private long verifiedBytes;
        private int filesCompleted;
        private int filesFailed;

        // Raw I/O counters, kept apart from the progress counters above
        private long networkBytes;
        private long diskBytes;

        private readonly double[] networkHistory = new double[HistorySamples];
        private readonly double[] diskHistory = new double[HistorySamples];
        private double networkRate;
        private double diskRate;
        private long lastSampleNetwork;
        private long lastSampleDisk;

        private long lastReportMs = -1;
        private double lastSampleWork;
        private long lastSampleMs;
        private double workPerSecond;

        private const int ReportIntervalMs = 150;
        private const int HeartbeatMs = 250;

        // At one sample per heartbeat this is a rolling window of about 15 seconds
        private const int HistorySamples = 60;

        public ProgressTracker(IProgress<DownloadProgress>? sink, long totalBytes, long verifiableBytes, int totalFiles)
        {
            this.sink = sink;
            this.totalBytes = totalBytes;
            this.verifiableBytes = verifiableBytes;
            this.totalFiles = totalFiles;
            workTotal = totalBytes + verifiableBytes * VerifyWeight;

            if (sink != null)
                heartbeat = new Timer(_ => { SampleRates(); Report(); }, null, HeartbeatMs, HeartbeatMs);
        }

        public FileScope CreateScope(SophonManifestAssetProperty asset, bool hasHash, int maxAttempts)
        {
            int id = Interlocked.Increment(ref nextScopeId);
            var scope = new FileScope(this, id, asset.AssetName, asset.AssetSize, hasHash, maxAttempts);
            active[id] = scope;
            return scope;
        }

        public void FileFinished(FileScope scope, bool success)
        {
            active.TryRemove(scope.Id, out _);
            if (success) Interlocked.Increment(ref filesCompleted);
            else Interlocked.Increment(ref filesFailed);
            Report(force: true);
        }

        private void AddBytes(long delta)
        {
            if (delta != 0) Interlocked.Add(ref completedBytes, delta);
        }

        private void AddVerified(long delta)
        {
            if (delta != 0) Interlocked.Add(ref verifiedBytes, delta);
        }

        // Bytes that arrived over the wire, compressed size included
        public void AddNetwork(long delta)
        {
            if (delta > 0) Interlocked.Add(ref networkBytes, delta);
        }

        // Bytes read from or written to local storage
        public void AddDisk(long delta)
        {
            if (delta > 0) Interlocked.Add(ref diskBytes, delta);
        }

        private double CurrentWork()
        {
            long bytes = Math.Clamp(Interlocked.Read(ref completedBytes), 0, totalBytes);
            long verified = Math.Clamp(Interlocked.Read(ref verifiedBytes), 0, verifiableBytes);
            return bytes + verified * VerifyWeight;
        }

        private void SampleRates()
        {
            lock (reportLock)
            {
                long now = clock.ElapsedMilliseconds;
                long elapsed = now - lastSampleMs;
                if (elapsed < 50) return;

                long net = Interlocked.Read(ref networkBytes);
                long disk = Interlocked.Read(ref diskBytes);
                double work = CurrentWork();

                networkRate = Math.Max(0, (net - lastSampleNetwork) * 1000.0 / elapsed);
                diskRate = Math.Max(0, (disk - lastSampleDisk) * 1000.0 / elapsed);

                double instant = (work - lastSampleWork) * 1000.0 / elapsed;
                // Smooth the overall rate so the ETA does not jitter with every chunk
                workPerSecond = workPerSecond <= 0 ? instant : workPerSecond * 0.7 + instant * 0.3;

                Push(networkHistory, networkRate);
                Push(diskHistory, diskRate);

                lastSampleNetwork = net;
                lastSampleDisk = disk;
                lastSampleWork = work;
                lastSampleMs = now;
            }
        }

        private static void Push(double[] history, double sample)
        {
            Array.Copy(history, 1, history, 0, history.Length - 1);
            history[^1] = sample;
        }

        public void Report(bool force = false)
        {
            if (sink == null) return;

            long now = clock.ElapsedMilliseconds;
            lock (reportLock)
            {
                if (!force && lastReportMs >= 0 && now - lastReportMs < ReportIntervalMs) return;
                lastReportMs = now;

                long bytes = Math.Clamp(Interlocked.Read(ref completedBytes), 0, totalBytes);

                var files = active.Values
                    .OrderBy(s => s.Id)
                    .Select(s => s.Snapshot())
                    .ToList();

                sink.Report(new DownloadProgress(
                    bytes,
                    totalBytes,
                    CurrentWork(),
                    workTotal,
                    Volatile.Read(ref filesCompleted),
                    Volatile.Read(ref filesFailed),
                    totalFiles,
                    Math.Max(0, workPerSecond),
                    new RateMeter(networkRate, (double[])networkHistory.Clone()),
                    new RateMeter(diskRate, (double[])diskHistory.Clone()),
                    files));
            }
        }

        public void Dispose() => heartbeat?.Dispose();

        // Per-file view of the counters. Byte bookkeeping is only touched by the single task that
        // owns the file; the status fields are written with Volatile so the reporting thread can
        // read them safely.
        internal sealed class FileScope
        {
            private readonly ProgressTracker owner;
            private readonly long assetSize;
            private readonly bool hasHash;
            private long counted;
            private long verifyCounted;

            private int state = (int)DownloadFileState.Downloading;
            private int attempt = 1;
            private string? detail;

            public FileScope(ProgressTracker owner, int id, string assetName, long assetSize, bool hasHash, int maxAttempts)
            {
                this.owner = owner;
                Id = id;
                AssetName = assetName;
                MaxAttempts = maxAttempts;
                this.hasHash = hasHash;
                this.assetSize = Math.Max(0, assetSize);
            }

            public int Id { get; }
            public string AssetName { get; }
            public int MaxAttempts { get; }

            public void SetState(DownloadFileState value, string? detailText = null)
            {
                Volatile.Write(ref state, (int)value);
                Volatile.Write(ref detail, detailText);
                // State changes are rare and worth showing at once, so they skip the throttle
                owner.Report(force: true);
            }

            public void SetDetail(string? detailText)
            {
                Volatile.Write(ref detail, detailText);
                owner.Report();
            }

            public void SetAttempt(int value) => Volatile.Write(ref attempt, value);

            // Raw I/O meters. These feed the throughput graphs, not the progress bar
            public void CountNetwork(long bytes) => owner.AddNetwork(bytes);

            public void CountDisk(long bytes) => owner.AddDisk(bytes);

            public FileProgressSnapshot Snapshot() => new(
                AssetName,
                (DownloadFileState)Volatile.Read(ref state),
                Math.Clamp(Interlocked.Read(ref counted), 0, assetSize),
                Math.Clamp(Interlocked.Read(ref verifyCounted), 0, assetSize),
                assetSize,
                Volatile.Read(ref attempt),
                MaxAttempts,
                Volatile.Read(ref detail));

            // Credits transferred bytes, clamped so one file can never contribute more than its size
            public void Advance(long bytes) => Credit(ref counted, bytes, owner.AddBytes);

            public void AdvanceVerify(long bytes) => Credit(ref verifyCounted, bytes, owner.AddVerified);

            private void Credit(ref long field, long bytes, Action<long> push)
            {
                if (bytes <= 0) return;
                long room = assetSize - Interlocked.Read(ref field);
                if (room <= 0) return;
                long delta = Math.Min(bytes, room);
                Interlocked.Add(ref field, delta);
                push(delta);
                owner.Report();
            }

            public void Rewind()
            {
                long held = Interlocked.Exchange(ref counted, 0);
                if (held != 0) owner.AddBytes(-held);

                long verified = Interlocked.Exchange(ref verifyCounted, 0);
                if (verified != 0) owner.AddVerified(-verified);

                if (held != 0 || verified != 0) owner.Report(force: true);
            }

            public void RewindVerification()
            {
                long verified = Interlocked.Exchange(ref verifyCounted, 0);
                if (verified == 0) return;
                owner.AddVerified(-verified);
                owner.Report(force: true);
            }

            public void Complete()
            {
                long remaining = assetSize - Interlocked.Read(ref counted);
                if (remaining > 0)
                {
                    Interlocked.Exchange(ref counted, assetSize);
                    owner.AddBytes(remaining);
                }

                // A hashed file that somehow skipped its check still owes the run its verify share,
                // otherwise the bar would stop just short of 100%
                if (hasHash)
                {
                    long verifyRemaining = assetSize - Interlocked.Read(ref verifyCounted);
                    if (verifyRemaining > 0)
                    {
                        Interlocked.Exchange(ref verifyCounted, assetSize);
                        owner.AddVerified(verifyRemaining);
                    }
                }

                owner.Report(force: true);
            }
        }
    }
}
