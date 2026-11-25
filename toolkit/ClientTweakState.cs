using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed partial class ClientTweaks {
    private const string SafetyPath=".twilight-toolkit/tweak-safety.txt";
    private const string ScriptMemoryPath=".twilight-toolkit/tweak-script-memory.txt";
    private static readonly string[] BinaryFields={"autoLoot","laa","audio","plates","fov","render","clutter","camera",
        "channels","cameraSkip","crossFactionRes","customGlues","depCompatibility"};
    private HashSet<string> requestedChanges;

    internal bool ExplicitChanges { get {return requestedChanges!=null;} }
    internal bool Includes(string name) {return requestedChanges==null||requestedChanges.Contains(name);}

    internal void SelectChanges(string[] fields) {
        var allowed=new HashSet<string>(BinaryFields.Concat(new[]{"clearWdb","scriptMemory","safetyLimits"}));
        if(fields.Length==0||fields.Any(field=>!allowed.Contains(field)))throw new IOException("Invalid Client Tweaks changes.");
        requestedChanges=new HashSet<string>(fields);
    }

    internal IEnumerable<string> DescribeChanges() {
        if(Includes("autoLoot"))yield return Switch("Always Auto-Loot",AlwaysAutoLoot);
        if(Includes("laa"))yield return Switch("Large Address Aware",LargeAddress);
        if(Includes("audio"))yield return Switch("Background Sounds",BackgroundSounds);
        if(Includes("cameraSkip"))yield return Switch("Fix Camera Skip",CameraSkipFix);
        if(Includes("scriptMemory"))yield return Switch("Unlimited Addon Script Memory",UnlimitedScriptMemory);
        if(Includes("crossFactionRes"))yield return Switch("Cross-Faction Resurrection Fix",CrossFactionRes);
        if(Includes("customGlues"))yield return Switch("Custom Login Screens",CustomGlues);
        if(Includes("depCompatibility"))yield return Switch("DEP Compatibility",DepCompatibility);
        if(Includes("clearWdb"))yield return Switch("Clear WDB on Launch",ClearWdb);
        if(Includes("safetyLimits"))yield return Switch("Safety Limits",!UnsafeLimits);
        if(Includes("render"))yield return "Render Distance · "+Render;
        if(Includes("clutter"))yield return "Ground Clutter · "+Clutter;
        if(Includes("plates"))yield return "Nameplate Distance · "+Nameplates;
        if(Includes("camera"))yield return "Max Camera Zoom · "+Camera;
        if(Includes("fov"))yield return "Field of View · "+FieldOfView+"°";
        if(Includes("channels")&&PatchSoundChannels)yield return "Audio Channels · "+SoundChannels;
    }

    private static string Switch(string name,bool enabled) {return name+(enabled?" · On":" · Off");}

    private sealed class SavedTweakState {
        public bool available {get;set;}
        public Dictionary<string,object> values {get;set;}
        public string[] locked {get;set;}
    }

    internal static object ReadState(string game) {
        var state=new SavedTweakState{values=new Dictionary<string,object>(),locked=new string[0]};
        try {
            byte[] image=ValidateClient(game);
            string settings=Path.Combine(game,"WTF","Config.wtf");
            string safe=Path.Combine(game,SafetyPath);
            decimal render=ReadFloat(image,0x40FED8,777,3000,1500),clutter=ReadFloat(image,0x467958,0,1000,300);
            decimal plates=ReadFloat(image,0x40C448,1,100,30),camera=ReadFloat(image,0x4089A4,1,100,75);
            decimal fov=ReadFloat(image,0x4089B4,60,150,110,180/Math.PI);
            int channels;
            if(!Int32.TryParse(Encoding.ASCII.GetString(image,0x435d38,4).TrimEnd('\0'),NumberStyles.None,CultureInfo.InvariantCulture,out channels)||channels<1||channels>256)channels=64;
            bool audio=BackgroundSoundEnabled(image),cameraSkip=ExtraClientPatches.CameraEnabled(image),glues=ExtraClientPatches.GlueEnabled(image);
            bool safety=!(render>1500||clutter>300||plates>41||fov>130||channels>64||File.Exists(safe)&&File.ReadAllText(safe).Trim()=="0");
            var builtIn=new List<string>();
            if(audio)builtIn.Add("audio");
            if(cameraSkip)builtIn.Add("cameraSkip");
            if(glues)builtIn.Add("customGlues");
            state.values=new Dictionary<string,object>{
                {"autoLoot",image[0x0C1ECF]==0x75&&image[0x0C2B25]==0x75},
                {"laa",(image[0x126]&0x20)!=0},{"audio",audio},{"plates",plates},{"fov",fov},
                {"render",render},{"clutter",clutter},{"camera",camera},{"channels",channels},
                {"cameraSkip",cameraSkip},{"scriptMemory",File.Exists(settings)&&ScriptMemory(File.ReadLines(settings))==0},
                {"crossFactionRes",image[0x2067de]==0},{"customGlues",glues},
                {"depCompatibility",(image[BitConverter.ToInt32(image,0x3c)+24+0x47]&1)==0},
                {"clearWdb",ClientCache.Enabled(game)},{"safetyLimits",safety}
            };
            state.locked=builtIn.ToArray();
            state.available=true;
        } catch(IOException) { }
          catch(UnauthorizedAccessException) { }
        return state;
    }

    private static bool BackgroundSoundEnabled(byte[] image) {return image[0x3A4869]==0x27;}

    private static decimal ReadFloat(byte[] image,int offset,int min,int max,int fallback,double scale=1) {
        double value=Math.Round(BitConverter.ToSingle(image,offset)*scale);
        return value>=min&&value<=max?(decimal)value:fallback;
    }

    private static int? ScriptMemory(IEnumerable<string> settings) {
        int? value=null;
        foreach(string line in settings) {
            Match match=Regex.Match(line,@"^\s*SET\s+scriptMemory\s+""(\d+)""\s*$",RegexOptions.IgnoreCase);
            int cap;
            if(match.Success&&Int32.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out cap))value=cap;
        }
        return value;
    }
}
