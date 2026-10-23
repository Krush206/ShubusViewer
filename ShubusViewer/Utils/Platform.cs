using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ShubusViewer.Properties;

namespace ExtendedData
{
    // Cross-platform helpers which make Shubus Viewer work on Unix/Linux.
    //
    // The Windows build plays multimedia (pictures, HTML, Unity3D/Flash/Shockwave
    // games) inside the IE-based WebBrowser control with ActiveX plugins. That
    // control does not exist on Unix (Mono never renders it), so on Linux:
    //   - pictures are drawn by the host form in a PictureBox;
    //   - HTML/MHT documents are opened by the system default handler;
    //   - game files (.unity3d, .swf, .dcr, .wrl, .wrz) are packaged into a small
    //     launcher page and opened in the system browser, where the Pipelight
    //     plugin bridge (https://fds-team.de/cms/pipelight-usage.html) hosts the
    //     Windows browser plugins (Unity Web Player, Flash, Shockwave) under Wine.
    public static class Platform
    {
        public static readonly bool IsUnix;

        static Platform()
        {
            PlatformID id = Environment.OSVersion.Platform;
            IsUnix = (id == PlatformID.Unix) || (id == PlatformID.MacOSX);
        }

        // Directory where generated multimedia launcher pages are stored on Unix.
        public static string MultimediaDir
        {
            get
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(baseDir, "Shubus_Corporation", "ShubusViewer", "multimedia");

                try
                {
                    if (Directory.Exists(dir) == false)
                        Directory.CreateDirectory(dir);
                }
                catch (Exception) { }
                return dir;
            }
        }

        // Opens a URL or a local file with the system default handler.
        public static void OpenWithSystemDefault(string pathOrUrl)
        {
            if (IsUnix == false)
            {
                Process.Start(pathOrUrl);
                return;
            }

            string[] openers = new string[] { "xdg-open", "gio", "open" };
            Exception lastError = null;

            foreach (string opener in openers)
            {
                try
                {
                    // "gio" needs the "open" verb, the others take the path directly.
                    string args = (opener == "gio")
                        ? "open \"" + pathOrUrl.Replace("\"", "\\\"") + "\""
                        : "\"" + pathOrUrl.Replace("\"", "\\\"") + "\"";

                    Process.Start(opener, args);
                    return;
                }
                catch (Exception e)
                {
                    lastError = e;
                }
            }

            if (lastError != null)
                throw lastError;
        }

        // Launches the multimedia launcher page in the user-configured player.
        // The "linuxGamePlayer" setting is a command template where %F is replaced
        // with the page path and %U with its file:// URL. Examples:
        //   firefox-esr %F
        //   palemoon %U
        //   wine "C:\Program Files\Mozilla Firefox\firefox.exe" %U
        // When the setting is empty the system default handler is used.
        public static void OpenGamePage(string pagePath)
        {
            string template = Settings.Default.linuxGamePlayer;

            if (String.IsNullOrEmpty(template) || template.Trim().Length < 1)
            {
                OpenWithSystemDefault(pagePath);
                return;
            }

            string command = template.Replace("%F", pagePath)
                                     .Replace("%U", new Uri(pagePath).AbsoluteUri);
            Process.Start("/bin/sh", "-c \"" + command.Replace("\"", "\\\"") + "\"");
        }

        private static bool unity3dHintShown = false;

