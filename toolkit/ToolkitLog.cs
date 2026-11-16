using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

internal static class ToolkitLog {
    private static readonly object Gate=new object();
    internal static readonly string FilePath=Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TwilightHammerToolkit","Toolkit.log");

    internal static void Write(string area,string message,string level="info") {
        lock(Gate) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                if(File.Exists(FilePath)&&new FileInfo(FilePath).Length>=1024*1024)
                    File.Copy(FilePath,FilePath+".previous",true);
                if(File.Exists(FilePath)&&new FileInfo(FilePath).Length>=1024*1024)
                    File.WriteAllText(FilePath,"");
                string line="["+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff",CultureInfo.InvariantCulture)+
                    "] ["+level+"] ["+area+"] "+message.Replace("\r"," ").Replace("\n"," ")+Environment.NewLine;
                File.AppendAllText(FilePath,line,Encoding.UTF8);
            } catch(IOException) { }
              catch(UnauthorizedAccessException) { }
        }
    }

    internal static void Open() {
        Write("toolkit","Opened the log.");
        Process.Start(new ProcessStartInfo("notepad.exe","\""+FilePath+"\""){UseShellExecute=true});
    }
}
