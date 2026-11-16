using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed class Package {
    internal readonly string Name, Group, Repo, Match, Folder, Help;
    internal readonly bool Octo;
    internal readonly string[] Requires;
    internal Package(string name, string group, string repo, string match, string folder, string help, params string[] requires) {
        Name=name; Group=group; Octo=repo.StartsWith("octo:");
        Repo=Octo?repo.Substring(5):repo; Match=match; Folder=folder; Help=help; Requires=requires;
    }
}
internal sealed class Release {
    internal string Version, Url, File, Digest = "", ArchiveTag = "";
}
internal static class ToolkitPackages {
    internal const string GitHub = "https://github.com/obs1deus/twilighthammer-sync";
    internal const string Version = "0.1.19";
    internal static readonly Package[] Catalog = {
        new Package("TWThreat","Must-have raid addons","octo-addons/TWThreat",null,"TWThreat","Shows threat so you can tell when you are close to pulling aggro."),
        new Package("Rook's RaidVisuals","Must-have raid addons","octo:Rook/RaidVisuals-OctoWoW",@"^patch-O\.mpq$","Data","Makes raid mechanics easier to see. Installs an OctoWoW client data patch."),
        new Package("BigWigs","Must-have raid addons","prodigeomix/BigWigs",@"^BigWigs-.*\.zip$", "BigWigs","Boss timers and raid warnings from the prodigeomix fork."),
        new Package("_LazyPig","Raid & character addons","obs1deus/_lazypig_updated",null,"_LazyPig","Configurable shortcuts and automation for common tasks."),
        new Package("DopingControl","Raid & character addons","octo:ShempError/DopingControl",null,"DopingControl","Checks raid consumables, buffs, resistances, hit and enchants."),
        new Package("AutoMarker","Raid & character addons","MarcelineVQ/AutoMarker",null,"AutoMarker","Assigns raid icons to configured enemies."),
        new Package("RollFor","Raid & character addons","prodigeomix/RollFor",@"^RollFor-.*\.zip$","RollFor","Manages loot rolls and records who rolled."),
        new Package("SmartLoot","Raid & character addons","Necroskillz/SmartLoot",@"^SmartLoot\.zip$","SmartLoot","Compacts loot rolls and remembers your roll choices."),
        new Package("BetterCharacterStats","Raid & character addons","pepopo978/BetterCharacterStats",null,"BetterCharacterStats","Adds more combat stats to the character panel."),
        new Package("Rinse","Raid & character addons","Otari98/Rinse",null,"Rinse","Helps dispel debuffs on party and raid members."),
        new Package("ChronicleCompanion","Raid & character addons","Emyrk/ChronicleCompanion",null,"ChronicleCompanion","Adds extended unit details to Chronicle combat logs. Requires Nampower.","Nampower"),
        new Package("VanillaFixes","Client tools & compatibility","hannesmann/vanillafixes",@"^vanillafixes-[\d.]+\.zip$","game","Launcher that fixes stutter and loads configured client mods."),
        new Package("Nampower","Client tools & compatibility","Emyrk/nampower",@"^nampower\.dll$","game","Improves spell handling and adds APIs required by ChronicleCompanion.","VanillaFixes"),
        new Package("NampowerSettings","Client tools & compatibility","octo:shaga/NampowerSettings",null,"NampowerSettings","In-game settings panel for Nampower.","Nampower"),
        new Package("ClassicAPI","Client tools & compatibility","brues-code/ClassicAPI",@"^ClassicAPI\.dll$","game","Adds newer WoW API functions to the vanilla client.","VanillaFixes"),
        new Package("UnitXP_SP3","Client tools & compatibility","brues-code/UnitXP_SP3",@"^UnitXP_SP3\.dll$","game","Client mod that exposes unit information and distance queries.","VanillaFixes"),
        new Package("UnitXP_SP3_Addon","Client tools & compatibility","octo:shaga/UnitXP_SP3_Addon",null,"UnitXP_SP3_Addon","Companion addon for UnitXP_SP3.","UnitXP_SP3"),
        new Package("VanillaHelpers","Client tools & compatibility","isfir/VanillaHelpers",@"^VanillaHelpers\.dll$","game","Adds file access, minimap functions, and graphics features.","VanillaFixes"),
        new Package("SuperWoW","Client tools & compatibility","balakethelock/SuperWoW",@"^SuperWoW\.release\..*\.zip$","game","Extends vanilla APIs, including live file export for Guild Sync.","VanillaFixes"),
        new Package("SuperAPI","Client tools & compatibility","balakethelock/SuperAPI",null,"SuperAPI","Compatibility addon for SuperWoW.","SuperWoW"),
        new Package("Chronicle Sidekick","Client tools & compatibility","Emyrk/chronicle-sidekick",@"^Chronicle-Sidekick-windows-(amd64|arm64)\.exe$","sidekick","Windows tray app that uploads OctoWoW logs after Chronicle sign-in.","ChronicleCompanion"),
        new Package("Aegis Exchange","Auction House","Torchlite-bit/Aegis_Exchange",null,"Aegis_Exchange","Auction search, full scans, and pricing tools."),
        new Package("Aux","Auction House","OldManAlpha/aux-addon",null,"aux-addon","Auction search, pricing and auction posting tools."),
        new Package("OctoAuctionator","Auction House","octo:John-The-Baptist/OctoAuctionator",null,"OctoAuctionator","Auctionator’s Buy, Sell, and More tabs for OctoWoW.")
    };
    internal static readonly Package Self = new Package("Toolkit","","obs1deus/twilighthammer-sync",@"^TwilightHammerToolkit\.exe$","","");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly HashSet<string> AddonExtensions = new HashSet<string>(".toc .lua .xml .blp .tga .jpg .jpeg .png .wav .ogg .mp3 .ttf .otf .txt .md .gif .dds".Split(' '),StringComparer.OrdinalIgnoreCase);

