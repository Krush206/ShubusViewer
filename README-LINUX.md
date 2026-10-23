Shubus Viewer on Unix/Linux
============================

This fork of Shubus Viewer runs natively on Linux with Mono (WinForms). All
text/editor/picture features work in-app. Multimedia playback (Unity3D,
Flash, Shockwave, VRML) is delegated to the system browser, where Pipelight
hosts the original Windows browser plugins under Wine.

The very same sources still build and run unchanged on Windows/.NET.


1. Building
----------

Requirements:

    apt-get install mono-complete mono-xbuild

Build:

    ./build.sh

Result: ShubusViewer/bin/Release/ShubusViewer.exe

Run:

    mono ShubusViewer/bin/Release/ShubusViewer.exe [file]
    mono ShubusViewer/bin/Release/ShubusViewer.exe game.unity3d


2. How playback works on Linux
------------------------------

The Windows build embeds the IE WebBrowser control and plays .unity3d (and
.swf/.dcr/.wrl) files through browser plugins. Mono does not implement that
control, and the Unity Web Player plugin never existed natively for Linux.

Therefore, on Unix:

  * Pictures are drawn in an in-app PictureBox.
  * HTML/MHT documents are opened by the system default handler.
  * Game files are packaged into a launcher page (the same HTML templates
    the Windows build uses) and opened in the system browser. A tiny
    embedded HTTP server (127.0.0.1:<random port>) publishes the page and
    the media file, because several plugins (notably the Unity Web Player)
    refuse to load data from file:// pages.

For the games to actually run, the browser must provide the plugin. Chromium
cannot (NPAPI support was removed); use Pale Moon, SeaMonkey or Firefox
ESR 52 - they still support NPAPI plugins.


3. Setting up the Unity Web Player (Pipelight)
----------------------------------------------

Pipelight (https://fds-team.de) wraps Windows browser plugins in Wine and
exposes them to Linux browsers as NPAPI plugins. Unity3D is one of its
standard plugins.

  a) Build and install Pipelight (source: pipelight sources, Nov 2016 tree):

        cd /opt/mmueller2012-pipelight-*
        ./configure --wine-path=/usr/bin/wine
        make
        make install            # installs to /usr/local
        pipelight-plugin --create-mozilla-plugins

     Requirements: gcc, g++, mingw-w64, wine (with 32-bit support), wget.

     On Devuan/Debian make sure 32-bit wine is present:

        dpkg --add-architecture i386
        apt-get install wine32:i386

  b) The original download servers (webplayer.unity3d.com) are dead, so
     pipelight-plugin --enable unity3d cannot fetch the installer itself.
     Place the full (offline) Unity Web Player installer at:

        /tmp/pipelight-unity3d.exe

     The enable script verifies its SHA256 before reusing that file. The
     archived "UnityWebPlayerFull.exe" (offline installer, ~7 MB) is known
     to work:

        SHA256: 5848e5a5d787656a06a3b5b9fbee86bbf5335fd8d5743e640820bebb0e96ab09

     (Avoid "UnityWebPlayer.exe" - it is a web stub that fetches more data
     from the dead servers.)

  c) Enable the plugin as the desktop user (not root):

        pipelight-plugin --accept --enable unity3d

     This creates ~/.wine-pipelight (win32), installs
     C:\Program Files\Unity\WebPlayer\loader\npUnity3D32.dll and symlinks
     ~/.mozilla/plugins/libpipelight-unity3d.so.

  d) Fonts: the prefix needs fonts for in-game text (Arial/Verdana are not
     redistributable). Copying the system metric-compatible fonts is enough:

        cp /usr/share/fonts/truetype/liberation/Liberation*.ttf \
           /usr/share/fonts/truetype/dejavu/DejaVuSans*.ttf \
           ~/.wine-pipelight/drive_c/windows/Fonts/

  e) Make sure your hostname resolves (wine needs it for wininet):

        echo "127.0.0.1  $(hostname)" >> /etc/hosts

  f) Sanity check:

        pipelight-plugin --system-check


4. Telling Shubus Viewer which browser to use
---------------------------------------------

The user setting "linuxGamePlayer" (in
~/.local/share/Shubus_Corporation/ShubusViewer/user.config) is a command
template. %F is replaced with the launcher page URL/path, %U with its
file:// URL. Examples:

    <setting name="linuxGamePlayer" serializeAs="String">/opt/palemoon/palemoon %F</setting>
    <setting name="linuxGamePlayer" serializeAs="String">firefox-esr %U</setting>
    <setting name="linuxGamePlayer" serializeAs="String">wine "C:\Program Files\Mozilla Firefox\firefox.exe" %U</setting>

When empty, the system default handler (xdg-open) is used.


5. Browser notes
----------------

  * Pale Moon: official Linux tarball, still supports NPAPI. Extract to
    /opt/palemoon and reference /opt/palemoon/palemoon in the setting.
  * SeaMonkey (Debian/Devuan repos) also still supports NPAPI.
  * Chromium and current Firefox do NOT support NPAPI and cannot be used.

If the plugin does not appear in about:plugins of the browser, check:

    ~/.mozilla/plugins/libpipelight-unity3d.so

and consult "pipelight-plugin --system-check". The XATTR/ACL failure it may
report only matters for Silverlight DRM, not for Unity3D.


6. Troubleshooting playback
---------------------------

  * Black window / "missing plugin" page: the browser has no NPAPI support
    or the plugin is not enabled for this user (step 3c).
  * Plugin loads but the data file does not: games are always served through
    the embedded loopback server; if a firewall blocks loopback traffic,
    allow connections to 127.0.0.1.
  * No audio in games: wine needs working audio (try a2j/ALSA/pulse
    configuration of your desktop session).
  * Crashes on start of wine: check that wine32:i386 is installed and that
    ~/.wine-pipelight was created with WINEARCH=win32 (it is done
    automatically by pipelight-plugin).


7. Linux-specific changes in the sources
----------------------------------------

  * Utils/Platform.cs         - new: OS detection, external player launching,
                                embedded game server, Pipelight detection.
  * Engines/AppWebModel.cs    - Unix playback path (launcher pages).
  * Forms/Form1WebBrowserProcessing.cs
                              - in-app PictureBox / game notice overlays.
  * Forms/Form1.cs             - /etc/hosts on Unix, browser-mode switch.
  * Utils/Utils.cs             - '/' path separators, Path.Combine config
                                path, help opens README-LINUX.md.
  * Components/SettingsProvider.cs - Path.Combine user.config location.
  * Components/MyBrowser.cs, Dialogs/AboutBox1.cs
                              - guards for COM/PInvoke absent on Unix.
  * Engines/AppUpdater.cs     - update servers are dead; failure is caught.
  * Properties: new user setting "linuxGamePlayer".
