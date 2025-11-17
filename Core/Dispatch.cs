using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using static System.Net.WebRequestMethods;

namespace Core
{
    public class Dispatch
    {
        private static readonly HttpClient client = new();

        public static async Task<JObject> GetDispatchData()
        {
            var dispatchUrl = "https://raw.githubusercontent.com/umaichanuwu/meta/refs/heads/master/hoyodata.json";
            var dispatchJson = JObject.Parse(await client.GetStringAsync(dispatchUrl));
            return dispatchJson;
        }

        public static async Task<List<string>> GetDispatchVersions(string game)
        {
            var dispatchJson = await GetDispatchData();
            var versions = ((JObject)dispatchJson[game]["hashes"]).Properties().Select(p => p.Name).ToList();
            var minVersion = Version.Parse((string)dispatchJson[game]["minVersion"]);
            foreach (var v in versions.ToList())
            {
                if (game == "hk4e")
                {
                    if (v == "3.2" || v == "3.4")
                    {
                        versions.Remove(v); // mihoyo accident in their servers, they're gone :(
                    }
                }
            }
            versions.Sort();
            versions.Reverse();
            return versions;
        }

        public static async Task<List<string>> GetPackages(string game, string version)
        {
            var dispatchJson = await GetDispatchData();
            var minVersion = Version.Parse((string)dispatchJson[game]["minVersion"]);

            List<string> packages = new();

            if (Version.Parse(version).CompareTo(minVersion) > 0)
            {
                packages.Add("Files");
            }

            if (!(game == "hkrpg" && Version.Parse(version).CompareTo(Version.Parse("2.0")) < 0))
            {
                packages.Add("ZIP");
            }

            return packages;
        }

        public static async Task<(SophonManifestProto, string)> GetFiles(string game, string version, string mode)
        {
            if (mode == "zip")
            {
                Console.WriteLine("Using ZIP mode");
                return await GetZIPFiles(game, version);
            }

            var dispatchJson = (JObject)await GetDispatchData();
            var urlBase = dispatchJson[game]["scatterURL"]!.ToString();
            var versionHash = dispatchJson[game]["hashes"]![version]!.ToString();
            urlBase = urlBase.Replace("$0", versionHash);
            Console.WriteLine($"url base : {urlBase}");

            // get structure
            var structureUrl = $"{urlBase}/{dispatchJson[game]["filesIndexOptions"]["index"]}";
            var structureRaw = await client.GetStringAsync(structureUrl);

            SophonManifestProto manifest = new SophonManifestProto();
            foreach (var file in structureRaw.Split("\n"))
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                var fileJson = JObject.Parse(file);

                var asset = new SophonManifestAssetProperty
                {
                    AssetName = fileJson["remoteName"]!.ToString(),
                    AssetHashMd5 = fileJson["md5"]!.ToString(),
                    AssetSize = long.Parse(fileJson["fileSize"]!.ToString())
                };
                manifest.Assets.Add(asset);
            }

            return (manifest, urlBase);
        }

        public static async Task<(SophonManifestProto, string)> GetZIPFiles(string game, string version)
        {
            var dispatchJson = (JObject)await GetDispatchData();
            var ver = new Version(version);
            var key = dispatchJson[game]["urls"]["full"].Children<JProperty>()
                .Select(p => new Version(p.Name))
                .Where(v => v <= ver)
                .Max();
            var urlBase = dispatchJson[game]["urls"]["full"][key.ToString()]!.ToString();
            var versionHash = dispatchJson[game]["hashes"]![version]!.ToString();
            urlBase = urlBase.Replace("$0", versionHash);
            urlBase = urlBase.Replace("$1", $"{version}.0");
            Console.WriteLine($"zip url : {urlBase}");

            SophonManifestProto manifest = new SophonManifestProto();
            for (var i = 1; i <= 15; i++)
            {
                var url = urlBase;
                if (urlBase.EndsWith("$4"))
                    url = urlBase.Replace("$4", i.ToString("D3"));

                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

                var ok = res.IsSuccessStatusCode;

                if (!ok)
                    break;

                var size = res.Content.Headers.ContentLength ?? 0;

                if (size == 3) // hsr can return ">.<" with status 200 which means not found
                    break;

                var filename = url.Split('/').Last();
                var asset = new SophonManifestAssetProperty
                {
                    AssetName = filename,
                    AssetHashMd5 = "", // no hash available
                    AssetSize = size
                };

                manifest.Assets.Add(asset);

                if (!urlBase.EndsWith("$4"))
                    break;
            }

            return (manifest, urlBase);
        }
    }
}
