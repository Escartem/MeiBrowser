using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.Compiler;
using Newtonsoft.Json.Linq;

namespace Core
{
    public class Meta
    {
        private static readonly HttpClient client = new();

        public static async Task<(List<string> versions, string packageId, string password)> GetVersions(string game, string region) {
            Console.WriteLine($"Fetching versions for {game} in {region}...");

            var gameMeta = await Sophon.GetGameBranches(game, region);

            var latest = (string)gameMeta["data"]["game_branches"][0]["main"]["tag"];
            latest = string.Join('.', latest.Split('.').Take(2));

            var metaUrl = "https://raw.githubusercontent.com/umaichanuwu/meta/refs/heads/master/hoyodata.json";
            var metaJson = JObject.Parse(await client.GetStringAsync(metaUrl));

            var versions = ((JObject)metaJson[game]["sophonHashes"]).Properties().Select(p => p.Name).ToList();

            if (!versions.Contains(latest))
                versions.Add(latest);

            versions.Sort();
            versions.Reverse();

            var packageId = (string)gameMeta["data"]["game_branches"][0]["main"]["package_id"];
            var password = (string)gameMeta["data"]["game_branches"][0]["main"]["password"];

            return (versions, packageId, password);
        }

        public static async Task<List<string[]>> GetPackages(string region, string version, string packageId, string password)
        {
            Console.WriteLine($"Fetching packages for {packageId} password {password} in {region} version {version}...");

            var build = await Sophon.GetBuild(region, packageId, password, $"{version}.0");

            List<string[]> packages = new();

            foreach (var package in build["data"]["manifests"])
            {
                Console.WriteLine($"{package["category_id"]} - {package["matching_field"]} - {package["category_name"]}");
                packages.Add(new string[] { (string)package["category_id"], (string)package["matching_field"], (string)package["category_name"] });
            }

            return packages;
        }
    }
}