    private static object Get(Dictionary<string,object> data,string key) {
        object value;
        return data.TryGetValue(key,out value)?value:null;
    }
    private static string StringField(Dictionary<string,object> data,string key) { return Convert.ToString(Get(data,key)); }
    private static Dictionary<string,object> Metadata(string url) {
        return Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(Download(url,2*1024*1024)));
    }
    private static bool Allowed(Uri uri) {
        return uri.Scheme=="https" && new[] {"api.github.com","github.com","codeload.github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","octowow.st"}.Contains(uri.Host);
    }
    internal static byte[] Download(string url,int limit,string expectedTag="",Uri repositoryServer=null) {
        for(int redirects=0;redirects<6;redirects++) {
            Uri uri;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0||
               !(repositoryServer==null?Allowed(uri):uri.Authority.Equals(repositoryServer.Authority,StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Unexpected download server.");
            var req=(HttpWebRequest)WebRequest.Create(uri);
            req.UserAgent="Twilight-Hammer-Toolkit/"+Version;
            req.AllowAutoRedirect=false; req.Timeout=20000; req.ReadWriteTimeout=20000;
            using(var response=(HttpWebResponse)req.GetResponse()) {
                if((int)response.StatusCode>=300&&(int)response.StatusCode<400) {
                    url=new Uri(uri,response.Headers["Location"]).AbsoluteUri;
                    continue;
                }
                if(response.ContentLength>limit)throw new IOException("Download exceeds the size limit.");
                if(expectedTag.Length>0 && response.Headers["ETag"]!=expectedTag)
                    throw new IOException("The source changed during setup. Retry to get the current revision.");
                using(var stream=response.GetResponseStream())
                using(var data=new MemoryStream()) {
                    byte[] chunk=new byte[65536];
                    int count;
                    while((count=stream.Read(chunk,0,chunk.Length))>0) {
                        if(data.Length+count>limit)throw new IOException("Download exceeds the size limit.");
                        data.Write(chunk,0,count);
                    }
                    return data.ToArray();
                }
            }
        }
        throw new IOException("Too many download redirects.");
    }
    internal static string[] StageRepository(string link,string stage) {
        Uri repository;
        if(link.Length>500||!Uri.TryCreate(link.Trim(),UriKind.Absolute,out repository)||repository.Scheme!="https"||
           repository.UserInfo.Length!=0||repository.Query.Length!=0||repository.Fragment.Length!=0)
            throw new IOException("Enter the HTTPS link to a public addon repository.");
        var path=repository.AbsolutePath.Trim('/').Split('/');
        if(path.Length<2||path.Any(part=>!Regex.IsMatch(part,@"^[A-Za-z0-9_][A-Za-z0-9_.-]*$")))
            throw new IOException("Enter the repository's main page or HTTPS clone link.");
        if(path[path.Length-1].EndsWith(".git",StringComparison.OrdinalIgnoreCase))
            path[path.Length-1]=path[path.Length-1].Substring(0,path[path.Length-1].Length-4);
        string url;
        Uri server;
        if(repository.Host=="github.com"&&repository.IsDefaultPort) {
            if(path.Length!=2)throw new IOException("Enter the GitHub repository link without a branch or file path.");
            url="https://codeload.github.com/"+String.Join("/",path)+"/zip/HEAD";
            server=new Uri(url);
        } else {
            string root=repository.GetLeftPart(UriPartial.Authority)+"/"+String.Join("/",path.Take(path.Length-2));
            string api=root.TrimEnd('/')+"/api/v1/repos/"+String.Join("/",path.Skip(path.Length-2));
            server=repository;
            var metadata=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(Download(api,2*1024*1024,"",server)));
            string branch=StringField(metadata,"default_branch");
            if(String.IsNullOrEmpty(branch))throw new IOException("The repository did not return a default branch.");
            url=api+"/archive/"+Uri.EscapeDataString(branch)+".zip";
        }
        var bytes=Download(url,130*1024*1024,"",server);
        StageArchive(bytes,stage,false,true);
        string addons=Path.Combine(stage,"Interface","AddOns");
        if(!Directory.Exists(addons))throw new IOException("No installable WoW addons were found.");
        return Directory.GetDirectories(addons).Select(Path.GetFileName).OrderBy(name=>name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static Release Resolve(Package package) {
        string api=(package.Octo?"https://octowow.st/git/api/v1/repos/":"https://api.github.com/repos/")+package.Repo;
        if(package.Match!=null) {
            var release=Metadata(api+"/releases/latest");
            var assets=Get(release,"assets") as IEnumerable;
            if(assets==null)throw new IOException("No published assets.");
            string architecture=Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")??"";
            foreach(var asset in assets.Cast<object>().OfType<Dictionary<string,object>>()) {
                string name=StringField(asset,"name");
                if(!Regex.IsMatch(name,package.Match,RegexOptions.IgnoreCase))continue;
                if(package.Folder=="sidekick"&&!name.Contains(architecture=="ARM64"?"-arm64.":"-amd64."))continue;
                return new Release {Version=StringField(release,"tag_name"),File=name,Url=StringField(asset,"browser_download_url"),Digest=StringField(asset,"digest")};
            }
            throw new IOException("No matching release asset.");
        }
        if(!package.Octo) {
            string archive="https://codeload.github.com/"+package.Repo+"/zip/HEAD";
            var request=(HttpWebRequest)WebRequest.Create(archive);
            request.UserAgent="Twilight-Hammer-Toolkit/"+Version;
            request.Method="HEAD"; request.AllowAutoRedirect=false; request.Timeout=20000;
            using(var response=(HttpWebResponse)request.GetResponse()) {
                string tag=response.Headers["ETag"]??"";
                if(response.StatusCode!=HttpStatusCode.OK||tag.Length==0)throw new IOException("Source revision unavailable.");
                return new Release {Version=tag,Url=archive,File="source.zip",ArchiveTag=tag};
            }
        }
        string branch=StringField(Metadata(api),"default_branch");
        if(String.IsNullOrEmpty(branch))throw new IOException("Default branch unavailable.");
        var commit=Metadata(api+"/git/commits/"+Uri.EscapeDataString(branch));
        string sha=StringField(commit,"sha");
        if(!Regex.IsMatch(sha,"^[a-f0-9]{40}$"))throw new IOException("Could not resolve the source revision.");
        string source="https://octowow.st/git/"+package.Repo+"/archive/"+sha+".zip";
        return new Release {Version=sha,File="source.zip",Url=source};
    }
    internal static string Hash(byte[] data) {
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
    }
    internal static void Stage(Package package,Release release,string stage) {
        byte[] bytes=Download(release.Url,130*1024*1024,release.ArchiveTag);
        if(release.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase) &&
           !Hash(bytes).Equals(release.Digest.Substring(7),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Upstream checksum did not match.");
        if(!release.File.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) {
            string folder=package.Folder=="game"?"":package.Folder;
            Save(stage,Path.Combine(folder,release.File),bytes);
            return;
        }
        StageArchive(bytes,stage,package.Folder=="game",false);
    }
    private static void StageArchive(byte[] bytes,string stage,bool game,bool repository) {
        using(var zip=new ZipArchive(new MemoryStream(bytes))) {
            long size=0;
            if(zip.Entries.Count>12000)throw new IOException("Too many archive entries.");
            var entries=zip.Entries.Where(entry=>!String.IsNullOrEmpty(entry.Name)).ToArray();
            var manifests=entries.Where(entry=>entry.Name.EndsWith(".toc",StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry=>entry.FullName.Count(c=>c=='/')).ToArray();
            if(!game&&manifests.Length==0)throw new IOException("The upstream archive does not contain a WoW addon.");
            var prefixes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if(!game) {
                foreach(var manifest in manifests) {
                    string prefix=manifest.FullName.Substring(0,manifest.FullName.Length-manifest.Name.Length);
                    if(prefixes.Keys.Any(parent=>prefix.StartsWith(parent,StringComparison.OrdinalIgnoreCase)))continue;
                    string addon=Path.GetFileNameWithoutExtension(manifest.Name);
                    if(repository&&(!Regex.IsMatch(addon,@"^[A-Za-z0-9_][A-Za-z0-9_ .-]{0,79}$")||addon.TrimEnd(' ','.')!=addon||
                       addon.Equals("TwilightHammerSync",StringComparison.OrdinalIgnoreCase)))continue;
                    prefixes.Add(prefix,addon);
                }
            }
            foreach(var entry in entries) {
                string destination=null, extension=Path.GetExtension(entry.Name);
                if(game) {
                    if(!new[] {".exe",".dll",".txt",".md"}.Contains(extension,StringComparer.OrdinalIgnoreCase))continue;
                    if(entry.Name.Equals("dlls.txt",StringComparison.OrdinalIgnoreCase))continue;
                    destination=entry.Name;
                } else if(AddonExtensions.Contains(extension)) {
                    foreach(var prefix in prefixes) if(entry.FullName.StartsWith(prefix.Key,StringComparison.OrdinalIgnoreCase)) {
                        string relative=entry.FullName.Substring(prefix.Key.Length);
                        if(relative.Split('/').Any(segment=>segment.StartsWith(".")))break;
                        destination=Path.Combine("Interface","AddOns",prefix.Value,relative);
                        break;
                    }
                }
                if(destination==null)continue;
                if(repository&&(entry.FullName.Contains("\\")||entry.FullName.Split('/').Any(part=>
                    part.Length==0||part.TrimEnd(' ','.')!=part||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||
                    Regex.IsMatch(part,@"^(con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])(?:\.|$)",RegexOptions.IgnoreCase))))
                    throw new IOException("The addon archive contains an invalid Windows file name.");
                if(repository&&File.Exists(SafePath(stage,destination)))throw new IOException("The addon archive contains duplicate files.");
                size+=entry.Length;
                if(size>300*1024*1024)throw new IOException("Extracted archive exceeds the size limit.");
                using(var input=entry.Open())
                using(var data=new MemoryStream()) {
                    input.CopyTo(data);
                    Save(stage,destination,data.ToArray());
                }
            }
        }
    }
    internal static string SafePath(string root,string relative) {
        if(Path.IsPathRooted(relative)||relative.Contains(":"))throw new IOException("Invalid package path.");
        string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string target=Path.GetFullPath(Path.Combine(root,relative));
        if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid package path.");
        return target;
    }
    internal static void Save(string root,string relative,byte[] bytes) {
        string target=SafePath(root,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.WriteAllBytes(target,bytes);
    }
}
