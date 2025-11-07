using Newtonsoft.Json.Linq;
using ZstdNet;

namespace Core
{
    public class Sophon
    {
        // TODO: add support for pre_download branch
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

        public static async Task<JObject> GetBuild(string region, string packageId, string password, string version)
        {
            var sophonData = Utils.sophonMap[region];
            var buildUrl =
                $"{sophonData["sophonBase"]}?branch={branch.Replace("_", "")}&package_id={packageId}&password={password}&plat_app={sophonData["platApp"]}&tag={version}";
            Console.WriteLine(buildUrl);
            var buildJson = JObject.Parse(await client.GetStringAsync(buildUrl));
            return buildJson;
        }

        public static async Task<(SophonManifestProto, string)> GetManifest(string game, string version, string region, string categoryId)
        {
            var metaJson = await GetGameBranches(game, region);

            var gameMeta = metaJson["data"]["game_branches"][0][branch];
            string packageId = gameMeta["package_id"]!.ToString();
            string password = gameMeta["password"]!.ToString();

            var buildJson = await GetBuild(region, packageId, password, version);

            var gameData = buildJson["data"];

            var manifestInfo = gameData["manifests"]
                .First(x => x["category_id"]!.ToString() == categoryId);

            string downloadUrl = $"{manifestInfo["chunk_download"]["url_prefix"]}";

            string manifestUrl = $"{manifestInfo["manifest_download"]["url_prefix"]}/{manifestInfo["manifest"]["id"]}";
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
