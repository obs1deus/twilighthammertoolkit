using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

internal sealed class MpqCatalogEntry {
    internal readonly string Id, Name, Description, Group, Slot, SourceUrl, AssetUrl, Sha256;
    internal readonly int Length;
    internal MpqCatalogLink[] DownloadLinks=new MpqCatalogLink[0];

    internal MpqCatalogEntry(string id,string name,string description,string group,char slot,string sourceUrl,
        string assetUrl=null,int length=0,string sha256=null) {
        Id=id;Name=name;Description=description;Group=group;Slot="patch-"+slot+".mpq";SourceUrl=sourceUrl;
        AssetUrl=assetUrl;Length=length;Sha256=sha256;
    }
}

internal sealed class MpqCatalogLink {
    public string Name {get;private set;}
    public string Url {get;private set;}

    internal MpqCatalogLink(string name,string url) {
        Name=name;Url=url;
    }
}

internal static class MpqDiscovery {
    private const string ReforgedSource="https://projectreforged.github.io/vanilla/downloads/";
    private const string ReforgedLegacySource="https://discord.gg/jnvkayMbqJ";
    private const string ReforgedAssets="https://pub-0f05631d243e4046993fc02ca7be9542.r2.dev/patches/";
    private const string VisualSource="https://github.com/RetroCro/TurtleWoW-Mods/blob/main/README.md";
    private const string RaidVisualsSource="https://octowow.st/git/Rook/RaidVisuals-OctoWoW";
    private const string DriveDownload="https://drive.usercontent.google.com/download?export=download&id=";
    private const string ReceiptFolder=".twilight-toolkit/mpq-catalog/";
    internal const string EnvironmentId="reforged-environment";
    private static readonly string[] EnvironmentMembers={"reforged-b","reforged-d","reforged-e"};
    private static readonly Dictionary<string,long[]> ReforgedLengths=new Dictionary<string,long[]> {
        {"reforged-a",new long[]{1748152913}}, {"reforged-b",new long[]{1608961816}}, {"reforged-c",new long[]{2083036611}},
        {"reforged-d",new long[]{1648472586}}, {"reforged-e",new long[]{719685533}}, {"reforged-g",new long[]{774538222}},
        {"reforged-i",new long[]{162091469}}, {"reforged-l",new long[]{37330631,53272447}}, {"reforged-m",new long[]{438564005}},
        {"reforged-n",new long[]{201362}}, {"reforged-p",new long[]{7272085}}, {"reforged-s",new long[]{262721653}},
        {"reforged-t",new long[]{499342621,541957195}}, {"reforged-u",new long[]{1662548731}}
    };
    private static readonly MpqCatalogEntry[] Entries={
        Reforged('A',"Player Characters & NPCs","Use the legacy Turtle WoW module from #legacy-patches. Custom OctoWoW models may be affected."),
        Reforged('B',"Buildings","Updates buildings and architecture. Use with Doodads and Environment."),
        Reforged('C',"Creatures","Imports Patch C as Patch V to load after OctoWoW raid visuals. If you get Error #132, switch this pack off."),
        Reforged('D',"Doodads","Updates world props and their textures. Use with Buildings and Environment."),
        Reforged('E',"Environment","Updates terrain and includes Fog Pushback. Use with Buildings and Doodads."),
        Reforged('G',"Gear & Weapons","Updates equipment and weapon models."),
        Reforged('I',"Interface","Legacy Turtle WoW interface, login artwork, and selection circles. Switch off to restore the OctoWoW login artwork."),
        Reforged('L',"Character Models (Optional)","Optional female character models. Choose one Patch L variant and enable Player Characters & NPCs."),
        Reforged('M',"Maps & Loading Screens","Use the legacy Turtle WoW module from #legacy-patches. OctoWoW-specific map changes may be affected."),
        Reforged('N',"Darker Nights","Reduces nighttime lighting."),
        Reforged('P',"Spell Particle Effects","Updates spell particles and effects."),
        Reforged('S',"Sounds & Music","Updates sound effects and music."),
        Reforged('T',"HD Character Textures","Choose standard or Ultra Base. Enable the Characters and Gear packs. Ultra HD needs Ultra Base."),
        Reforged('U',"Ultra HD Textures","Requires the Characters and Gear packs and the Ultra Base variant of HD Character Textures."),
        new MpqCatalogEntry("pretty-night-sky","Pretty Night Sky","Adds a star-filled night sky.","visual",'Z',VisualSource,
            DriveDownload+"1qu99ZS-SQFfTtYodBmZWYiHmxL8QtUY4",1390151,"b72e5837139772dff5e436c6a6375910fcb5c34ae5742512968463425c4520dd"),
        new MpqCatalogEntry("epoch-water","Epoch Water","Updates water textures and reflections.","visual",'W',VisualSource,
            DriveDownload+"1xRx9OrznbgbE1uBae3H3OGke9UoXtzmU",20411526,"30a969c185cc082a0eb158a7901de889af815426c1e5baec373649688b3151aa"),
        new MpqCatalogEntry("fog-pushback","Fog Pushback","Pushes distant fog back. Reforged Environment already includes this effect.","visual",'Y',VisualSource,
            DriveDownload+"14aHvyfr_ACL-UURbNa_fXRPcfQZoIw8n",51281,"c59b87f7e9425ee890e2b869ea0d26cde84a68e3ee1004e18bb6a29b294c3e0d"),
        new MpqCatalogEntry("pink-herbs","Pink Herbs","Turns many herb models bright pink so they are easier to spot.","visual",'H',"https://github.com/seacrabsam/patch-herb",
            "https://raw.githubusercontent.com/seacrabsam/patch-herb/main/patch-H.mpq",575628,"45b6b6795465a93104de213e0a36e1290b005a852cd6d03a8d21103efaefa0a7")
    };

