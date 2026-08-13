using System;
using System.Collections.Generic;

namespace Core
{
    public enum DownloadFileState
    {
        Downloading,
        Verifying,
        Retrying,
        Failed
    }

    /// State of one file that is currently being worked on
    public sealed record FileProgressSnapshot(
        string AssetName,
        DownloadFileState State,
        long BytesCompleted,
        long VerifiedBytes,
        long TotalBytes,
        int Attempt,
        int MaxAttempts,
        string? Detail)
    {
        public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp(BytesCompleted / (double)TotalBytes, 0, 1);

        public double VerifyFraction => TotalBytes <= 0 ? 0 : Math.Clamp(VerifiedBytes / (double)TotalBytes, 0, 1);
    }

    public sealed record RateMeter(double BytesPerSecond, IReadOnlyList<double> History)
    {
        public static readonly RateMeter Empty = new(0, Array.Empty<double>());

        /// Highest sample in the window, useful for scaling a graph
        public double Peak
        {
            get
            {
                double peak = 0;
                foreach (double sample in History)
                    if (sample > peak) peak = sample;
                return peak;
            }
        }
    }

    /// Snapshot of an in-flight download, pushed to the UI on a throttled interval
    public sealed record DownloadProgress(
        long BytesCompleted,
        long TotalBytes,
        double WorkCompleted,
        double WorkTotal,
        int FilesCompleted,
        int FilesFailed,
        int TotalFiles,
        double BytesPerSecond,
        RateMeter Network,
        RateMeter Disk,
        IReadOnlyList<FileProgressSnapshot> ActiveFiles)
    {
        public double Fraction => WorkTotal <= 0 ? 0 : Math.Clamp(WorkCompleted / WorkTotal, 0, 1);

        public TimeSpan? Eta
        {
            get
            {
                if (BytesPerSecond <= 0 || WorkTotal <= 0) return null;
                double remaining = WorkTotal - WorkCompleted;
                if (remaining <= 0) return TimeSpan.Zero;
                return TimeSpan.FromSeconds(remaining / BytesPerSecond);
            }
        }
    }

    public sealed record DownloadFailure(string AssetName, string Reason);

    public sealed record DownloadResult(
        int Succeeded,
        int Skipped,
        IReadOnlyList<DownloadFailure> Failures,
        bool Cancelled)
    {
        public bool AllSucceeded => !Cancelled && Failures.Count == 0;
    }

    public sealed class DownloadOptions
    {
        /// How many files are fetched at once. Chunks inside one file stay sequential
        public int MaxParallelFiles { get; init; } = 4;

        /// Attempts per file before it is reported as failed
        public int MaxAttemptsPerFile { get; init; } = 4;

        /// Attempts per sophon chunk before the whole file attempt is abandoned
        public int MaxAttemptsPerChunk { get; init; } = 4;

        /// Applies to a single request, not to the whole transfer
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

        /// Wait before the first retry; doubled on each further attempt
        public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(2);

        /// Hash files that are already on disk instead of trusting their size
        public bool VerifyExistingFiles { get; init; } = true;
    }
}
