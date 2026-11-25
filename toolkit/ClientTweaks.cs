using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed partial class ClientTweaks {
    internal bool Enabled, LargeAddress, BackgroundSounds, AlwaysAutoLoot;
    internal bool LoginChange, LoginEnabled;
    internal Dictionary<string,bool> ModChanges=new Dictionary<string,bool>();
    internal bool PresenceChange,PresenceEnabled;
    internal int PresenceFlags;
    internal bool UnsafeLimits, CameraSkipFix, UnlimitedScriptMemory, CrossFactionRes, CustomGlues, DepCompatibility, ClearWdb;
    internal bool PatchSoundChannels;
    internal int SoundChannels=64;
    internal decimal Nameplates, FieldOfView, Render, Clutter, Camera;
    // Build 5875 patch offsets. Current OctoWoW clients include additional data;
    // fingerprint the complete image while normalizing only supported settings.
    private const string ModernizedHash = "1ad469d940114941a605e9a3463b5e91df7c540ce85ad896aa13eb52620ea25d";
    private const string OctoClientFingerprint="b8b3a0010ae42c716e1c6bb0ed315bf2896240483c287c7039a3938c663be33f";
    internal static byte[] ValidateClient(string game) {
        string source=Path.Combine(game,"WoW_Modernized.exe");
        if(!File.Exists(source))throw new IOException("Client Tweaks require the supported WoW_Modernized.exe build. No changes were applied.");
        byte[] image=File.ReadAllBytes(source);
        ValidateImage(game,image);
        return image;
    }
    internal static void ValidateImage(string game,byte[] image) {
        const string message="Client Tweaks could not verify the 1.12 patch locations in this executable. No changes were applied.";
        if(image.Length<0x48e000||image[0]!=0x4d||image[1]!=0x5a)throw new IOException(message);
        int pe=BitConverter.ToInt32(image,0x3c);
        if(pe<0||pe>image.Length-96||BitConverter.ToUInt32(image,pe)!=0x4550||BitConverter.ToUInt16(image,pe+24)!=0x10b)throw new IOException(message);
        string tracked=Path.Combine(game,".twilight-toolkit","toolkit-client.sha256");
        string current=ToolkitPackages.Hash(image);
        if(current==ModernizedHash)return;
        if(image.Length==4927488&&File.Exists(tracked)&&File.ReadAllText(tracked).Trim()==current)return;
        if(pe!=0x110||BitConverter.ToUInt16(image,pe+4)!=0x14c)throw new IOException(message);
        byte[] normalized=(byte[])image.Clone();
        foreach(int offset in new[]{0x4089b4,0x40fed8,0x467958,0x40c448,0x4089a4,0x435d38})Array.Clear(normalized,offset,4);
        normalized[0x126]&=0xdf;normalized[pe+24+0x47]&=0xfe;
        foreach(int offset in new[]{0xc1ecf,0xc2b25,0x2067de,0x3a4869})normalized[offset]=0;
        // Retain known existing compatibility options from other client tools.
        NormalizeVariant(normalized,0x253c,"33f68bff8b04b5","be00000000eb1f");
        NormalizeVariant(normalized,0x1b2115,"a1a4a2c200","b853556e65");
        if(ToolkitPackages.Hash(normalized)!=OctoClientFingerprint)throw new IOException(message);
    }
    private static void NormalizeVariant(byte[] image,int offset,string original,string patched) {
        byte[] baseline=Enumerable.Range(0,original.Length/2).Select(i=>Convert.ToByte(original.Substring(i*2,2),16)).ToArray();
        byte[] alternate=Enumerable.Range(0,patched.Length/2).Select(i=>Convert.ToByte(patched.Substring(i*2,2),16)).ToArray();
        if(image.Skip(offset).Take(alternate.Length).SequenceEqual(alternate))Buffer.BlockCopy(baseline,0,image,offset,baseline.Length);
    }
    internal void Stage(string game,string staging) {
        if(!Enabled)return;
        ValidateLimits();
        if(BinaryFields.Any(Includes))StageImage(game,staging);
        if(Includes("render")||Includes("clutter")||Includes("camera")||Includes("scriptMemory"))StageConfig(game,staging);
        if(Includes("clearWdb"))ToolkitPackages.Save(staging,Path.Combine(".twilight-toolkit","clear-wdb.txt"),Encoding.ASCII.GetBytes(ClearWdb?"1":"0"));
        if(Includes("safetyLimits"))ToolkitPackages.Save(staging,SafetyPath,Encoding.ASCII.GetBytes(UnsafeLimits?"0":"1"));
    }

    private void StageImage(string game,string staging) {
        const string exe="WoW_Modernized.exe";
        const string record="toolkit-client.sha256";
        byte[] image=ValidateClient(game);
        if(Includes("fov"))WriteFloat(image,0x4089B4,(float)((double)FieldOfView*Math.PI/180));
        if(Includes("render"))WriteFloat(image,0x40FED8,(float)Render);
        if(Includes("clutter"))WriteFloat(image,0x467958,(float)Clutter);
        if(Includes("plates"))WriteFloat(image,0x40C448,(float)Nameplates);
        if(Includes("camera"))WriteFloat(image,0x4089A4,(float)Camera);
        if(Includes("channels")&&PatchSoundChannels) {
            byte[] value=Encoding.ASCII.GetBytes(SoundChannels.ToString(CultureInfo.InvariantCulture));
            Array.Clear(image,0x435d38,4);Buffer.BlockCopy(value,0,image,0x435d38,value.Length);
        }
        ExtraClientPatches.Apply(image,this);
        if(Includes("laa")&&(LargeAddress||ExplicitChanges))
            image[0x126]=LargeAddress?(byte)(image[0x126]|0x20):(byte)(image[0x126]&~0x20);
        if(Includes("audio")) {
            if(BackgroundSounds)image[0x3A4869]=0x27;
            else if(ExplicitChanges&&BackgroundSoundEnabled(image))throw new IOException("Background Sounds is built into this executable. Restore a previous version to change it.");
        }
        if(Includes("autoLoot")&&(AlwaysAutoLoot||ExplicitChanges)) {
            byte instruction=AlwaysAutoLoot?(byte)0x75:(byte)0x74;
            image[0x0C1ECF]=instruction;
            image[0x0C2B25]=instruction;
        }
        ToolkitPackages.Save(staging,exe,image);
        ToolkitPackages.Save(staging,Path.Combine(".twilight-toolkit",record),Encoding.ASCII.GetBytes(ToolkitPackages.Hash(image)));
    }

    private void StageConfig(string game,string staging) {
        string settings=Path.Combine("WTF","Config.wtf");
        string file=Path.Combine(game,settings);
        var lines=File.Exists(file)?File.ReadAllLines(file).ToList():new List<string>();
        if(Includes("render"))Set(lines,"farclip",Render);
        if(Includes("clutter"))Set(lines,"frillDistance",Clutter);
        if(Includes("camera"))Set(lines,"cameraDistanceMax",Camera);
        if(Includes("scriptMemory")) {
            int? existing=ScriptMemory(lines);
            if(UnlimitedScriptMemory) {
                if(existing!=0)ToolkitPackages.Save(staging,ScriptMemoryPath,Encoding.ASCII.GetBytes(existing.HasValue?existing.Value.ToString(CultureInfo.InvariantCulture):""));
                Set(lines,"scriptMemory",0);
            } else if(ExplicitChanges&&existing==0) {
                string previous=Path.Combine(game,ScriptMemoryPath);
                int cap;
                if(File.Exists(previous)&&Int32.TryParse(File.ReadAllText(previous),NumberStyles.None,CultureInfo.InvariantCulture,out cap)&&cap>0)
                    Set(lines,"scriptMemory",cap);
                else Remove(lines,"scriptMemory");
            }
        }
        ToolkitPackages.Save(staging,settings,Encoding.UTF8.GetBytes(String.Join(Environment.NewLine,lines)+Environment.NewLine));
    }
    internal void ValidateLimits() {
        Limit("Render distance",Render,777,UnsafeLimits?3000:1500);
        Limit("Ground clutter",Clutter,0,UnsafeLimits?1000:300);
        Limit("Nameplate distance",Nameplates,1,UnsafeLimits?100:41);
        Limit("Camera zoom",Camera,1,100);
        Limit("Field of view",FieldOfView,60,UnsafeLimits?150:130);
        if(PatchSoundChannels)Limit("Audio channels",SoundChannels,1,UnsafeLimits?256:64);
    }
    private static void Limit(string label,decimal value,decimal min,decimal max) {
        if(value<min||value>max)throw new IOException(label+" must be between "+min+" and "+max+" with the selected Safety Limits setting.");
    }
    private static void Remove(List<string> lines,string key) {
        lines.RemoveAll(line=>Regex.IsMatch(line,@"^\s*SET\s+"+Regex.Escape(key)+@"\s",RegexOptions.IgnoreCase));
    }
    private static void Set(List<string> lines,string key,decimal value) {
        Remove(lines,key);
        lines.Add("SET "+key+" \""+value.ToString(CultureInfo.InvariantCulture)+"\"");
    }
    private static void WriteFloat(byte[] target,int index,float value) { Buffer.BlockCopy(BitConverter.GetBytes(value),0,target,index,4); }

    internal sealed class RenderAdvice {
        public int distance { get; set; }
        public int ramGiB { get; set; }
        public int threads { get; set; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus {
        public uint Length,Load;
        public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    internal static RenderAdvice CheckPc() {
        var memory=new MemoryStatus();
        memory.Length=(uint)Marshal.SizeOf(typeof(MemoryStatus));
        int ram=GlobalMemoryStatusEx(ref memory)?(int)Math.Min(1024UL,(memory.TotalPhysical+(1UL<<29))/(1UL<<30)):0;
        int threads=Environment.ProcessorCount;
        return new RenderAdvice {distance=RecommendedDistance(ram,threads),ramGiB=ram,threads=threads};
    }
    internal static int RecommendedDistance(int ramGiB,int threads) {
        if(ramGiB>=16&&threads>=8)return 2200;
        if(ramGiB>=8&&threads>=4)return 1500;
        if(ramGiB>=4&&threads>=2)return 1000;
        return 777;
    }
}
