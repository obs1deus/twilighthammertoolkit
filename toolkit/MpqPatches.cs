using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class MpqImport {
    internal string Source;
    internal string Name;
    internal string CatalogId;
}

internal sealed class MpqPatchState {
    public string Name {get;set;}
    public bool Enabled {get;set;}
}

internal static class MpqPatches {
    private static readonly Regex PatchName=new Regex(@"\Apatch-([a-z0-9])\.mpq\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private const string Letters="ABCDEFGHIJKLMNPQRSTUVWXYZ";
    internal const string DisabledSuffix=".disabled";
    internal const string RaidVisualsName="patch-O.mpq";

    private static string DataFolder(string game) {
        string data=ToolkitPackages.SafePath(game,"Data");
        ToolkitInstall.CheckForLinks(game,data);
        if(!Directory.Exists(data))throw new IOException("The OctoWoW Data folder could not be found.");
        return data;
    }

    private static bool IsCustomName(string name) {
        var match=PatchName.Match(name);
        return match.Success&&Letters.Contains(Char.ToUpperInvariant(match.Groups[1].Value[0]));
    }

    private static bool IsSwitchableName(string name) {
        return IsCustomName(name)||String.Equals(name,RaidVisualsName,StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsRaidVisualsPath(string path) {
        return String.Equals(Path.GetDirectoryName(path),"Data",StringComparison.OrdinalIgnoreCase)&&
            String.Equals(Path.GetFileName(path),RaidVisualsName,StringComparison.OrdinalIgnoreCase);
    }

    private static string ActiveName(string name) {
        return name.EndsWith(DisabledSuffix,StringComparison.OrdinalIgnoreCase)
            ?name.Substring(0,name.Length-DisabledSuffix.Length):name;
    }

    internal static bool IsCustomPath(string path) {
        return String.Equals(Path.GetDirectoryName(path),"Data",StringComparison.OrdinalIgnoreCase)&&IsSwitchableName(ActiveName(Path.GetFileName(path)));
    }

    internal static MpqPatchState[] List(string game) {
        return Directory.EnumerateFiles(DataFolder(game)).Select(Path.GetFileName)
            .GroupBy(ActiveName,StringComparer.OrdinalIgnoreCase).Where(group=>IsSwitchableName(group.Key))
            .Select(group=>new MpqPatchState {
                Name=group.Key,
                Enabled=group.Contains(group.Key,StringComparer.OrdinalIgnoreCase)
            }).OrderBy(patch=>patch.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void SetEnabled(string game,string name,bool enabled,Action verifyGameClosed=null) {
        using(OctoPatch.OpenPatchLock(game)) {
            (verifyGameClosed??ToolkitInstall.EnsureClosed)();
            SwitchPatches(game,new[]{name},enabled);
        }
    }

    // The caller holds the shared patch lock before inspecting or moving any archive.
    internal static void SwitchPatches(string game,IEnumerable<string> names,bool enabled) {
        string data=DataFolder(game);
        var moves=new List<KeyValuePair<string,string>>();
        foreach(string name in names) {
            if(!IsSwitchableName(name))throw new IOException("Choose a custom MPQ patch.");
            string active=ToolkitPackages.SafePath(data,name),disabled=active+DisabledSuffix;
            ToolkitInstall.CheckForLinks(game,active);
            ToolkitInstall.CheckForLinks(game,disabled);
            string from=enabled?disabled:active,to=enabled?active:disabled;
            if(File.Exists(to)) {
                if(File.Exists(from))throw new IOException("Both switched-on and switched-off copies of "+name+" exist. Move one copy out of Data before switching this patch.");
                continue;
            }
            if(Directory.Exists(to))throw new IOException("A directory occupies the patch location for "+name+".");
            if(!File.Exists(from))throw new IOException(name+" is no longer in the Data folder.");
            moves.Add(new KeyValuePair<string,string>(from,to));
        }
        int moved=0;
        try {
            foreach(var move in moves) {
                File.Move(move.Key,move.Value);
                moved++;
            }
        } catch {
            for(int i=moved-1;i>=0;i--)File.Move(moves[i].Value,moves[i].Key);
            throw;
        }
    }

    internal static MpqImport[] Plan(string game,string[] paths) {
        if(paths.Length==0||paths.Length>Letters.Length)throw new IOException("Choose between 1 and "+Letters.Length+" MPQ files.");
        var occupied=new HashSet<string>(Directory.EnumerateFileSystemEntries(DataFolder(game)).Select(path=>ActiveName(Path.GetFileName(path))),StringComparer.OrdinalIgnoreCase);
        var imports=new List<MpqImport>();
        foreach(string source in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)) {
            string filename=Path.GetFileName(source);
            CheckSource(source);
            var match=PatchName.Match(filename);
            string name=null;
            if(match.Success) {
                char letter=Char.ToUpperInvariant(match.Groups[1].Value[0]);
                if(!Letters.Contains(letter))throw new IOException(filename+" uses a reserved game or catalog patch name.");
                name="patch-"+letter+".mpq";
                if(!occupied.Add(name))throw new IOException(name+" already exists in the Data folder.");
            }
            if(filename.Equals("patch.mpq",StringComparison.OrdinalIgnoreCase))
                throw new IOException(filename+" is reserved for the base game patch.");
            imports.Add(new MpqImport{Source=source,Name=name});
        }
        foreach(var item in imports.Where(item=>item.Name==null)) {
            item.Name=Letters.Select(letter=>"patch-"+letter+".mpq").FirstOrDefault(occupied.Add);
            if(item.Name==null)throw new IOException("The Data folder has no free custom patch slots.");
        }
        return imports.ToArray();
    }

    internal static MpqImport PlanCatalog(string game,MpqCatalogEntry entry,string path) {
        string source=Path.GetFullPath(path);
        string data=DataFolder(game);
        string target=ToolkitPackages.SafePath(data,entry.Slot);
        string disabled=target+DisabledSuffix;
        if(!IsCustomName(entry.Slot)||File.Exists(target)||Directory.Exists(target)||File.Exists(disabled)||Directory.Exists(disabled))
            throw new IOException(entry.Slot+" is already in use. Manage the existing file in Audio & Visual before importing this mod.");
        if(entry.Id=="reforged-c")CheckCreatureSlot(data);
        if(entry.Group=="reforged"&&!Path.GetFileName(source).Equals("patch-"+entry.Id.Last()+".mpq",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose the Project Reforged Patch "+Char.ToUpperInvariant(entry.Id.Last())+" archive for this mod.");
        CheckSource(source);
        return new MpqImport{Source=source,Name=entry.Slot,CatalogId=entry.Id};
    }

    private static void CheckCreatureSlot(string data) {
        string original=Path.Combine(data,"patch-C.mpq");
        if(File.Exists(original)||File.Exists(original+DisabledSuffix))
            throw new IOException("Patch C is already in Data. Manage the existing archive before importing the Creatures pack as Patch V.");
    }

    internal static void Install(string game,MpqImport[] imports,Action verifyGameClosed=null) {
        using(OctoPatch.OpenPatchLock(game))InstallPatches(game,imports,verifyGameClosed??ToolkitInstall.EnsureClosed);
    }

    private static void InstallPatches(string game,MpqImport[] imports,Action ensureClosed) {
        ensureClosed();
        string data=DataFolder(game);
        string stage=ToolkitPackages.SafePath(game,Path.Combine(".twilight-toolkit","mpq-import-"+Guid.NewGuid().ToString("N")));
        ToolkitInstall.CheckForLinks(game,stage);
        Directory.CreateDirectory(stage);
        var changes=new List<KeyValuePair<string,string>>();
        try {
            foreach(var item in imports) {
                string file=ToolkitPackages.SafePath(stage,item.Name);
                using(var input=File.OpenRead(item.Source))
                using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    CheckHeader(input,Path.GetFileName(item.Source));
                    input.CopyTo(output);
                }
            }
            ensureClosed();
            foreach(var item in imports) {
                if(item.CatalogId=="reforged-c")CheckCreatureSlot(data);
                string target=ToolkitPackages.SafePath(data,item.Name);
                ToolkitInstall.CheckForLinks(game,target);
                string disabled=target+DisabledSuffix;
                ToolkitInstall.CheckForLinks(game,disabled);
                if(File.Exists(disabled)||Directory.Exists(disabled))
                    throw new IOException(item.Name+" already has a disabled copy in the Data folder.");
                File.Move(Path.Combine(stage,item.Name),target);
                changes.Add(new KeyValuePair<string,string>(target,null));
                if(item.CatalogId!=null)MpqDiscovery.Record(game,stage,item,target,changes);
            }
            ToolkitHistory.Record(game,"Import MPQ patches",changes);
        } catch {
            ToolkitInstall.Restore(changes);
            throw;
        } finally {
            try {Directory.Delete(stage,true);}
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }

    private static void CheckSource(string source) {
        string name=Path.GetFileName(source);
        if(!Path.GetExtension(name).Equals(".mpq",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose MPQ files for patch import.");
        using(var input=File.OpenRead(source))CheckHeader(input,name);
    }

    internal static bool HasHeader(string path) {
        using(var input=File.OpenRead(path))return ValidHeader(input);
    }

    private static void CheckHeader(Stream input,string name) {
        if(!ValidHeader(input))throw new IOException(name+" does not have an MPQ archive header.");
        input.Position=0;
    }

    private static bool ValidHeader(Stream input) {
        if(input.Length<32||input.ReadByte()!='M'||input.ReadByte()!='P'||input.ReadByte()!='Q')return false;
        int type=input.ReadByte();
        return type==0x1a||type==0x1b;
    }
}