    private static MpqCatalogEntry Reforged(char slot,string name,string description) {
        string source="AIM".IndexOf(slot)>=0?ReforgedLegacySource:ReforgedSource;
        string archive="patch-"+slot+".mpq";
        string normal=ReforgedAssets+archive,alternate=ReforgedAssets+"extras/"+archive;
        var entry=new MpqCatalogEntry("reforged-"+Char.ToLowerInvariant(slot),name,description,"reforged",slot=='C'?'V':slot,source);
        switch(slot) {
            case 'L': entry.DownloadLinks=new[]{new MpqCatalogLink("regular",normal),new MpqCatalogLink("lessThicc",alternate)};break;
            case 'T': entry.DownloadLinks=new[]{new MpqCatalogLink("standard",alternate),new MpqCatalogLink("ultraBase",normal)};break;
            default: entry.DownloadLinks=new[]{new MpqCatalogLink("download",normal)};break;
        }
        return entry;
    }

    internal static MpqCatalogEntry Find(string id) {
        var entry=Entries.FirstOrDefault(item=>item.Id==id);
        if(entry==null)throw new IOException("This mod is no longer listed in Discover.");
        return entry;
    }

    internal static bool IsSourceLink(string url) {
        return url==RaidVisualsSource||Entries.Any(entry=>entry.SourceUrl==url||entry.DownloadLinks.Any(link=>link.Url==url));
    }

    internal static object[] List(string game,MpqPatchState[] files) {
        var entries=Entries.Select(entry=>{
            bool existing;
            var file=InstalledArchive(game,entry,files,out existing);
            var occupied=file==null?OccupiedArchive(entry,files):null;
            return (object)new{entry.Id,entry.Name,entry.Description,entry.Group,entry.SourceUrl,entry.DownloadLinks,
                Slot=file==null?entry.Slot:file.Name,Installed=file!=null,Enabled=file!=null&&file.Enabled,Existing=existing,
                Blocked=occupied!=null,OccupiedSlot=occupied==null?null:occupied.Name,OccupiedEnabled=occupied!=null&&occupied.Enabled,
                Downloadable=entry.AssetUrl!=null};
        });
        var raid=files.FirstOrDefault(file=>file.Name.Equals(MpqPatches.RaidVisualsName,StringComparison.OrdinalIgnoreCase));
        if(raid==null)return entries.ToArray();
        return entries.Concat(new object[]{new{
            Id="rook-raidvisuals",Name="Rook's RaidVisuals",Description="Changes spell effects and sounds to make raid mechanics easier to see.",
            Group="visual",Slot=raid.Name,SourceUrl=RaidVisualsSource,DownloadLinks=new MpqCatalogLink[0],
            Installed=true,Enabled=raid.Enabled,Existing=false,Blocked=false,OccupiedSlot=(string)null,OccupiedEnabled=false,Downloadable=false
        }}).ToArray();
    }

    private static MpqPatchState OccupiedArchive(MpqCatalogEntry entry,MpqPatchState[] files) {
        return files.FirstOrDefault(file=>file.Name.Equals(entry.Slot,StringComparison.OrdinalIgnoreCase))??
            (entry.Id=="reforged-c"?files.FirstOrDefault(file=>file.Name.Equals("patch-C.mpq",StringComparison.OrdinalIgnoreCase)):null);
    }

    private static MpqPatchState InstalledArchive(string game,MpqCatalogEntry entry,MpqPatchState[] files,out bool existing) {
        var primary=files.FirstOrDefault(file=>file.Name.Equals(entry.Slot,StringComparison.OrdinalIgnoreCase));
        existing=false;
        if(primary!=null&&OwnsFile(game,entry,primary))return primary;
        string legacy=entry.Id=="reforged-c"?"patch-C.mpq":entry.Id=="pink-herbs"?"patch-V.mpq":null;
        var candidates=files.Where(file=>file==primary||file.Name.Equals(legacy,StringComparison.OrdinalIgnoreCase));
        var found=candidates.FirstOrDefault(file=>MatchesPublishedArchive(game,entry,file));
        existing=found!=null;
        return found;
    }

