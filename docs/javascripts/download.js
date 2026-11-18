// Download page: points the buttons at the newest release's files (pre-releases included while
// there is no stable release yet). Without JavaScript or GitHub, the links stay on the releases page.
(function () {
  function setUp() {
    var latest = document.getElementById("deckhost-latest");
    if (!latest) {
      return;
    }

    fetch("https://api.github.com/repos/vman26/DeckHost/releases?per_page=10", { headers: { Accept: "application/vnd.github+json" } })
      .then(function (response) { return response.ok ? response.json() : []; })
      .then(function (releases) {
        var published = releases.filter(function (release) { return !release.draft; });
        var release = published.find(function (r) { return !r.prerelease; }) || published[0];
        if (!release) {
          return;
        }

        function asset(suffix) {
          var match = release.assets.find(function (a) { return a.name.toLowerCase().endsWith(suffix); });
          return match ? match.browser_download_url : release.html_url;
        }

        var files = {
          win: [asset("-win-x64-install.exe"), asset("-win-x64-setup.exe"), asset("-win-x64.zip")].find(function (url) { return url !== release.html_url; }) || release.html_url,
          linux: asset("-linux-x64.tar.gz"),
          appimage: asset("-linux-x86_64.appimage"),
          mac: asset("-osx-arm64.zip"),
        };
        document.getElementById("deckhost-win").href = files.win;
        document.getElementById("deckhost-linux").href = files.linux;
        var appimage = document.getElementById("deckhost-appimage");
        if (appimage) {
          appimage.href = files.appimage;
        }
        document.getElementById("deckhost-mac").href = files.mac;

        var platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
        var system = /win/i.test(platform) ? "win" : /mac/i.test(platform) ? "mac" : /linux/i.test(platform) ? "linux" : null;
        var name = release.name || release.tag_name;
        latest.href = system ? files[system] : release.html_url;
        latest.textContent = system
          ? "Download " + name + " for " + { win: "Windows", mac: "macOS", linux: "Linux" }[system]
          : "Download " + name;
        document.getElementById("deckhost-release-name").textContent =
          (release.prerelease ? "Pre-release" : "Release") + " " + release.tag_name + ", " + new Date(release.published_at).toLocaleDateString() + ".";
      })
      .catch(function () { /* keep the links to the releases page */ });
  }

  // Material's instant navigation swaps pages without a reload.
  if (window.document$ && window.document$.subscribe) {
    window.document$.subscribe(setUp);
  } else {
    document.addEventListener("DOMContentLoaded", setUp);
  }
})();
