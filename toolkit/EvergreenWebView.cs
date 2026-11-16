using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class EvergreenWebView {
    private const string RuntimeKey = @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    internal static bool IsInstalled() {
        using (var machineRoot = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        using (var machine = machineRoot.OpenSubKey(RuntimeKey))
        using (var user = Registry.CurrentUser.OpenSubKey(RuntimeKey))
            return HasVersion(machine) || HasVersion(user);
    }

    private static bool HasVersion(RegistryKey key) {
        Version version;
        return key != null && Version.TryParse(key.GetValue("pv") as string, out version)
            && version.CompareTo(new Version(0, 0, 0, 0)) > 0;
    }

    internal static bool EnsureInstalled() {
        if (IsInstalled()) return true;
        if (MessageBox.Show(
            "The full Toolkit interface needs Microsoft Edge WebView2. Download and install the Evergreen Runtime from Microsoft now? Choose No to open the built-in Windows interface.",
            "Twilight Hammer Toolkit", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return false;

        string path = Path.Combine(Path.GetTempPath(), "TwilightHammer-WebView2-" + Guid.NewGuid().ToString("N") + ".exe");
        try {
            using (var client = new WebClient()) client.DownloadFile(BootstrapperUrl, path);
            if (!IsTrustedMicrosoftFile(path))
                throw new IOException("Windows could not verify the Microsoft signature on the WebView2 installer.");

            using (var installer = Process.Start(new ProcessStartInfo(path, "/install") { UseShellExecute = false })) {
                if (!installer.WaitForExit(300000))
                    throw new IOException("The WebView2 installer is still running. Reopen the Toolkit after setup finishes.");
                if (!IsInstalled())
                    throw new IOException("The WebView2 installer did not register the runtime (exit code " + installer.ExitCode + ").");
            }
            return true;
        } finally {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsTrustedMicrosoftFile(string path) {
        var file = new TrustFile { Size = Marshal.SizeOf(typeof(TrustFile)), Path = path };
        var data = new TrustData {
            Size = Marshal.SizeOf(typeof(TrustData)), UiChoice = 2, UnionChoice = 1,
            ProviderFlags = 0x80, File = Marshal.AllocHGlobal(file.Size)
        };
        try {
            Marshal.StructureToPtr(file, data.File, false);
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
            using (var certificate = X509Certificate.CreateFromSignedFile(path))
            using (var signer = new X509Certificate2(certificate))
                return signer.GetNameInfo(X509NameType.SimpleName, false) == "Microsoft Corporation";
        } finally {
            Marshal.DestroyStructure(data.File, typeof(TrustFile));
            Marshal.FreeHGlobal(data.File);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile {
        internal int Size;
        internal string Path;
        internal IntPtr Handle, KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData {
        internal int Size;
        internal IntPtr PolicyCallback, ClientData;
        internal int UiChoice, RevocationChecks, UnionChoice;
        internal IntPtr File;
        internal int StateAction;
        internal IntPtr StateData, UrlReference;
        internal int ProviderFlags, UiContext;
        internal IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
