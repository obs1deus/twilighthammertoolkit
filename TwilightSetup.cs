using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class TwilightSetup {
    private const string Portal = "https://twilighthammer.hccpride.workers.dev";
    private const string TaskName = "Twilight Hammer Saved Capture Sync";
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwilightHammerSync");
    private static readonly string Config = Path.Combine(Folder, "native-game.txt");
    private static readonly string Approval = Path.Combine(Folder, "native-approval.dpapi");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 20000000 };
#if BANK_ENABLED
    private const string Title = "Twilight Hammer Bank and Guild Sync";
    private const string Mode = "bank";
#elif AUCTION_ONLY
    private const string Title = "Twilight Hammer Auction Only";
#else
    private const string Title = "Twilight Hammer Guild Sync";
    private const string Mode = "guild";
#endif

    [STAThread]
    private static int Main(string[] args) {
        bool upload = args.Length == 1 && args[0] == "--upload";
        try {
            if (upload) Upload();
            else if (!Install(args.Length == 2 && args[0] == "--install" ? args[1] : null)) return 2;
            return 0;
        } catch (Exception error) {
            if (upload) Log(error.Message);
            else MessageBox.Show(error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool IsGame(string folder) {
        return !String.IsNullOrWhiteSpace(folder) && (File.Exists(Path.Combine(folder, "WoW.exe")) ||
            File.Exists(Path.Combine(folder, "WoW_Modernized.exe")));
    }

    private static bool Install(string requestedGame) {
        if (Process.GetProcessesByName("WoW").Length > 0 || Process.GetProcessesByName("WoW_Modernized").Length > 0)
            throw new InvalidOperationException("Close OctoWoW and run Setup again.");
        if(requestedGame != null && !IsGame(requestedGame))
            throw new InvalidOperationException("Choose the OctoWoW folder containing WoW.exe.");
        string game = requestedGame ?? (File.Exists(Config) ? File.ReadAllText(Config).Trim() : null);
        string oldConfig = Path.Combine(Folder, "config.json");
        if (!IsGame(game) && File.Exists(oldConfig)) {
            var previous = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(oldConfig));
            game = Field(previous, "gamePath");
        }
        if (!IsGame(game)) {
            using (var picker = new FolderBrowserDialog()) {
                picker.Description = "Choose the OctoWoW folder containing WoW.exe.";
                picker.ShowNewFolderButton = false;
                if (IsGame(@"G:\Games\Octo")) picker.SelectedPath = @"G:\Games\Octo";
                if (picker.ShowDialog() != DialogResult.OK) return false;
                game = picker.SelectedPath;
            }
        }
        if (!IsGame(game)) throw new InvalidOperationException("Choose the OctoWoW folder containing WoW.exe.");
#if !AUCTION_ONLY
        string approval = Authorize();
        if (approval == null) return false;
#endif
        string addon = Path.Combine(game, "Interface", "AddOns", "TwilightHammerSync");
        Directory.CreateDirectory(addon);
        Directory.CreateDirectory(Folder);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string resource in assembly.GetManifestResourceNames()) {
            using (Stream input = assembly.GetManifestResourceStream(resource))
            using (Stream output = File.Create(Path.Combine(addon, resource))) input.CopyTo(output);
        }
        string executable = Path.Combine(Folder, "TwilightHammerUploader.exe");
        if (!String.Equals(assembly.Location, executable, StringComparison.OrdinalIgnoreCase))
            File.Copy(assembly.Location, executable, true);
        File.WriteAllText(Config, game);
#if !AUCTION_ONLY
        File.WriteAllBytes(Approval, ProtectedData.Protect(Encoding.UTF8.GetBytes(approval), null, DataProtectionScope.CurrentUser));
#else
        File.Delete(Approval);
#endif
        Register(executable);
        if(requestedGame == null)
            MessageBox.Show("Setup complete. Start OctoWoW and check that Twilight Hammer is enabled at character selection.", Title);
        return true;
    }

#if !AUCTION_ONLY
    private static string ReadApproval() {
        if (!File.Exists(Approval)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Approval), null, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }
    }

    private static string Authorize() {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        string existing = ReadApproval();
        if (existing != null && IsApproved(existing)) return existing;
        var bytes = new byte[32];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        string token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token))).Replace("-", "").ToLowerInvariant();
        var deadline = DateTime.UtcNow.AddMinutes(10);
        long expires = (long)(deadline - new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;
        Process.Start(new ProcessStartInfo(Portal + "/api/sync-setup?mode=" + Mode + "&id=" + hash + "&expires=" + expires) { UseShellExecute = true });
        string instructions = "Authorize this computer on the guild portal in your browser, then choose OK.";
        while (DateTime.UtcNow < deadline) {
            if (MessageBox.Show(instructions, Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return null;
            if (DateTime.UtcNow < deadline && IsApproved(token)) return token;
            instructions = "Authorization is still pending. Finish in your browser, then choose OK to check again.";
        }
        throw new InvalidOperationException("Portal authorization expired. Start the installation again.");
    }

    private static bool IsApproved(string token) {
        return Field(Post("/api/sync-setup/status?mode=" + Mode, Encoding.UTF8.GetBytes("{}"), token), "approved") == "True";
    }
#endif

    private static void Register(string executable) {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        service.Connect();
        dynamic folder = service.GetFolder("\\");
        dynamic task = service.NewTask(0);
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        task.RegistrationInfo.Description = "Checks OctoWoW game files for new Twilight Hammer captures every two minutes.";
        task.Principal.UserId = user;
        task.Principal.LogonType = 3;
        task.Settings.StartWhenAvailable = true;
        task.Settings.MultipleInstances = 2;
        task.Settings.ExecutionTimeLimit = "PT2M";
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        dynamic trigger = task.Triggers.Create(1);
        trigger.StartBoundary = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss");
        trigger.Repetition.Interval = "PT2M";
        dynamic action = task.Actions.Create(0);
        action.Path = executable;
        action.Arguments = "--upload";
        action.WorkingDirectory = Folder;
        folder.RegisterTaskDefinition(TaskName, task, 6, user, null, 3);
    }

    private static IEnumerable<string> ExportFiles(string game) {
        string imports = Path.Combine(game, "imports");
        yield return Path.Combine(imports, "TwilightHammerSync-Captures.txt");
        yield return Path.Combine(imports, "TwilightHammerSync-Aegis-Auctions.txt");
        string accounts = Path.Combine(game, "WTF", "Account");
        if (!Directory.Exists(accounts)) yield break;
        foreach (string account in Directory.EnumerateDirectories(accounts))
            yield return Path.Combine(account, "SavedVariables", "TwilightHammerSync.lua");
    }

    private static IEnumerable<object> ReadCaptures(string file) {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length > 20000000) yield break;
        DateTime stamp = info.LastWriteTimeUtc;
        string text = File.ReadAllText(file);
        if (File.GetLastWriteTimeUtc(file) != stamp) yield break;
        if (file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) {
            Match field = Regex.Match(text, @"^TwilightHammerSync_Export\s*=\s*(?:\[(?<equals>=*)\[(?<long>[\s\S]*?)\]\k<equals>\]|""(?<quoted>(?:[^""\\]|\\[\s\S])*)"")", RegexOptions.Multiline);
            if (!field.Success) yield break;
            text = field.Groups["long"].Success ? field.Groups["long"].Value : DecodeLua(field.Groups["quoted"].Value);
        }
        object capture = Json.DeserializeObject(text);
        var list = capture as IList;
        if (list != null) {
            foreach (object row in list) yield return row;
        } else if (capture != null) yield return capture;
    }

    private static string DecodeLua(string text) {
        return Regex.Replace(text, @"\\([0-9]{1,3}|\r\n|[\s\S])", delegate(Match match) {
            string escape = match.Groups[1].Value;
            int value;
            if (Int32.TryParse(escape, out value) && value < 256) return ((char)value).ToString();
            switch (escape) {
                case "n": return "\n";
                case "r": return "\r";
                case "t": return "\t";
                case "b": return "\b";
                case "f": return "\f";
                case "a": return "\a";
                case "v": return "\v";
                case "\r\n": return "\n";
                default: return escape;
            }
        });
    }

    private static string Field(Dictionary<string, object> row, string name) {
        object value;
        return row.TryGetValue(name, out value) ? Convert.ToString(value) : "";
    }

    private static string Endpoint(Dictionary<string, object> row) {
        if (Field(row, "realm") != "Y'Shaarj" || Field(row, "faction") != "Alliance") return null;
        long stamp;
        if (!Int64.TryParse(Field(row, "observedAt"), out stamp)) return null;
        long age = (long)(DateTime.UtcNow - new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds - stamp;
        if (age > 86400000 || age < -120000) return null;
        object items;
        if (!row.TryGetValue("items", out items) || !(items is IList)) return null;
        string kind = Field(row, "kind");
#if BANK_ENABLED
        if ((kind == "inventory" || kind == "wallet") && Field(row, "character").Equals("Twilightbank", StringComparison.OrdinalIgnoreCase))
            return "toolkit-upload";
#endif
        if (((IList)items).Count == 0) return null;
        if (kind == "auctions") {
            foreach (object raw in (IList)items) {
                var item = raw as Dictionary<string, object>;
                object listings;
                if (item == null || !item.TryGetValue("listings", out listings) || !(listings is IList) || ((IList)listings).Count == 0) return null;
            }
            row.Remove("character");
            row.Remove("guild");
            return "auction-upload";
        }
#if !AUCTION_ONLY
        if ((kind == "roster" || kind == "professions") && Field(row, "guild").Trim().Equals("Twilight Hammer", StringComparison.OrdinalIgnoreCase))
            return "toolkit-upload";
#endif
        return null;
    }

    private static void Upload() {
        string game = File.ReadAllText(Config).Trim();
        string stateFile = Path.Combine(Folder, "native-sent.txt");
        var sent = new HashSet<string>(File.Exists(stateFile) ? File.ReadAllLines(stateFile) : new string[0]);
        var errors = new List<string>();
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        foreach (string file in ExportFiles(game)) {
            try {
                foreach (object capture in ReadCaptures(file)) {
                    var row = capture as Dictionary<string, object>;
                    string route = row == null ? null : Endpoint(row);
                    if (route == null) continue;
                    byte[] body = Encoding.UTF8.GetBytes(Json.Serialize(row));
                    if (body.Length > 900000) continue;
                    string digest;
                    using (var sha = SHA256.Create()) digest = Convert.ToBase64String(sha.ComputeHash(body));
                    if (sent.Contains(digest)) continue;
                    Send(route, body);
                    File.AppendAllText(stateFile, digest + Environment.NewLine);
                    sent.Add(digest);
                    Log("Uploaded " + Field(row, "kind") + " capture.");
                }
            } catch (Exception error) { errors.Add(error.Message); }
        }
        if (errors.Count > 0) throw new IOException(String.Join(" ", errors));
        if (sent.Count > 5000) {
            string[] records = File.ReadAllLines(stateFile);
            File.WriteAllLines(stateFile, new ArraySegment<string>(records, records.Length - 3000, 3000));
        }
    }

    private static void Send(string route, byte[] body) {
        string token = null;
#if !AUCTION_ONLY
        if (route == "toolkit-upload") {
            token = ReadApproval();
            if (token == null) throw new IOException("Run Setup again to authorize this uploader through the guild portal.");
        }
#endif
        if (Field(Post("/api/data/" + route, body, token), "ok") != "True") throw new IOException("Portal did not confirm the upload.");
    }

    private static Dictionary<string, object> Post(string path, byte[] body, string token) {
        var request = (HttpWebRequest)WebRequest.Create(Portal + path);
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Headers["Origin"] = Portal;
        if (token != null) request.Headers["Authorization"] = "Bearer " + token;
        request.AllowAutoRedirect = false;
        request.Timeout = 30000;
        request.ReadWriteTimeout = 30000;
        request.ContentLength = body.Length;
        using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
        using (var response = (HttpWebResponse)request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream())) {
            return Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
        }
    }

    private static void Log(string message) {
        string file = Path.Combine(Folder, "sync.log");
        if (File.Exists(file) && new FileInfo(file).Length > 1000000) {
            File.Delete(file + ".previous");
            File.Move(file, file + ".previous");
        }
        File.AppendAllText(file, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
    }
}
