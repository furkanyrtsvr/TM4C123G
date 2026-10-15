using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TM4CDers {
public static class L {
    public static volatile bool English = true;
    static readonly Dictionary<string,string> entries = new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(Assets.Text("translations.json"));
    static readonly Regex pattern = new Regex(String.Join("|", entries.Keys.OrderByDescending(k=>k.Length).Select(Regex.Escape)), RegexOptions.Compiled);
    static string Settings { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TivaSetup","language.txt"); } }
    public static string T(string tr, string en) { return English ? en : tr; }
    public static string Translate(string text) { return English && text != null ? pattern.Replace(text, m=>entries[m.Value]) : text; }
    public static void Initialize(string[] args) {
        try { if(File.Exists(Settings)) English=File.ReadAllText(Settings).Trim()!="TR"; } catch(IOException) {} catch(UnauthorizedAccessException) {}
        if(args.Contains("--ui-en"))English=true;
        if(args.Contains("--ui-tr"))English=false;
    }
    public static void Select(bool english) {
        English=english;
        try { Directory.CreateDirectory(Path.GetDirectoryName(Settings));File.WriteAllText(Settings,english ? "EN" : "TR"); } catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
}
}
