// Custom action de l'assistant MSI : cherche la derniere version stable publiee sur GitHub (appelee par Product.wxs, fonction CheckLatest).
function CheckLatest() {
  try {
    var cur = Session.Property("ProductVersion").split(".");
    var http = new ActiveXObject("WinHttp.WinHttpRequest.5.1");
    http.SetTimeouts(2000, 2000, 3000, 3000);
    http.Open("GET", "https://api.github.com/repos/Rimfire03/Stream_launcher/releases/latest", false);
    http.SetRequestHeader("User-Agent", "StreamLauncher-Setup");
    http.Send();
    if (http.Status != 200) return 1;
    var m = /"tag_name"\s*:\s*"[Vv]?(\d+)\.(\d+)\.(\d+)"/.exec(http.ResponseText);
    if (!m) return 1;
    for (var i = 0; i < 3; i++) {
      var l = parseInt(m[i + 1], 10), c = parseInt(cur[i], 10) || 0;
      if (l > c) {
        var v = m[1] + "." + m[2] + "." + m[3];
        Session.Property("LATEST_VERSION") = "V" + v;
        Session.Property("LATEST_URL") = "https://github.com/Rimfire03/Stream_launcher/releases/download/V" + v + "/StreamLauncher-V" + v + "-win-x64.msi";
        Session.Property("UPDATEAVAILABLE") = "1";
        break;
      }
      if (l < c) break;
    }
  } catch (e) { }
  return 1;
}