    private static bool MatchesPublishedArchive(string game,MpqCatalogEntry entry,MpqPatchState state) {
        string path=ArchivePath(game,state);
        try {
            ToolkitInstall.CheckForLinks(game,path);
            long length=new FileInfo(path).Length;
            long[] lengths;
            bool known=ReforgedLengths.TryGetValue(entry.Id,out lengths)?lengths.Contains(length):length==entry.Length;
            return known&&MpqPatches.HasHeader(path);
        } catch(IOException) {return false;}
        catch(UnauthorizedAccessException) {return false;}
    }

    internal static MpqImport[] PlanEnvironment(string game,string[] paths) {
        var entries=EnvironmentMembers.Select(Find).ToArray();
        var supplied=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string path in paths) {
            string name=Path.GetFileName(path);
            if(!entries.Any(entry=>entry.Slot.Equals(name,StringComparison.OrdinalIgnoreCase))||supplied.ContainsKey(name))
                throw new IOException("Select the Reforged Buildings (B), Doodads (D), and Environment (E) archives for this set.");
            supplied.Add(name,path);
        }
        var files=MpqPatches.List(game);
        var imports=new List<MpqImport>();
        foreach(var entry in entries) {
            bool existing;
            if(InstalledArchive(game,entry,files,out existing)!=null)continue;
            string path;
            if(!supplied.TryGetValue(entry.Slot,out path))throw new IOException("Select every missing environment archive together, including "+entry.Slot+".");
            imports.Add(MpqPatches.PlanCatalog(game,entry,path));
        }
        return imports.ToArray();
    }

    internal static void SetEnvironmentEnabled(string game,bool enabled,Action verifyGameClosed=null) {
        using(OctoPatch.OpenPatchLock(game)) {
            (verifyGameClosed??ToolkitInstall.EnsureClosed)();
            var files=MpqPatches.List(game);
            var members=EnvironmentMembers.Select(id=>{
                bool existing;
                return InstalledArchive(game,Find(id),files,out existing);
            }).ToArray();
            if(enabled&&members.Any(file=>file==null))throw new IOException("Import all three Reforged environment archives before enabling the set.");
            MpqPatches.SwitchPatches(game,members.Where(file=>file!=null).Select(file=>file.Name),enabled);
        }
    }

    private static string Receipt(MpqCatalogEntry entry,FileInfo file) {
        return entry.Id+"\n"+file.Length.ToString(CultureInfo.InvariantCulture)+"\n"+file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    private static string ArchivePath(string game,MpqPatchState state) {
        return ToolkitPackages.SafePath(game,"Data/"+state.Name+(state.Enabled?"":MpqPatches.DisabledSuffix));
    }

    private static bool OwnsFile(string game,MpqCatalogEntry entry,MpqPatchState state) {
        string receipt=ToolkitPackages.SafePath(game,ReceiptFolder+entry.Id+".txt");
        string target=ArchivePath(game,state);
        try {
            ToolkitInstall.CheckForLinks(game,receipt);
            ToolkitInstall.CheckForLinks(game,target);
            var info=new FileInfo(receipt);
            return info.Exists&&info.Length<256&&File.ReadAllText(receipt)==Receipt(entry,new FileInfo(target));
        } catch(IOException) {return false;}
        catch(UnauthorizedAccessException) {return false;}
    }

    internal static bool IsReceipt(string path) {
        return Entries.Any(entry=>String.Equals(path,ReceiptFolder+entry.Id+".txt",StringComparison.OrdinalIgnoreCase));
    }

    internal static void Record(string game,string stage,MpqImport item,string target,List<KeyValuePair<string,string>> changes) {
        var entry=Find(item.CatalogId);
        string name=entry.Id+".txt";
        string receipt=ToolkitPackages.SafePath(game,ReceiptFolder+name);
        string backup=ToolkitPackages.SafePath(game,".twilight-toolkit/backups/"+Path.GetFileName(stage)+"/"+name);
        ToolkitInstall.CheckForLinks(game,receipt);
        ToolkitInstall.CheckForLinks(game,backup);
        string staged=Path.Combine(stage,name);
        File.WriteAllText(staged,Receipt(entry,new FileInfo(target)));
        ToolkitInstall.Replace(staged,receipt,backup,changes,consumeSource:true);
    }

    internal static byte[] Download(MpqCatalogEntry entry) {
        if(entry.AssetUrl==null)throw new IOException("Choose a local MPQ file for this mod.");
        var bytes=ToolkitPackages.Download(entry.AssetUrl,entry.Length,repositoryServer:new Uri(entry.AssetUrl));
        if(bytes.Length!=entry.Length||ToolkitPackages.Hash(bytes)!=entry.Sha256)
            throw new IOException("The downloaded archive does not match the published checksum. No patch was installed.");
        return bytes;
    }
}
