using System.Collections.Generic;
using Core;

namespace GUI
{
    public sealed record PackageSelection(
        string Game,
        string Region,
        string Version,
        string CategoryId,
        string Mode,
        string? PreviousVersion,
        string StokenData)
    {
        public string Title
        {
            get
            {
                string game = Game switch
                {
                    "hk4e" => "Genshin",
                    "hkrpg" => "Star Rail",
                    "nap" => "ZZZ",
                    "bh3" => "Honkai 3rd",
                    "custom" => "Custom",
                    _ => Game
                };

                string version = Version.EndsWith(".0") ? Version[..^2] : Version;
                string diff = PreviousVersion == null ? "" : $" Δ{PreviousVersion}";
                return $"{game} {version}{diff}";
            }
        }
    }

    public sealed record DownloadRequest(
        IReadOnlyList<SophonManifestAssetProperty> Assets,
        long TotalSize,
        string DownloadUrl,
        string SourceTitle);
}
