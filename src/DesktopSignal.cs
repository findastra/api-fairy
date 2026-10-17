using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ApiFairy
{
    // How API Fairy talks to her desktop pet, which Startup Star draws (astras-pet-os). One small JSON file, rewritten whole:
    //   %LOCALAPPDATA%\astras-pet-os\api-fairy-ring\desktop-signal.json
    // It holds a mood, a badge count and the newest bubble line. Bubble lines name keys and places, never key values.
    // Startup Star shows a bubble when "bubble_id" changes, and opens the app with "open" when the pet is clicked.
    static class DesktopSignal
    {
        public const int Schema = 1;
        static long bubbleId;
        static string lastBubble;
        static bool lastSticky;

        public static string FilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "astras-pet-os", "api-fairy-ring", "desktop-signal.json");
        }

        public static void Say(string mood, int badge, string bubble, bool sticky)
        {
            bubbleId = DateTime.UtcNow.Ticks;
            lastBubble = bubble;
            lastSticky = sticky;
            Write(mood, badge);
        }

        public static void Write(string mood, int badge)
        {
            try
            {
                var d = new Dictionary<string, object>();
                d["schema"] = Schema;
                d["pet"] = "api-fairy";
                d["program"] = "API Fairy Ring";
                d["updated"] = Core.Iso(DateTime.UtcNow);
                d["mood"] = mood;
                d["badge"] = badge;
                d["bubble"] = lastBubble;
                d["bubble_id"] = bubbleId.ToString();
                d["sticky"] = lastSticky;
                d["open"] = "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --open";
                var path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null, true);
                else File.Move(tmp, path);
            }
            catch (Exception ex) { Log.Error(ex); }
        }
    }
}
