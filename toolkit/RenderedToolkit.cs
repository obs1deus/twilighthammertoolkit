using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class ToolkitBrowserRuntime {
    private static readonly Assembly Bundle=typeof(ToolkitBrowserRuntime).Assembly;
    private static readonly Dictionary<string,Assembly> Loaded=new Dictionary<string,Assembly>();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr LoadLibrary(string file);

    internal static void Prepare() {
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args) {
            string name=new AssemblyName(args.Name).Name;
            if(name!="Microsoft.Web.WebView2.Core"&&name!="Microsoft.Web.WebView2.WinForms")return null;
            lock(Loaded) {
                Assembly assembly;
                if(Loaded.TryGetValue(name,out assembly))return assembly;
                using(var stream=Bundle.GetManifestResourceStream(name+".dll"))
                using(var bytes=new MemoryStream()) {
                    stream.CopyTo(bytes);
                    assembly=Assembly.Load(bytes.ToArray());
                    Loaded.Add(name,assembly);
                    return assembly;
                }
            }
        };
        string arch=IntPtr.Size==4?"x86":Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")=="ARM64"?"arm64":"x64";
        string folder=Path.Combine(LocalData,"WebView2 SDK 1.0.2903.40",arch);
        Directory.CreateDirectory(folder);
        string library=Path.Combine(folder,"WebView2Loader.dll");
        using(var input=Bundle.GetManifestResourceStream("WebView2Loader."+arch+".dll"))
        using(var buffer=new MemoryStream()) {
            input.CopyTo(buffer);
            byte[] bytes=buffer.ToArray();
            if(!File.Exists(library)||!File.ReadAllBytes(library).SequenceEqual(bytes)){
                string temporary=library+"."+Guid.NewGuid().ToString("N")+".tmp";
                try {
                    File.WriteAllBytes(temporary,bytes);
                    if(File.Exists(library))File.Replace(temporary,library,null);
                    else File.Move(temporary,library);
                } finally {if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        if(LoadLibrary(library)==IntPtr.Zero)throw new IOException("Microsoft WebView2 could not be loaded.");
    }
    internal static string LocalData {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerToolkit");}}
}

internal sealed class RenderedToolkit : Form {
    private const string Portal="https://twilighthammer.hccpride.workers.dev/";
    private const string InterfaceUrl="https://toolkit.internal/";
    private static readonly string GameFolderSetting=Path.Combine(ToolkitBrowserRuntime.LocalData,"game-folder.txt");
    private readonly WebView2 browser=new WebView2();
    private readonly JavaScriptSerializer json=new JavaScriptSerializer();
    private readonly Dictionary<string,Package> catalog=ToolkitPackages.Catalog.ToDictionary(p=>p.Name);
    private readonly HashSet<string> addonSourceLinks=new HashSet<string>(StringComparer.Ordinal);
    private bool busy;
    private bool checkingUpdates;
    private int updateEpoch;
    private const string ReloadHint="Run /reload in OctoWoW. If a newly installed addon is missing, restart the game.";
    private TaskCompletionSource<bool> installReview;
    private string reviewConfirmation;
    private double cssToWindowScale;
    private string releaseUrl;
    private Release toolkitRelease;
    private CancellationTokenSource patchVerification;
    private CancellationTokenSource patchDownload;
    internal string StartupError;

    internal RenderedToolkit() {
        Text="Twilight Hammer Launcher v"+ToolkitPackages.Version;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.CenterScreen;
        var area=Screen.PrimaryScreen.WorkingArea;
        ClientSize=new Size(Math.Min(1100,area.Width-16),Math.Min(865,area.Height-20));
        MinimumSize=new Size(650,540);
        BackColor=Color.FromArgb(24,21,34);
        browser.Dock=DockStyle.Fill;
        browser.DefaultBackgroundColor=BackColor;
        Controls.Add(browser);
        Shown+=async delegate {
            try {await Initialize();}
            catch(Exception error) {StartupError=error.Message;Close();}
        };
    }
    private async Task Initialize() {
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(ToolkitBrowserRuntime.LocalData,"Browser"));
        await browser.EnsureCoreWebView2Async(environment);
        var view=browser.CoreWebView2;
        view.Settings.AreDefaultContextMenusEnabled=false;
        view.Settings.AreDevToolsEnabled=false;
        view.Settings.IsZoomControlEnabled=false;
        view.Settings.AreBrowserAcceleratorKeysEnabled=false;
        view.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){e.Cancel=e.Uri!=InterfaceUrl;};
        view.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {
            e.Handled=true;
            if(e.Uri==Portal||e.Uri==ToolkitPackages.GitHub||OctoPatchNotes.IsOfficialLink(e.Uri)||addonSourceLinks.Contains(e.Uri)||MpqDiscovery.IsSourceLink(e.Uri))Open(e.Uri);
        };
        view.WebMessageReceived+=OnMessage;
        byte[] document;
        using(var stream=GetType().Assembly.GetManifestResourceStream("Toolkit.html"))
        using(var reader=new StreamReader(stream)) {
            var dependencies=catalog.Values.Where(p=>p.Requires.Length>0).ToDictionary(p=>p.Name,p=>p.Requires);
            var addons=catalog.Values.Where(p=>p.Folder!="game"&&p.Folder!="Data"&&p.Folder!="sidekick"&&p.Folder!="client-login").Select(p=>p.Name).ToArray();
            var sources=catalog.Values.Where(p=>addons.Contains(p.Name)||p.Folder=="Data")
                .ToDictionary(p=>p.Name,p=>ToolkitPackages.AddonSourceUrl(p.SourceUrl));
            sources["TwilightHammerSync"]=ToolkitPackages.GitHub;
            addonSourceLinks.UnionWith(sources.Values.Where(source=>source!=null));
            document=Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace("{{VERSION}}",ToolkitPackages.Version)
                .Replace("{{IMPORTED_CATALOG}}",json.Serialize(ImportedCatalog.Packages.Select(p=>new { p.Name,p.Group,p.Help })))
                .Replace("{{DEPENDENCIES}}",json.Serialize(dependencies)).Replace("{{ADDON_COMPONENTS}}",json.Serialize(addons))
                .Replace("{{ADDON_SOURCES}}",json.Serialize(sources))
                .Replace("{{FRAME}}",nativeFrame?"native":"fallback"));
        }
        view.AddWebResourceRequestedFilter(InterfaceUrl+"*",CoreWebView2WebResourceContext.All);
        view.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){
            string resource;
            string mime;
            switch(e.Request.Uri) {
                case InterfaceUrl:
                    e.Response=environment.CreateWebResourceResponse(new MemoryStream(document,false),200,"OK","Content-Type: text/html; charset=utf-8");
                    return;
                case InterfaceUrl+"launcher-locales.js": resource="LauncherLocales.js";mime="application/javascript; charset=utf-8";break;
                case InterfaceUrl+"launcher-hero.jpg": resource="LauncherHero.jpg";mime="image/jpeg";break;
                case InterfaceUrl+"launcher-horde.jpg": resource="LauncherHorde.jpg";mime="image/jpeg";break;
                case InterfaceUrl+"launcher-octo.jpg": resource="LauncherOcto.jpg";mime="image/jpeg";break;
                case InterfaceUrl+"launcher-yshaarj.jpg": resource="LauncherYshaarj.jpg";mime="image/jpeg";break;
                case InterfaceUrl+"launcher-cthun.jpg": resource="LauncherCthun.jpg";mime="image/jpeg";break;
                case InterfaceUrl+"launcher-nzoth.jpg": resource="LauncherNzoth.jpg";mime="image/jpeg";break;
                case InterfaceUrl+"launcher-turtle.jpg": resource="LauncherTurtle.jpg";mime="image/jpeg";break;
                case InterfaceUrl+"launcher-hammers.png": resource="LauncherHammers.png";mime="image/png";break;
                case InterfaceUrl+"octowow-logo.webp": resource="OctoWoWLogo.webp";mime="image/webp";break;
                default:
                    if(e.Request.Uri.StartsWith(InterfaceUrl+"icons/",StringComparison.Ordinal)) {
                        resource="icons."+e.Request.Uri.Substring((InterfaceUrl+"icons/").Length);
                        if(GetType().Assembly.GetManifestResourceInfo(resource)!=null) {
                            mime="image/jpeg";
                            break;
                        }
                    }
                    e.Response=environment.CreateWebResourceResponse(null,404,"Not Found","");
                    return;
            }
            e.Response=environment.CreateWebResourceResponse(GetType().Assembly.GetManifestResourceStream(resource),200,"OK","Content-Type: "+mime);
        };
        view.Navigate(InterfaceUrl);
    }
    private async void OnMessage(object sender,CoreWebView2WebMessageReceivedEventArgs args) {
        if(args.Source!=InterfaceUrl)return;
        try {
            var request=json.Deserialize<Dictionary<string,object>>(args.WebMessageAsJson);
            string game=TextField(request,"game");
            switch(TextField(request,"action")) {
                case "ready":
                    string initialGame=ExistingPath();
                    Post(new {game=initialGame,gameValid=IsGameFolder(initialGame),savedGameFolder=File.Exists(GameFolderSetting),toolkitFolder=AppDomain.CurrentDomain.BaseDirectory});
                    break;
                case "fit": FitContent(request);break;
                case "pc-check": Post(new {renderAdvice=ClientTweaks.CheckPc()});break;
                case "close": if(!busy)Close();break;
                case "minimize": WindowState=FormWindowState.Minimized;break;
                case "drag": ReleaseCapture();SendMessage(Handle,0x00a1,new IntPtr(2),IntPtr.Zero);break;
                case "resize":
                    int edge;
                    if(Int32.TryParse(TextField(request,"edge"),out edge)&&edge>=10&&edge<=17){
                        ReleaseCapture();SendMessage(Handle,0x00a1,new IntPtr(edge),IntPtr.Zero);
                    }
                    break;
                case "browse": if(!busy)Browse(game);break;
                case "save-folder": if(!busy)SaveGameFolder(game);break;
                case "list-mpq": if(!busy)await ListMpqPatches(game);break;
                case "browse-mpq": if(!busy)await BrowseMpqPatches(game);break;
                case "browse-discover-mpq":
                case "download-discover-mpq":
                    await InstallDiscoveredMpq(game,TextField(request,"id"),TextField(request,"action")=="download-discover-mpq");
                    break;
                case "browse-discover-mpq-set":
                    if(TextField(request,"id")==MpqDiscovery.EnvironmentId)await BrowseEnvironmentPatches(game);
                    break;
                case "import-mpq":
                    if(busy){
                        Post(new{mpqGame=game,mpqStatus="Wait for the current launcher task to finish, then drop the files again."});
                        break;
                    }
                    await ImportMpqPatches(game,args.AdditionalObjects.Select(item=>{
                        var file=item as FileInfo;
                        if(file!=null)return file.FullName;
                        var webFile=item as CoreWebView2File;
                        return webFile==null?null:webFile.Path;
                    }).Where(path=>path!=null).ToArray());
                    break;
                case "toggle-mpq":
                    if(busy)Post(new{mpqGame=game,mpqStatus="Wait for the current launcher task to finish, then try again."});
                    else await ToggleMpqPatch(game,TextField(request,"patch"),IsChecked(request,"enabled"));
                    break;
                case "toggle-discover-mpq-set":
                    if(busy)Post(new{mpqGame=game,mpqStatus="Wait for the current launcher task to finish, then try again."});
                    else if(TextField(request,"id")==MpqDiscovery.EnvironmentId)
                        await ToggleMpqPatch(game,"Reforged Environment Set",IsChecked(request,"enabled"),environmentSet:true);
                    break;
                case "apply-client-mods": if(!busy)await ApplyClientMods(request,GamePath(game));break;
                case "play": if(!busy)await LaunchGame(game,TextField(request,"client"));break;
                case "install-experimental": if(!busy)await InstallExperimental(GamePath(game));break;
                case "play-experimental": if(!busy)await LaunchGame(game,"benilla");break;
                case "uninstall-experimental": if(!busy)await UninstallExperimental(GamePath(game));break;
                case "uninstall-toolkit": if(!busy)await UninstallToolkit();break;
                case "experimental-source": Open(ExperimentalClient.SourceUrl);break;
                case "experimental-folder":
                    string clientFolder=ExperimentalClient.Root(GamePath(game));
                    if(!Directory.Exists(clientFolder))throw new IOException("Install Benilla-Octo first.");
                    Process.Start(new ProcessStartInfo(clientFolder){UseShellExecute=true});
                    break;
                case "check-game-patch": await CheckGamePatch(game);break;
                case "verify-game-patch": if(!busy)await CheckGamePatch(game,true);break;
                case "stop-game-verification": if(patchVerification!=null)patchVerification.Cancel();break;
                case "stop-game-update": if(patchDownload!=null)patchDownload.Cancel();break;
                case "apply-game-patch": if(!busy)await ApplyGamePatch(game);break;
                case "installed": await ReadInstalled(game);break;
                case "security-open": Open("windowsdefender:");break;
                case "security-history": Open("windowsdefender://threat/");break;
                case "security-report": Open("https://www.microsoft.com/en-us/wdsi/filesubmission");break;
                case "copy-game": CopyFolder(game,"Game folder");break;
                case "copy-toolkit": CopyFolder(AppDomain.CurrentDomain.BaseDirectory,"Launcher folder");break;
                case "security-check": CheckFiles(request,game);break;
                case "release": if(releaseUrl!=null)Open(releaseUrl);break;
                case "view-log": ToolkitLog.Open();break;
                case "history": PostHistory(GamePath(game));break;
                case "restore-history": if(!busy)await RestoreHistory(GamePath(game),TextField(request,"id"));break;
                case "install-toolkit-update": if(!busy&&toolkitRelease!=null)await InstallToolkitUpdate(toolkitRelease);break;
                case "install": if(!busy)await Install(request,GamePath(game));break;
                case "update-addons": if(!busy)await UpdateAddons(request,GamePath(game));break;
                case "install-repository": if(!busy)await InstallRepository(GamePath(game),TextField(request,"repository"));break;
                case "remove-addon": if(!busy)await RemoveAddons(GamePath(game),new[]{TextField(request,"addon")});break;
                case "remove-addons":
                    if(!busy) {
                        var names=Field(request,"addons") as IList;
                        if(names==null||names.Count==0||names.Count>1000)throw new IOException("Choose installed addons to delete.");
                        await RemoveAddons(GamePath(game),names.Cast<object>().Select(Convert.ToString).ToArray());
                    }
                    break;
                case "review-install":
                    if(installReview!=null)installReview.TrySetResult(IsChecked(request,"confirmed")&&
                        (String.IsNullOrEmpty(reviewConfirmation)||TextField(request,"confirmation")==reviewConfirmation));
                    break;
                case "uninstall":
                    if(!busy)await Uninstall(game,IsChecked(request,"removeAddon"));
                    break;
                case "updates":
                    if(!busy&&!checkingUpdates)await Updates(game,IsChecked(request,"background"));
                    else Post(new{checkingUpdates,updateCheckDeferred=true});
                    break;
            }
        } catch(Exception error) {
            ToolkitLog.Write("toolkit","Action failed ("+error.GetType().Name+"): "+error.Message,"error");
            busy=false;
            Post(new {status=error.Message,busy=false,actionError=error.Message});
        }
    }
    private static object Field(Dictionary<string,object> row,string key) {
        object value;return row.TryGetValue(key,out value)?value:null;
    }
    private static string TextField(Dictionary<string,object> row,string key) {return Convert.ToString(Field(row,key));}
    private static bool IsChecked(Dictionary<string,object> row,string key) {return Object.Equals(Field(row,key),true);}
    private void FitContent(Dictionary<string,object> request) {
        if(WindowState!=FormWindowState.Normal)return;
        double viewport;
        if(!Double.TryParse(TextField(request,"viewport"),out viewport)||viewport<300)return;
        if(cssToWindowScale==0)cssToWindowScale=browser.Width/viewport;
        double zoom=Math.Min(1,(browser.Width-2)/(900*cssToWindowScale));
        if(Math.Abs(browser.ZoomFactor-zoom)>0.002)browser.ZoomFactor=zoom;
    }
    private static decimal Number(Dictionary<string,object> row,string key,decimal min,decimal max) {
        decimal value;
        if(!Decimal.TryParse(Convert.ToString(Field(row,key),CultureInfo.InvariantCulture),NumberStyles.Number,CultureInfo.InvariantCulture,out value)||value<min||value>max)
            throw new IOException("Client tweak value out of range: "+key+".");
        return value;
    }
    private Package[] Plan(Dictionary<string,object> request,string folder,bool checkOnly) {
        var input=Field(request,"components") as IList;
        if(input==null||input.Count>catalog.Count)throw new IOException("Invalid component selection.");
        var requested=new HashSet<string>(input.Cast<object>().Select(Convert.ToString));
        if(requested.Any(name=>!catalog.ContainsKey(name)))throw new IOException("Unknown component in the install plan.");
        if(requested.Count(name=>catalog[name].Group=="Auction House")>1)throw new IOException("Choose one auction addon.");
        foreach(string name in requested.ToArray())
            foreach(string dependency in catalog[name].Requires.Where(dependency=>catalog[dependency].Requires.Contains(name)))
                requested.Add(dependency);
        var seen=new HashSet<string>();
        var plan=new List<Package>();
        Action<string> add=null;
        add=name=>{
            if(!seen.Add(name))return;
            var package=catalog[name];
            foreach(var dependency in package.Requires)add(dependency);
            if(checkOnly||requested.Contains(name)||!ToolkitInstall.DependencyPresent(folder,package))plan.Add(package);
        };
        foreach(string name in requested)add(name);
        return plan.ToArray();
    }
    private async Task ApplyClientMods(Dictionary<string,object> request,string folder) {
        var changes=Field(request,"changes") as Dictionary<string,object>;
        if(changes==null||changes.Count>catalog.Count)throw new IOException("Invalid client mod changes.");
        var tweaks=new ClientTweaks();var enable=new List<string>();var review=new List<string>();
        foreach(var change in changes) {
            Package package;
            if(!catalog.TryGetValue(change.Key,out package)||!ClientModSettings.Toggleable(package)||!(change.Value is bool))throw new IOException("Invalid client mod switch.");
            bool enabled=(bool)change.Value;tweaks.ModChanges[change.Key]=enabled;
            if(enabled)enable.Add(change.Key);
            review.Add((enabled?"Enable ":"Disable ")+change.Key);
        }
        var presence=Field(request,"presence") as Dictionary<string,object>;
        if(presence!=null&&IsChecked(presence,"change")) {
            tweaks.PresenceChange=true;tweaks.PresenceEnabled=IsChecked(presence,"enabled");
            tweaks.PresenceFlags=(int)Number(presence,"flags",0,127);
            if(tweaks.PresenceEnabled&&!DiscordPresence.State(folder).installed)enable.Add("Discord Presence");
            review.Add(tweaks.PresenceEnabled?"Apply Discord Presence options":"Disable Discord Presence");
        }
        var full=Plan(new Dictionary<string,object>{{"components",enable.ToArray()}},folder,true);
        foreach(var dependency in full.Where(ClientModSettings.Toggleable)) {
            bool desired;
            if(tweaks.ModChanges.TryGetValue(dependency.Name,out desired)&&!desired)
                throw new IOException(dependency.Name+" is required by another mod you are enabling. Leave it on or turn off the dependent mod.");
            if(!ToolkitInstall.DependencyPresent(folder,dependency)&&!tweaks.ModChanges.ContainsKey(dependency.Name))review.Add("Enable required mod "+dependency.Name);
            tweaks.ModChanges[dependency.Name]=true;
        }
        var plan=full.Where(package=>!ToolkitInstall.ComponentExists(folder,package)).ToArray();
        var beforeNormalize=new HashSet<string>(tweaks.ModChanges.Keys);
        ClientModSettings.NormalizeChanges(folder,tweaks.ModChanges);
        foreach(var change in tweaks.ModChanges.Where(change=>!beforeNormalize.Contains(change.Key)))
            review.Add("Disable "+change.Key);
        review.AddRange(plan.Select(package=>"Install "+package.Name));
        if(review.Count==0)return;
        busy=true;
        if(!await ConfirmInstall("Review client mod changes",null,review,
            "Close OctoWoW before applying. Turning a mod off keeps its files and settings. Required components are included automatically. Disabled mods may affect addons that use them.","Apply Changes")) {
            busy=false;Post(new{busy=false,status="Client mod changes cancelled."});return;
        }
        Post(new{busy=true,status="Applying client mod changes…"});
        var progress=new Progress<string>(message=>Post(new{status=message}));
        await Task.Run(()=>ToolkitInstall.Run(folder,"Skip Sync",plan,tweaks,message=>((IProgress<string>)progress).Report(message)));
        busy=false;Post(new{busy=false,modsApplied=true,clientMods=ClientModSettings.Read(folder),presenceApplied=tweaks.PresenceChange,status="Client mod changes saved. Restart OctoWoW to load them."});
    }
    private async Task Install(Dictionary<string,object> request,string folder) {
        var plan=Plan(request,folder,false);
        string edition=TextField(request,"edition");
        if(!new[]{"Guild Sync","Auction Only","Bank and Guild Sync","Skip Sync"}.Contains(edition))throw new IOException("Choose a Sync edition.");
        var values=Field(request,"tweaks") as Dictionary<string,object>;
        var tweaks=new ClientTweaks();
        var presence=Field(request,"presence") as Dictionary<string,object>;
        if(presence!=null&&IsChecked(presence,"change")) {
            tweaks.PresenceChange=true;tweaks.PresenceEnabled=IsChecked(presence,"enabled");
            decimal flags=Number(presence,"flags",0,127);
            if(flags!=Decimal.Truncate(flags))throw new IOException("Invalid Discord Presence detail options.");
            tweaks.PresenceFlags=(int)flags;
            if(tweaks.PresenceEnabled&&!DiscordPresence.State(folder).installed&&!plan.Any(package=>package.Name=="Discord Presence"))
                throw new IOException("Include Discord Presence in the install plan to enable it.");
        }
        if(values!=null) {
            tweaks.LoginChange=IsChecked(values,"changeAutoLogin");
            tweaks.LoginEnabled=IsChecked(values,"autoLogin");
            if(tweaks.LoginChange&&tweaks.LoginEnabled&&!plan.Any(package=>package.Name=="Auto Login"))
                throw new IOException("Auto Login must be included in the reviewed install plan.");
        }
        if(values!=null&&IsChecked(values,"enabled")) {
            tweaks.Enabled=true;
            tweaks.Nameplates=Number(values,"plates",1,100);tweaks.FieldOfView=Number(values,"fov",60,150);
            tweaks.Render=Number(values,"render",777,3000);tweaks.Clutter=Number(values,"clutter",0,1000);tweaks.Camera=Number(values,"camera",1,100);
            tweaks.LargeAddress=IsChecked(values,"laa");tweaks.BackgroundSounds=IsChecked(values,"audio");
            tweaks.AlwaysAutoLoot=IsChecked(values,"autoLoot");
            tweaks.UnsafeLimits=IsChecked(values,"unsafeLimits");
            tweaks.CameraSkipFix=IsChecked(values,"cameraSkip");
            tweaks.UnlimitedScriptMemory=IsChecked(values,"scriptMemory");
            tweaks.CrossFactionRes=IsChecked(values,"crossFactionRes");
            tweaks.CustomGlues=IsChecked(values,"customGlues");
            tweaks.DepCompatibility=IsChecked(values,"depCompatibility");
            tweaks.ClearWdb=IsChecked(values,"clearWdb");
            tweaks.PatchSoundChannels=Field(values,"channels")!=null;
            if(tweaks.PatchSoundChannels)tweaks.SoundChannels=(int)Number(values,"channels",1,256);
            var changes=Field(values,"changed") as IList;
            if(changes!=null)tweaks.SelectChanges(changes.Cast<object>().Select(Convert.ToString).ToArray());
            tweaks.ValidateLimits();
        }
        var review=new List<string>();
        if(edition!="Skip Sync")review.Add(edition);
        if(tweaks.LoginChange&&!tweaks.LoginEnabled)review.Add("Disable AutoLogin · keeps saved accounts and other login-screen customizations");
        if(tweaks.PresenceChange) {
            string[] detailNames={"Name","Guild","Faction","Class","Level","Zone","Race"};
            string details=String.Join(", ",detailNames.Where((name,index)=>(tweaks.PresenceFlags&(1<<index))!=0));
            review.Add(!tweaks.PresenceEnabled?"Discord Presence OFF":details.Length==0?"Discord Presence ON · no character details shared":"Discord Presence ON · share: "+details);
        }
        review.AddRange(plan.Select(package=>(ToolkitInstall.ComponentExists(folder,package)?"Update ":"Install ")+package.Name));
        if(tweaks.Enabled)review.AddRange(tweaks.DescribeChanges());
        if(review.Count==0){Post(new{status="Choose a component or enable Client Tweaks."});return;}
        if(tweaks.Enabled)ClientTweaks.ValidateClient(folder);
        busy=true;
        bool requiresClose=ToolkitInstall.RequiresClosedGame(edition,plan,tweaks);
        string note=requiresClose?"Close OctoWoW before applying these changes.":ReloadHint;
        if(edition!="Skip Sync") {
            string data="Y'Shaarj Alliance auction listings, including in-game sellers when available, for the public Auction House";
            if(edition!="Auction Only")data+=", plus the Twilight Hammer roster and captured professions";
            if(edition=="Bank and Guild Sync")data+=", plus inventory and gold for Twilightbank and Twilightbags";
            note+=" Sync installs a scheduled task that sends "+data+" to the Twilight Hammer portal. Choose Skip Sync to continue without uploads.";
        }
        bool appliesTweaks=tweaks.Enabled||tweaks.LoginChange;
        if(!await ConfirmInstall("Review changes",null,review,note,appliesTweaks?"Apply Tweaks":"Install")) {
            busy=false;
            Post(new{status="Installation cancelled.",busy=false});
            return;
        }
        Post(new{status="Preparing installation…",busy=true,
            updating=plan.Where(package=>ToolkitInstall.ComponentExists(folder,package)).Select(package=>package.Name).ToArray()});
        ToolkitLog.Write("install","Applying "+String.Join(", ",review)+".");
        updateEpoch++;
        var progress=new Progress<string>(message=>Post(new{status=message}));
        await Task.Run(()=>ToolkitInstall.Run(folder,edition,plan,tweaks,message=>((IProgress<string>)progress).Report(message)));
        ToolkitLog.Write("install","Installation completed.");
        busy=false;
        Post(new{status=plan.Any(p=>p.Folder=="game")?"Setup complete. Launch through VanillaFixes to load DLL mods.":
            requiresClose?"Setup complete. Start OctoWoW to see your changes.":"Setup complete. "+ReloadHint,
            busy=false,updated=plan.Select(package=>package.Name).ToArray(),presenceApplied=tweaks.PresenceChange||plan.Any(package=>package.Name=="Discord Presence"),
            tweaksApplied=appliesTweaks,tweakPath=TextField(request,"game"),installPlanApplied=!IsChecked(request,"single")});
    }
    private async Task Uninstall(string game,bool removeAddon) {
        string folder=removeAddon?GamePath(game):null;
        busy=true;
        Post(new{status="Removing the Sync uploader…",busy=true});
        updateEpoch++;
        await Task.Run(()=>SyncRemoval.Run(folder,removeAddon));
        ToolkitLog.Write("sync",removeAddon?"Removed uploader and addon.":"Removed uploader.");
        busy=false;
        Post(new{status=removeAddon?"Sync uploader, scheduled task, and WoW addon removed.":"Sync uploader and scheduled task removed.",
            busy=false,syncRemoved=true});
    }
    private async Task InstallRepository(string game,string link) {
        string stage=Path.Combine(Path.GetTempPath(),"TwilightRepository-"+Guid.NewGuid().ToString("N"));
        busy=true;
        Post(new{status="Reading addon repository…",busy=true});
        try {
            var addons=await Task.Run(()=>ToolkitPackages.StageRepository(link,stage));
            var installed=ToolkitInstall.Addons(game).ToDictionary(addon=>addon.Name,StringComparer.OrdinalIgnoreCase);
            var repositories=ToolkitInstall.Repositories(game);
            var descriptions=addons.Select(name=>{
                InstalledAddon addon;
                if(!installed.TryGetValue(name,out addon))return "Install "+name;
                RepositoryAddon previous;
                repositories.TryGetValue(name,out previous);
                var versions=ToolkitUpdates.DescribeRepositoryVersion(addon,stage,previous==null?null:previous.Fingerprint,ToolkitInstall.RepositoryFingerprint(stage,name));
                return "Update "+name+" · "+(versions.Current??"Unknown version")+" → "+(versions.Latest??"Version unavailable");
            }).ToArray();
            if(!await ConfirmInstall("Review repository addons",link.Trim(),descriptions,
                "The Launcher backs up matching files before replacing them. "+ReloadHint)) {
                Post(new{status="Repository install cancelled."});
                return;
            }
            Post(new{status="Applying repository changes…",updating=addons.Where(installed.ContainsKey).Select(name=>"git:"+name).ToArray()});
            updateEpoch++;
            await Task.Run(()=>ToolkitInstall.InstallRepository(game,stage,link.Trim(),addons));
            ToolkitLog.Write("repository","Applied "+String.Join(", ",addons)+".");
            Post(new{status="Applied changes to "+String.Join(", ",addons)+". "+ReloadHint,repositoryApplied=true,
                updated=addons.Select(name=>"git:"+name).ToArray()});
        } finally {
            try {if(Directory.Exists(stage))Directory.Delete(stage,true);}
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            busy=false;
            Post(new{busy=false});
        }
    }
    private async Task UpdateAddons(Dictionary<string,object> request,string game) {
        var input=Field(request,"keys") as IList;
        if(input==null||input.Count==0||input.Count>1000)throw new IOException("No addon updates selected.");
        var keys=new HashSet<string>(input.Cast<object>().Select(Convert.ToString),StringComparer.Ordinal);
        var addons=ToolkitInstall.Addons(game);
        var repositoryGroups=addons.Where(addon=>addon.Source!=null&&keys.Contains("git:"+addon.Name))
            .GroupBy(addon=>addon.Source).ToArray();
        var packages=catalog.Values.Where(package=>keys.Contains(package.Name)&&ToolkitInstall.ComponentExists(game,package))
            .Select(package=>new {Package=package,Plan=Plan(new Dictionary<string,object>{{"components",new[]{package.Name}}},game,false)}).ToArray();
        var review=packages.Select(item=>item.Package.Name)
            .Concat(repositoryGroups.SelectMany(group=>group.Select(addon=>addon.Name))).ToArray();
        if(review.Length==0)throw new IOException("No installed components match this update.");
        busy=true;
        var tweaks=new ClientTweaks();
        bool requiresClose=packages.Any(item=>ToolkitInstall.RequiresClosedGame("Skip Sync",item.Plan,tweaks));
        string note=requiresClose?"Close OctoWoW for client mods or patches. Addon-only updates can still run.":ReloadHint;
        if(!await ConfirmInstall("Review updates",null,review,note,review.Length==1?"Update":"Update All")){
            busy=false;
            Post(new{busy=false});
            return;
        }
        updateEpoch++;
        Post(new{updating=keys.ToArray(),status="Updating selected components…"});
        var finished=new List<string>();
        int failures=0;
        foreach(var item in packages){
            try {
                await Task.Run(()=>ToolkitInstall.Run(game,"Skip Sync",item.Plan,tweaks,message=>{}));
                var updated=item.Plan.Select(package=>package.Name).ToArray();
                finished.AddRange(updated);
                Post(new{updated});
            } catch(Exception error) {
                failures++;
                Post(new{updateFailed=new[]{item.Package.Name}});
                ToolkitLog.Write("updates",item.Package.Name+": "+error.Message,"warn");
            }
        }
        foreach(var group in repositoryGroups){
            string stage=Path.Combine(Path.GetTempPath(),"TwilightRepository-"+Guid.NewGuid().ToString("N"));
            try {
                var updated=await Task.Run(()=>{
                    var found=ToolkitPackages.StageRepository(group.Key,stage);
                    var names=found.Where(name=>group.Any(addon=>addon.Name.Equals(name,StringComparison.OrdinalIgnoreCase))).ToArray();
                    if(names.Length!=group.Count())
                        throw new IOException("A selected addon is missing from its repository.");
                    ToolkitInstall.InstallRepository(game,stage,group.Key,names);
                    return names.Select(name=>"git:"+name).ToArray();
                });
                finished.AddRange(updated);
                Post(new{updated});
            } catch(Exception error) {
                failures++;
                Post(new{updateFailed=group.Select(addon=>"git:"+addon.Name).ToArray()});
                ToolkitLog.Write("updates","Repository update failed: "+error.Message,"warn");
            } finally {
                try {if(Directory.Exists(stage))Directory.Delete(stage,true);}
                catch(IOException) { }
                catch(UnauthorizedAccessException) { }
            }
        }
        busy=false;
        string status=failures==0?"Updates complete.":failures+" update operations failed. See the Launcher log.";
        if(finished.Count>0)status+=" "+ReloadHint;
        Post(new{status,busy=false});
    }
    private async Task RemoveAddons(string game,string[] names) {
        var installed=ToolkitInstall.Addons(game);
        var selected=names.Distinct(StringComparer.OrdinalIgnoreCase).Select(name=>
            installed.FirstOrDefault(row=>row.Name.Equals(name,StringComparison.OrdinalIgnoreCase))).ToArray();
        if(selected.Any(addon=>addon==null||addon.Name.Equals("TwilightHammerSync",StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Choose installed addons to delete. Manage Sync through its Details button.");
        busy=true;
        if(!await ConfirmInstall("Delete selected addons?",null,selected.Select(addon=>addon.Name),
            "The selected addons will be removed from the game folder and backed up. Saved addon settings are kept.",
            "Delete","DELETE")) {
            busy=false;
            Post(new{busy=false});
            return;
        }
        updateEpoch++;
        var removed=new List<InstalledAddon>();
        int failures=0;
        foreach(var addon in selected) {
            try {
                await Task.Run(()=>ToolkitInstall.RemoveAddon(game,addon.Name));
                removed.Add(addon);
                ToolkitLog.Write("addons","Removed "+addon.Name+" and backed up its files.");
            } catch(Exception error) {
                failures++;
                ToolkitLog.Write("addons","Could not remove "+addon.Name+": "+error.Message,"error");
            }
        }
        busy=false;
        string status=removed.Count+" addon"+(removed.Count==1?"":"s")+" deleted.";
        if(failures>0)status+=" "+failures+" could not be deleted. See the Launcher log.";
        if(removed.Count>0)status+=" Run /reload in OctoWoW.";
        Post(new{status,busy=false,removed=removed.Select(addon=>addon.Name).ToArray(),
            removedComponents=removed.Select(addon=>addon.CatalogName).Where(name=>name!=null).ToArray()});
    }
    private async Task<bool> ConfirmInstall(string title,string source,IEnumerable<string> items,string note,string action="Install",string confirmation=null) {
        var review=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        installReview=review;
        reviewConfirmation=confirmation;
        try {
            Post(new{busy=true,review=new{title,source,items=items.ToArray(),note,action,confirmation}});
            return await review.Task;
        } finally {
            installReview=null;
            reviewConfirmation=null;
        }
    }
    private async Task Updates(string game,bool background) {
        checkingUpdates=true;
        ToolkitLog.Write("updates",background?"Background update check started.":"Manual update check started.");
        int epoch=updateEpoch;
        Post(new{checkingUpdates=true});
        if(!background)Post(new{status="Checking for updates…"});
        try {
            string path=game.Trim().Trim('"');
            string folder=IsGameFolder(path)?Path.GetFullPath(path):null;
            IProgress<object> progress=new Progress<object>(update=>{
                if(epoch==updateEpoch&&checkingUpdates)Post(update);
            });
            var result=await Task.Run(()=>ToolkitUpdates.Check(folder,message=>{},(key,identity,version)=>
                progress.Report(new{updateGame=game,updateComponent=new{key,identity,version}})));
            releaseUrl=result.ToolkitUrl;
            toolkitRelease=result.ToolkitRelease;
            Post(new{toolkitAvailable=toolkitRelease==null?null:new {
                version=toolkitRelease.Version,notes=AbbreviatedNotes(toolkitRelease.Notes)}});
            if(epoch==updateEpoch)Post(new{updateGame=game,updatePath=folder,availableUpdates=result.Available,updateIdentities=result.UpdateIdentities,updateVersions=result.Versions});
            if(!background&&!busy)Post(new{status=result.Summary});
        } catch(Exception error) {
            ToolkitLog.Write("updates","Update check failed ("+error.GetType().Name+").","warn");
            if(!background&&!busy)Post(new{status=error.Message});
        } finally {
            checkingUpdates=false;
            Post(new{checkingUpdates=false,updateCheckInvalidated=epoch!=updateEpoch});
        }
    }
    private static string[] AbbreviatedNotes(string notes) {
        return (notes??"").Split('\n').Select(line=>line.Trim()).Where(line=>line.Length>0&&!line.StartsWith("#"))
            .Take(5).Select(line=>{
                string clean=System.Text.RegularExpressions.Regex.Replace(line.TrimStart('-','*',' '),@"\[([^\]]+)\]\([^)]+\)","$1")
                    .Replace("**","").Replace("`","");
                return clean.Length>200?clean.Substring(0,197)+"…":clean;
            }).ToArray();
    }
    private async Task InstallToolkitUpdate(Release release) {
        busy=true;
        Post(new{busy=true,selfUpdateProgress="Downloading and verifying the update…"});
        ToolkitLog.Write("selfUpdater","Downloading "+release.Version+".");
        string staged=null;
        try {
            string target=Application.ExecutablePath;
            staged=await Task.Run(()=>ToolkitSelfUpdate.Prepare(release,target));
            ToolkitSelfUpdate.Start(staged,target);
            busy=false;
            Close();
        } catch(Exception error) {
            if(staged!=null)try {File.Delete(staged);} catch(IOException) { } catch(UnauthorizedAccessException) { }
            ToolkitLog.Write("selfUpdater","Update failed ("+error.GetType().Name+").","error");
            busy=false;
            Post(new{busy=false,selfUpdateProgress=error.Message});
        }
    }
    private void Post(object message) {if(!IsDisposed)browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));}
    private async Task ReadInstalled(string game) {
        try {
            string path=game.Trim().Trim('"');
            if(String.IsNullOrWhiteSpace(path)) {
                Post(new{installPath=game,componentStatuses=new Dictionary<string,string>(),addons=new InstalledAddon[0],syncInstalled=false});
                return;
            }
            path=Path.GetFullPath(path);
            var inventory=await Task.Run(()=>{
                bool syncInstalled=ToolkitInstall.SyncInstalled(path);
                return new{installPath=game,scanPath=path,componentStatuses=ToolkitInstall.ComponentStatuses(path),
                    addons=ToolkitInstall.Addons(path),syncInstalled=syncInstalled,
                    syncEdition=syncInstalled?ToolkitInstall.SyncEdition(path):null,benillaInstalled=ExperimentalClient.Installed(path),
                    vanillaFixesInstalled=File.Exists(Path.Combine(path,"VanillaFixes.exe")),clearWdbEnabled=ClientCache.Enabled(path),
                    tweakState=ClientTweaks.ReadState(path),presence=DiscordPresence.State(path),clientMods=ClientModSettings.Read(path)};
            });
            addonSourceLinks.UnionWith(inventory.addons.Select(addon=>addon.SourceUrl).Where(source=>source!=null));
            Post(inventory);
        } catch(IOException) {Post(new {installPath=game,componentStatuses=new Dictionary<string,string>()});}
          catch(UnauthorizedAccessException) {Post(new {installPath=game,componentStatuses=new Dictionary<string,string>()});}
    }
    private void Browse(string path) {
        using(var picker=new FolderBrowserDialog{Description="Choose the OctoWoW folder containing WoW.exe",ShowNewFolderButton=false}) {
            if(Directory.Exists(path))picker.SelectedPath=path;
            if(picker.ShowDialog(this)==DialogResult.OK) {
                if(IsGameFolder(picker.SelectedPath))SaveGameFolder(picker.SelectedPath);
                else Post(new{folderDraft=picker.SelectedPath,settingsStatus="Choose the OctoWoW folder containing WoW.exe."});
            }
        }
    }
    private int mpqWriteEpoch;
    private async Task ListMpqPatches(string game,string status=null) {
        if(!IsGameFolder(game))return;
        int epoch=mpqWriteEpoch;
        try {
            var result=await Task.Run(()=>{
                string folder=GamePath(game);
                var files=MpqPatches.List(folder);
                return new{mpqFiles=files,mpqDiscover=MpqDiscovery.List(folder,files)};
            });
            if(epoch!=mpqWriteEpoch)return;
            if(status==null)Post(new{mpqGame=game,result.mpqFiles,result.mpqDiscover});
            else Post(new{mpqGame=game,result.mpqFiles,result.mpqDiscover,mpqStatus=status});
        } catch(IOException error) {
            if(epoch==mpqWriteEpoch)Post(new{mpqGame=game,mpqStatus=status??error.Message});
        } catch(UnauthorizedAccessException error) {
            if(epoch==mpqWriteEpoch)Post(new{mpqGame=game,mpqStatus=status??error.Message});
        }
    }
    private async Task ToggleMpqPatch(string game,string name,bool enabled,bool environmentSet=false) {
        busy=true;
        mpqWriteEpoch++;
        Post(new{busy=true});
        try {
            string folder=GamePath(game);
            await Task.Run(()=>{
                if(environmentSet)MpqDiscovery.SetEnvironmentEnabled(folder,enabled);
                else MpqPatches.SetEnabled(folder,name,enabled);
            });
            ToolkitLog.Write("mods",(enabled?"Enabled ":"Disabled ")+name+".");
            await ListMpqPatches(game,"");
        } catch(Exception error) {
            ToolkitLog.Write("mods","MPQ switch failed ("+error.GetType().Name+").","warn");
            Post(new{mpqGame=game,mpqStatus=error.Message});
        } finally {
            busy=false;
            Post(new{busy=false});
        }
    }
    private async Task BrowseMpqPatches(string game) {
        using(var picker=new OpenFileDialog{Title="Choose Audio & Visual patches",Filter="MPQ patches (*.mpq)|*.mpq",Multiselect=true}) {
            if(picker.ShowDialog(this)==DialogResult.OK)await ImportMpqPatches(game,picker.FileNames);
        }
    }
    private async Task BrowseEnvironmentPatches(string game) {
        if(busy) {
            Post(new{mpqGame=game,mpqStatus="Wait for the current launcher task to finish, then try again."});
            return;
        }
        using(var picker=new OpenFileDialog{Title="Select the missing Buildings, Doodads and Environment MPQs",Filter="MPQ patches (*.mpq)|*.mpq",Multiselect=true}) {
            if(picker.ShowDialog(this)==DialogResult.OK)await ImportMpqPatches(game,picker.FileNames,environmentSet:true);
            else Post(new{mpqGame=game,mpqStatus=""});
        }
    }
    private async Task InstallDiscoveredMpq(string game,string id,bool download) {
        if(busy) {
            Post(new{mpqGame=game,mpqStatus="Wait for the current launcher task to finish, then try again."});
            return;
        }
        try {
            var entry=MpqDiscovery.Find(id);
            if(download)await ImportMpqPatches(game,null,entry);
            else using(var picker=new OpenFileDialog{Title="Choose the MPQ for "+entry.Name,Filter="MPQ patches (*.mpq)|*.mpq"}) {
                if(picker.ShowDialog(this)==DialogResult.OK)await ImportMpqPatches(game,picker.FileNames,entry);
                else Post(new{mpqGame=game,mpqStatus=""});
            }
        } catch(Exception error) {
            Post(new{mpqGame=game,mpqStatus=error.Message});
        }
    }
    private async Task ImportMpqPatches(string game,string[] paths,MpqCatalogEntry entry=null,bool environmentSet=false) {
        busy=true;
        mpqWriteEpoch++;
        Post(new{busy=true});
        string downloadFile=null;
        try {
            string folder=GamePath(game);
            ToolkitInstall.EnsureClosed();
            bool download=paths==null;
            MpqImport[] patches=download?null:await Task.Run(()=>{
                if(environmentSet)return MpqDiscovery.PlanEnvironment(folder,paths);
                return entry==null?MpqPatches.Plan(folder,paths):new[]{MpqPatches.PlanCatalog(folder,entry,paths.Single())};
            });
            if(patches!=null&&patches.Length==0) {
                await ListMpqPatches(game,"");
                return;
            }
            string title=download?"Download and install this mod?":environmentSet?"Import the Reforged environment set?":"Import MPQ patches?";
            if(!await ConfirmInstall(title,null,
                download?new[]{entry.Name+" → Data/"+entry.Slot}:patches.Select(patch=>Path.GetFileName(patch.Source)+" → Data/"+patch.Name),
                "The launcher will copy these patches into the game folder. You can undo the import from History & Restore.",download?"Download":"Import")) {
                Post(new{mpqGame=game,mpqStatus="MPQ import canceled."});
                return;
            }
            if(download) {
                Post(new{mpqGame=game,mpqStatus="Downloading "+entry.Name+"…"});
                downloadFile=Path.Combine(Path.GetTempPath(),"TwilightMpq-"+Guid.NewGuid().ToString("N")+".mpq");
                patches=await Task.Run(()=>{
                    var bytes=MpqDiscovery.Download(entry);
                    using(var output=new FileStream(downloadFile,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes,0,bytes.Length);
                    return new[]{MpqPatches.PlanCatalog(folder,entry,downloadFile)};
                });
            }
            Post(new{mpqGame=game,mpqStatus="Importing MPQ patches…"});
            await Task.Run(()=>MpqPatches.Install(folder,patches));
            ToolkitLog.Write("mods","Imported MPQ patches: "+String.Join(", ",patches.Select(patch=>patch.Name))+".");
            Post(new{mpqGame=game,mpqStatus=patches.Length+(patches.Length==1?" MPQ patch imported.":" MPQ patches imported.")});
            await ListMpqPatches(game);
        } catch(Exception error) {
            ToolkitLog.Write("mods","MPQ import failed ("+error.GetType().Name+").","warn");
            Post(new{mpqGame=game,mpqStatus=error.Message});
        } finally {
            if(downloadFile!=null)try {File.Delete(downloadFile);} catch(IOException) { } catch(UnauthorizedAccessException) { }
            busy=false;
            Post(new{busy=false});
        }
    }
    private void SaveGameFolder(string path) {
        string folder;
        try {folder=GamePath(path);}
        catch(IOException error){Post(new{settingsStatus=error.Message});return;}
        Directory.CreateDirectory(ToolkitBrowserRuntime.LocalData);
        File.WriteAllText(GameFolderSetting,folder);
        Post(new{game=folder,gameValid=true,savedGameFolder=true,status="Game folder saved."});
    }
    private static string ExistingPath() {
        if(File.Exists(GameFolderSetting))return File.ReadAllText(GameFolderSetting).Trim();
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerSync","native-game.txt");
        if(File.Exists(file))return File.ReadAllText(file).Trim();
        return Directory.Exists(@"G:\Games\Octo")?@"G:\Games\Octo":"";
    }
    private static bool IsGameFolder(string path) {
        return Directory.Exists(path)&&(File.Exists(Path.Combine(path,"WoW.exe"))||File.Exists(Path.Combine(path,"WoW_Modernized.exe")));
    }
    private static string GamePath(string path) {
        path=path.Trim().Trim('"');
        if(!IsGameFolder(path))throw new IOException("Choose the OctoWoW folder containing WoW.exe.");
        return Path.GetFullPath(path);
    }
    internal static string ResolveGameClient(string folder,string client) {
        string[] candidates;
        switch(client) {
            case "benilla": return ExperimentalClient.Executable(folder);
            case "auto": candidates=new[]{"VanillaFixes.exe","WoW_Modernized.exe","WoW.exe"};break;
            case "modernized": candidates=new[]{"WoW_Modernized.exe"};break;
            case "standard": candidates=new[]{"WoW.exe"};break;
            case "vanillafixes": candidates=new[]{"VanillaFixes.exe"};break;
            default: throw new IOException("Choose a launch client in Settings.");
        }
        foreach(string name in candidates) {
            string executable=Path.Combine(folder,name);
            if(File.Exists(executable))return executable;
        }
        throw new IOException("The selected launch client isn't in the game folder.");
    }
    private async Task CheckGamePatch(string game,bool verify=false) {
        var watch=Stopwatch.StartNew();
        var cancellation=verify?new CancellationTokenSource():null;
        if(verify){patchVerification=cancellation;busy=true;Post(new{busy=true,patchVerifying=true,patchMessage="Verifying game files…"});}
        try {
            string folder=GamePath(game);
            var progress=new Progress<Tuple<string,long,long>>(value=>{
                double elapsed=watch.Elapsed.TotalSeconds;
                double remaining=value.Item2>0&&elapsed>=2?Math.Max(0,(value.Item3-value.Item2)*elapsed/value.Item2):-1;
                Post(new{patchMessage=value.Item1,patchDone=value.Item2,patchTotal=value.Item3,patchEta=remaining});
            });
            var check=await Task.Run(()=>verify?OctoPatch.CheckDetailed(folder,(message,done,total)=>
                ((IProgress<Tuple<string,long,long>>)progress).Report(Tuple.Create(message,done,total)),cancellation.Token):OctoPatch.Check(folder));
            bool newRelease=!String.IsNullOrEmpty(check.CurrentRevision)&&check.CurrentRevision!=check.Revision;
            string pending=check.ArchiveCount==0?"No game files to download":
                check.ArchiveCount+(check.ArchiveCount==1?" file":" files")+(newRelease?" to update":" to download");
            ToolkitLog.Write("game-updates","Release "+check.Revision+" · "+check.ReleaseCount+" files · "+pending+". "+check.Verification);
            foreach(var file in check.Files)ToolkitLog.Write("game-updates",file.Path+": "+file.Reason);
            Post(new{patchCheck=check,patchGame=game});
            LoadPatchNotes(game,check);
        } catch(OperationCanceledException) {
            Post(new{patchMessage="Verification stopped. No game files were changed.",patchVerificationStopped=true});
        } catch(Exception error) {
            ToolkitLog.Write("launcher","Patch check failed ("+error.GetType().Name+").","warn");
            Post(new{patchCheck=new OctoPatchInfo{State="unavailable"},patchGame=game,patchError="Could not verify game updates: "+error.Message});
        }
        finally {if(verify){patchVerification=null;cancellation.Dispose();busy=false;Post(new{busy=false,patchVerifying=false});}}
    }
    private string patchNotesGame,patchNotesRelease;
    private DateTime nextPatchNotesCheck;
    private object patchNotesRequest;
    private async void LoadPatchNotes(string game,OctoPatchInfo check,bool completed=false) {
        if(!OctoPatchNotes.CanLookUp(check,completed)) {
            if(patchNotesGame!=game||patchNotesRelease!=check.ReleaseId) {
                patchNotesRequest=null;
                patchNotesRelease=null;
            }
            return;
        }
        if(patchNotesGame==game&&patchNotesRelease==check.ReleaseId&&DateTime.UtcNow<nextPatchNotesCheck)return;
        patchNotesGame=game;
        patchNotesRelease=check.ReleaseId;
        nextPatchNotesCheck=DateTime.MaxValue;
        var request=patchNotesRequest=new object();
        Post(new{patchNotesGame=game,patchNotesRevision=check.Revision,patchNotesLoading=true});
        OctoPatchNotesInfo notes=null;
        try {notes=await Task.Run(()=>OctoPatchNotes.Find(check,completed));}
        catch(Exception failure) {
            ToolkitLog.Write("game-updates","Patch notes unavailable ("+failure.GetType().Name+").","warn");
        }
        if(request!=patchNotesRequest)return;
        nextPatchNotesCheck=notes==null?DateTime.UtcNow.AddMinutes(2):DateTime.MaxValue;
        Post(new{patchNotesGame=game,patchNotesRevision=check.Revision,patchNotesLoading=false,patchNotes=notes});
    }
    private async Task ApplyGamePatch(string game) {
        busy=true;
        patchDownload=new CancellationTokenSource();
        var token=patchDownload.Token;
        var clock=Stopwatch.StartNew();string lastPhase="preparing";
        long phaseStartBytes=0;
        Post(new{busy=true,patchApplying=true,patchPhase="preparing",patchMessage="Preparing game update…"});
        try {
            string folder=GamePath(game);
            var progress=new Progress<Tuple<string,long,long>>(value=>{
                string phase=value.Item1.StartsWith("Applying")?"applying":value.Item1.StartsWith("Downloading")?"downloading":value.Item1.StartsWith("Verif")?"verifying":"preparing";
                if(lastPhase!=phase){lastPhase=phase;phaseStartBytes=value.Item2;clock.Restart();}
                double elapsed=clock.Elapsed.TotalSeconds;
                long transferred=value.Item2-phaseStartBytes;
                double remaining=phase=="downloading"&&transferred>0&&elapsed>=2?Math.Max(0,(value.Item3-value.Item2)*elapsed/transferred):-1;
                Post(new{patchMessage=value.Item1,patchDone=value.Item2,patchTotal=value.Item3,patchEta=remaining,patchPhase=phase});
            });
            var check=await Task.Run(()=>OctoPatch.ApplyDetailed(folder,(message,done,total)=>
                ((IProgress<Tuple<string,long,long>>)progress).Report(Tuple.Create(message,done,total)),token));
            ToolkitLog.Write("launcher","Applied OctoWoW revision "+check.Revision+".");
            Post(new{patchCheck=check,patchGame=game,patchMessage="Game update complete.",patchApplied=true});
            LoadPatchNotes(game,check,true);
        } catch(OperationCanceledException) {
            Post(new{patchMessage="Update stopped. Original game files were kept. Choose Update to resume.",patchUpdateStopped=true});
        } catch(Exception error) {
            ToolkitLog.Write("launcher","Game patch failed ("+error.GetType().Name+").","warn");
            Post(new{patchError=error.Message});
        } finally {
            patchDownload.Dispose();patchDownload=null;
            busy=false;
            Post(new{busy=false,patchApplying=false});
        }
    }
    private async Task InstallExperimental(string game) {
        busy=true;
        Post(new{busy=true,experimentalStatus="Downloading Benilla-Octo…",experimentalDownloading=true});
        try {
            var progress=new Progress<Tuple<long,long>>(value=>Post(new{experimentalDone=value.Item1,experimentalTotal=value.Item2}));
            await Task.Run(()=>ExperimentalClient.Install(game,(done,total)=>
                ((IProgress<Tuple<long,long>>)progress).Report(Tuple.Create(done,total))));
            ToolkitLog.Write("experimental","Installed verified Benilla-Octo package "+ExperimentalClient.PackageHash.Substring(0,12)+".");
            Post(new{benillaInstalled=true,experimentalStatus="Benilla-Octo is installed. Ready to play."});
        } catch(Exception error) {
            ToolkitLog.Write("experimental","Benilla installation failed: "+error.Message,"error");
            Post(new{experimentalStatus=error.Message});
        } finally {
            busy=false;
            Post(new{busy=false,experimentalDownloading=false});
        }
    }
    private async Task UninstallExperimental(string folder) {
        busy=true;
        try {
            ExperimentalClient.EnsureStopped(folder);
            if(!await ConfirmInstall("Uninstall Benilla-Octo?",null,new[]{"Benilla-Octo client"},
                "Moves the installed client to a backup. Benilla settings and addons, and the shared OctoWoW Data folder, are kept.","Uninstall","UNINSTALL"))return;
            string backup=await Task.Run(()=>ExperimentalClient.Uninstall(folder));
            ToolkitLog.Write("experimental","Uninstalled Benilla to backup: "+backup);
            Post(new{benillaInstalled=false,benillaRemoved=true,experimentalStatus="Benilla uninstalled. Its settings and addons were kept; client files are backed up."});
        } catch(Exception error) {
            ToolkitLog.Write("experimental","Uninstall failed: "+error.Message,"error");
            Post(new{experimentalStatus=error.Message});
        } finally {busy=false;Post(new{busy=false});}
    }
    private async Task UninstallToolkit() {
        busy=true;
        try {
            if(!await ConfirmInstall("Uninstall Twilight Hammer Launcher?",null,new[]{Application.ExecutablePath},
                "Removes this launcher executable and closes this window. Game files, addons, Benilla, the Sync uploader, and launcher preferences and logs are kept. Other downloaded copies of the launcher are not removed.","Uninstall Launcher","UNINSTALL"))return;
            ToolkitUninstall.Start();
            busy=false;
            Close();
        } catch(Exception error) {
            ToolkitLog.Write("toolkit","Could not start uninstall: "+error.Message,"error");
            Post(new{settingsStatus=error.Message});
        } finally {busy=false;Post(new{busy=false});}
    }
    private void ReportLaunch(string client,string message) {
        Post(new{playStatus=message});
        if(client=="benilla")Post(new{experimentalStatus=message});
    }
    private void PostHistory(string game) {
        try {
            Post(new{history=ToolkitHistory.List(game).Select(entry=>new{
                id=entry.Id,date=entry.Date,title=entry.Title,files=entry.Files.Count,restored=entry.Restored}).ToArray()});
        } catch(Exception error) {Post(new{historyError=error.Message});}
    }
    private async Task RestoreHistory(string game,string id) {
        busy=true;Post(new{busy=true});
        try {
            var entry=ToolkitHistory.Load(game,id);
            if(entry.Restored)throw new IOException("These changes have already been restored.");
            var conflicts=await Task.Run(()=>ToolkitHistory.Conflicts(game,entry));
            if(conflicts.Length>0)throw new IOException("Cannot restore because files changed again or backup files are missing: "+String.Join(", ",conflicts.Take(5))+". Restore newer changes first.");
            if(!await ConfirmInstall("Restore previous version?",null,
                new[]{entry.Title}.Concat(entry.Files.Take(30).Select(file=>file.Path)),
                "Restores "+entry.Files.Count+" file changes. Newly added files are removed and replaced files return to their previous versions. Your current files are backed up so the restore can also be undone. Close the game first.","Restore","RESTORE"))return;
            await Task.Run(()=>ToolkitHistory.Restore(game,id));
            updateEpoch++;
            Post(new{historyMessage="Previous version restored. Current files were backed up.",status="Previous version restored.",availableUpdates=new Dictionary<string,bool>(),updatePath=game});
            PostHistory(game);
        } catch(Exception error) {
            ToolkitLog.Write("history","Restore failed: "+error.Message,"error");
            Post(new{historyError=error.Message});
        } finally {busy=false;Post(new{busy=false});}
    }
    private async Task LaunchGame(string game,string client) {
        busy=true;
        Post(new{busy=true});
        try {
            string folder=GamePath(game);
            string executable=ResolveGameClient(folder,client);
            foreach(string name in new[]{"WoW","WoW_Modernized","WoW_tweaked","VanillaFixes","Benilla-Octo"}) {
                foreach(var running in Process.GetProcessesByName(name))using(running) {
                    string message=name=="Benilla-Octo"?"Benilla is already running.":"The game is already running. Close it before starting another client.";
                    if(running.MainWindowHandle!=IntPtr.Zero) {
                        if(IsIconic(running.MainWindowHandle))ShowWindowAsync(running.MainWindowHandle,9);
                        SetForegroundWindow(running.MainWindowHandle);
                    }
                    ToolkitLog.Write("launcher",message);
                    ReportLaunch(client,message);
                    return;
                }
            }
            string launchFile=Path.GetFileName(executable);
            if(launchFile.Equals("WoW_Modernized.exe",StringComparison.OrdinalIgnoreCase)||
               (launchFile.Equals("VanillaFixes.exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(folder,"WoW_Modernized.exe"))))
                await Task.Run(()=>ClientMods.EnsureUltrawideLogin(folder));
            var start=client=="benilla"?ExperimentalClient.StartInfo(folder):new ProcessStartInfo(executable){WorkingDirectory=folder,UseShellExecute=false};
            if(client!="benilla")ClientMods.ConfigureLaunch(folder,executable,start);
            if(client!="benilla")ClientCache.BeforeLaunch(folder);
            ReportLaunch(client,client=="benilla"?"Starting Benilla…":"Starting game…");
            using(var process=Process.Start(start)) {
                if(process==null)throw new IOException("Windows could not start the game.");
                if(client=="benilla"&&await Task.Run(()=>process.WaitForExit(3000)))
                    throw new IOException("Benilla closed during startup (exit code "+process.ExitCode+"). See the Benilla client logs in Open Client Folder.");
            }
            ToolkitLog.Write("launcher","Started "+Path.GetFileName(executable)+".");
            ReportLaunch(client,client=="benilla"?"Benilla started.":"Game started");
        } catch(Exception error) {
            ToolkitLog.Write("launcher","Launch failed: "+error.Message,"warn");
            ReportLaunch(client,error.Message);
        } finally {busy=false;Post(new{busy=false});}
    }
    private void CopyFolder(string folder,string label) {
        if(String.IsNullOrWhiteSpace(folder)) {
            Post(new{securityResult="Choose your OctoWoW folder first."});
            return;
        }
        Clipboard.SetText(folder);
        Post(new{securityResult=label+" copied."});
    }
    private void CheckFiles(Dictionary<string,object> request,string game) {
        try {
            string folder=GamePath(game);
            var installed=ToolkitInstall.Installed(folder);
            var targets=Plan(request,folder,true).Concat(catalog.Values.Where(p=>installed.ContainsKey(p.Name))).Distinct().ToArray();
            if(targets.Length==0) {
                Post(new{securityResult="Game folder found. Select a component to check its files."});
                return;
            }
            var missing=targets.Where(package=>!ToolkitInstall.DependencyPresent(folder,package)).Select(package=>package.Name).ToArray();
            string result=missing.Length==0?targets.Length+" checked. All expected component files are present.":
                "Missing or incomplete: "+String.Join(", ",missing)+".";
            Post(new{securityResult=result+"\nThis checks files and loader configuration only. Review blocks in Windows Protection history."});
        } catch(IOException error) {
            Post(new{securityResult=error.Message});
        }
    }
    private static void Open(string target) {Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}
    protected override void OnFormClosing(FormClosingEventArgs args) {
        if(busy&&args.CloseReason==CloseReason.UserClosing)args.Cancel=true;
        base.OnFormClosing(args);
    }
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]private static extern bool ShowWindowAsync(IntPtr window,int command);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    protected override CreateParams CreateParams {
        get {
            const int DropShadow = 0x00020000;
            var parameters=base.CreateParams;
            parameters.ClassStyle|=DropShadow;
            return parameters;
        }
    }
    private bool nativeFrame;
    private const int CalcClientArea = 0x0083;
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int length);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);

    protected override void OnHandleCreated(EventArgs args) {
        base.OnHandleCreated(args);
        const int CornerPreference=33,BorderColor=34;
        const uint RefreshFrame=0x0037;
        int round=2;
        nativeFrame=DwmSetWindowAttribute(Handle,CornerPreference,ref round,sizeof(int))==0;
        if(nativeFrame) {
            Region=null;
            int plum=0x005b4352;
            DwmSetWindowAttribute(Handle,BorderColor,ref plum,sizeof(int));
            SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,RefreshFrame);
        } else SetShape();
    }

    protected override void WndProc(ref Message message) {
        if(nativeFrame&&message.Msg==CalcClientArea&&message.LParam!=IntPtr.Zero) {
            // A one-pixel non-client frame gives DWM a surface for its border and shadow.
            for(int edge=0;edge<4;edge++) {
                int offset=edge*sizeof(int);
                Marshal.WriteInt32(message.LParam,offset,Marshal.ReadInt32(message.LParam,offset)+(edge<2?1:-1));
            }
            message.Result=IntPtr.Zero;
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnSizeChanged(EventArgs args) {
        base.OnSizeChanged(args);
        if(IsHandleCreated&&!nativeFrame)SetShape();
    }
    private void SetShape() {
        if(Width<40||Height<40||WindowState==FormWindowState.Minimized)return;
        using(var path=new GraphicsPath()) {
            const int diameter=28;
            path.AddArc(0,0,diameter,diameter,180,90);
            path.AddArc(Width-diameter,0,diameter,diameter,270,90);
            path.AddArc(Width-diameter,Height-diameter,diameter,diameter,0,90);
            path.AddArc(0,Height-diameter,diameter,diameter,90,90);
            path.CloseFigure();
            var old=Region;
            Region=new Region(path);
            if(old!=null)old.Dispose();
        }
    }
}