        // Warns once per session when a .unity3d file is opened, but no Pipelight
        // Unity3D plugin is enabled in the browser plugin directories.
        public static void CheckUnity3DPluginHint()
        {
            if (IsUnix == false || unity3dHintShown || hasPipelightUnityPlugin())
                return;

            unity3dHintShown = true;

            MessageBox.Show(
                "The Pipelight Unity3D plugin was not found. Without it, .unity3d games cannot run in a Linux browser.\n\n"
                + "See README-LINUX.md for how to build Pipelight from source and enable the Unity Web Player:\n"
                + "    pipelight-plugin --enable unity3d\n\n"
                + "The game page will now be opened in the default browser.",
                Constants.STR_APP_TITLE,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool hasPipelightUnityPlugin()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            string[] dirs = new string[]
            {
                Path.Combine(home, ".mozilla", "plugins"),
                "/usr/lib/pipelight",
                "/usr/lib64/pipelight"
            };

            foreach (string dir in dirs)
            {
                try
                {
                    if (Directory.Exists(dir) == false)
                        continue;

                    foreach (string f in Directory.GetFiles(dir, "libpipelight*"))
                    {
                        string name = Path.GetFileName(f).ToLower();
                        if (name.Contains("unity"))
                            return true;
                    }
                }
                catch (Exception) { }
            }
            return false;
        }

        #region Embedded game server
        // Several web plugins (most notably the Unity Web Player) expect real
        // web content and refuse to load data from file:// pages. This minimal
        // loopback server publishes the generated launcher page and the media
        // file itself, mimicking the environment of a game site.
        //
        // Implemented on a raw TcpListener: Mono's HttpListener proved to be
        // unreliable here (it stops accepting connections after serving the
        // first request).
        private class GameContent
        {
            public string page;
            public string mediaPath;
        }

        private static TcpListener gameTcp = null;
        private static int gameServerPort = 0;
        private static string gameToken = null;
        private static readonly object gameServerLock = new object();
        private static readonly Dictionary<string, GameContent> gameContent =
            new Dictionary<string, GameContent>();

        private static string gameServerLog
        {
            get { return Path.Combine(MultimediaDir, "server.log"); }
        }

        private static void gameLog(string msg)
        {
            try
            {
                File.AppendAllText(gameServerLog
                    , DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine);
            }
            catch (Exception) { }
        }

        private static string StartGameServer()
        {
            lock (gameServerLock)
            {
                if (gameTcp != null && gameTcp.Server.IsBound)
                    return "http://127.0.0.1:" + gameServerPort + "/";

                Random rnd = new Random();
                for (int i = 0; i < 16; i++)
                {
                    int port = rnd.Next(49152, 65000);
                    try
                    {
                        TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                        listener.Start(8);

                        gameTcp = listener;
                        gameServerPort = port;

                        Thread accept = new Thread(acceptGameLoop);
                        accept.IsBackground = true;
                        accept.Start();

                        gameLog("server started on port " + port);
                        return "http://127.0.0.1:" + port + "/";
                    }
                    catch (Exception) { }
                }
                gameLog("failed to start server");
                return null;
            }
        }

        private static void acceptGameLoop()
        {
            TcpListener listener = gameTcp;
            if (listener == null) return;

            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception e)
                {
                    gameLog("accept failed: " + e.Message);
                    break;
                }

                ThreadPool.QueueUserWorkItem(delegate(object o)
                {
                    handleGameClient((TcpClient)o);
                }, client);
            }
        }

        private static void handleGameClient(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 120000;

                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    // Read the request head (until CRLFCRLF, 8 KB max).
                    byte[] buffer = new byte[4096];
                    int total = 0;
                    int headerEnd;

                    while (true)
                    {
                        if (total >= buffer.Length)
                        {
                            Array.Resize(ref buffer, buffer.Length * 2);
                            if (total >= 8192) break;
                        }

                        int read = stream.Read(buffer, total, buffer.Length - total);
                        if (read <= 0) break;
                        total += read;

                        headerEnd = indexOfHeaderEnd(buffer, total);
                        if (headerEnd >= 0) break;
                    }

                    string head = Encoding.ASCII.GetString(buffer, 0, total);
                    string[] lines = head.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length < 1) return;

                    string[] req = lines[0].Split(' ');
                    if (req.Length < 2) return;

                    string method = req[0].ToUpper();
                    string route = req[1];

                    // Drop query strings.
                    int q = route.IndexOf('?');
                    if (q >= 0) route = route.Substring(0, q);

                    gameLog(method + " " + route + " from game client");

                    if (method != "GET" && method != "HEAD")
                    {
                        writeGameStatus(stream, 405, "Method Not Allowed");
                        return;
                    }

                    // Routes: /<token>/game.html and /<token>/data
                    string[] parts = route.Trim('/').Split('/');
                    if (parts.Length != 2)
                    {
                        writeGameStatus(stream, 404, "Not Found");
                        return;
                    }

                    GameContent content;
                    lock (gameServerLock)
                    {
                        if (gameContent.TryGetValue(parts[0], out content) == false)
                        {
                            writeGameStatus(stream, 404, "Not Found");
                            return;
                        }
                    }

                    if (parts[1] == "game.html")
                    {
                        byte[] body = Encoding.UTF8.GetBytes(content.page);
                        byte[] header = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "
                            + body.Length + "\r\nConnection: close\r\n\r\n");

                        stream.Write(header, 0, header.Length);
                        if (method == "GET")
                            stream.Write(body, 0, body.Length);
                        return;
                    }

                    if (parts[1] == "data")
                    {
                        try
                        {
                            using (FileStream fs = File.OpenRead(content.mediaPath))
                            {
                                byte[] header = Encoding.ASCII.GetBytes(
                                    "HTTP/1.1 200 OK\r\nContent-Type: " + mediaContentType(content.mediaPath)
                                    + "\r\nContent-Length: " + fs.Length + "\r\nConnection: close\r\n\r\n");

                                stream.Write(header, 0, header.Length);
                                if (method == "GET")
                                {
                                    byte[] chunk = new byte[64 * 1024];
                                    int read;
                                    while ((read = fs.Read(chunk, 0, chunk.Length)) > 0)
                                        stream.Write(chunk, 0, read);
                                }
                            }
                            gameLog("data served: " + content.mediaPath);
                        }
                        catch (Exception e)
                        {
                            gameLog("data failed: " + e.Message);
                        }
                        return;
                    }

                    writeGameStatus(stream, 404, "Not Found");
                }
            }
            catch (Exception e)
            {
                gameLog("client failed: " + e.Message);
                try { client.Close(); }
                catch (Exception) { }
            }
        }

        private static int indexOfHeaderEnd(byte[] buf, int len)
        {
            for (int i = 3; i < len; i++)
            {
                if (buf[i - 3] == 13 && buf[i - 2] == 10 && buf[i - 1] == 13 && buf[i] == 10)
                    return i;
            }
            return -1;
        }

        private static void writeGameStatus(NetworkStream stream, int code, string reason)
        {
            try
            {
                byte[] header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 " + code + " " + reason + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                stream.Write(header, 0, header.Length);
            }
            catch (Exception) { }
        }

        private static string mediaContentType(string path)
        {
            switch (Path.GetExtension(path).ToLower())
            {
                case ".unity3d": return "application/vnd.unity";
                case ".swf": return "application/x-shockwave-flash";
                case ".dcr": return "application/x-director";
                case ".wrl":
                case ".wrz": return "x-world/x-vrml";
                default: return "application/octet-stream";
            }
        }

        // Step 1: registers the media file and returns its URL,
        // or null when the embedded server could not be started.
        public static string CreateGameEntry(string mediaPath)
        {
            string baseUrl = StartGameServer();
            if (baseUrl == null) return null;

            lock (gameServerLock)
            {
                // Each open of a game publishes a new entry; keep the map bounded.
                if (gameContent.Count > 64)
                    gameContent.Clear();

                gameToken = Guid.NewGuid().ToString("N").Substring(0, 12);
                gameContent[gameToken] = new GameContent { page = "", mediaPath = mediaPath };
                return baseUrl + gameToken + "/data";
            }
        }

        // Step 2: stores the launcher page for the entry created by
        // CreateGameEntry() and returns the page URL.
        public static string FinishGamePage(string page)
        {
            lock (gameServerLock)
            {
                if (gameToken == null || gameContent.ContainsKey(gameToken) == false)
                    return null;

                gameContent[gameToken].page = page;
                return "http://127.0.0.1:" + gameServerPort + "/" + gameToken + "/game.html";
            }
        }
        #endregion // Embedded game server
    }
}
