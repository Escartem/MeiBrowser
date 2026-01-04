using Newtonsoft.Json.Linq;
using ZstdNet;

namespace Core
{
    public class Sophon
    {
        private static readonly string branch = "main";
        private static readonly HttpClient client = new();

        public static async Task<JObject> GetGameBranches(string game, string region)
        {
            var gameId = Utils.gameMap[(region, game)];
            var sophonData = Utils.sophonMap[region];
            var metaUrl = $"{sophonData["apiBase"]}?launcher_id={sophonData["launcherId"]}&game_ids[]={gameId}";
            Console.WriteLine(metaUrl);
            var metaJson = JObject.Parse(await client.GetStringAsync(metaUrl));
            return metaJson;
        }

        public static async Task<JObject> GetBuild(string region, string packageId, string password, string version, bool preDownload=false)
        {
            var sophonData = Utils.sophonMap[region];
            var branchName = preDownload ? "pre_download" : branch;
            var buildUrl =
                $"{sophonData["sophonBase"]}?branch={branchName.Replace("_", "")}&package_id={packageId}&password={password}&plat_app={sophonData["platApp"]}";
            if (!preDownload)
                buildUrl += $"&tag={version}";
            Console.WriteLine(buildUrl);
            var buildJson = JObject.Parse(await client.GetStringAsync(buildUrl));
            return buildJson;
        }

        public static async Task<JObject> GetCustomBuild(string url)
        {
            var buildUrl = url;
            var buildJson = JObject.Parse(await client.GetStringAsync(buildUrl));
            return buildJson;
        }

        public static async Task<string> CheckBuild(string url)
        {
            var buildJson = JObject.Parse(await client.GetStringAsync(url));
            var version = buildJson["data"]["tag"].ToString();
            return version;
        }

        public static async Task<(SophonManifestProto, string)> GetManifest(string game, string version, string region, string categoryId, string? customData = "")
        {
            var buildJson = new JObject();

            if (customData != "")
            {
                buildJson = JObject.Parse(customData);
                string matchingField = "game"; // TODO: control matching field
                categoryId = buildJson["data"]["manifests"].First(x => x["matching_field"]!.ToString() == matchingField)["category_id"].ToString();
            } 
            else
            {
                if (game != "custom")
                {
                    var metaJson = await GetGameBranches(game, region);

                    bool preDownload = false;
                    if (version.EndsWith(" (pre-download).0")) // weird workaround
                    {
                        version = version.Replace(" (pre-download).0", "");
                        preDownload = true;
                    }

                    var branchName = preDownload ? "pre_download" : branch;
                    var gameMeta = metaJson["data"]["game_branches"][0][branchName];
                    string packageId = gameMeta["package_id"]!.ToString();
                    string password = gameMeta["password"]!.ToString();

                    buildJson = await GetBuild(region, packageId, password, version, preDownload);
                }
                else
                {
                    buildJson = await GetCustomBuild(region);
                }
            }

            var gameData = buildJson["data"];

            var manifestInfo = gameData["manifests"]
                .First(x => x["category_id"]!.ToString() == categoryId);

            string downloadUrl = $"{manifestInfo["chunk_download"]["url_prefix"]}/$0";
            if (manifestInfo["chunk_download"]["url_suffix"].ToString() != "")
                downloadUrl += $"?{manifestInfo["chunk_download"]["url_suffix"]}";

            string manifestUrl = $"{manifestInfo["manifest_download"]["url_prefix"]}/{manifestInfo["manifest"]["id"]}";
            if (manifestInfo["manifest_download"]["url_suffix"].ToString() != "")
                manifestUrl += $"?{manifestInfo["manifest_download"]["url_suffix"]}";

            Console.WriteLine(manifestUrl);
            var compressed = await client.GetByteArrayAsync(manifestUrl);

            using var dctx = new DecompressionStream(new MemoryStream(compressed));
            using var ms = new MemoryStream();
            dctx.CopyTo(ms);
            ms.Position = 0;

            var manifest = SophonManifestProto.Parser.ParseFrom(ms);
            return (manifest, downloadUrl);
        }
    }
}
