namespace Showroom;

/// <summary>
/// Where the big data files live: the council data and the Prism checkpoint are served from the separate
/// website-data repo (GitHub Pages, same origin as the site), not from this app's own wwwroot, so the site
/// repo and its deploy stop growing with data. ONE setting, <c>window.EA_DATA_BASE</c> at the top of wwwroot/index.html (read once at boot, see Program.cs):
///   production   "/website-data/"  (origin-relative: https://evaluatedapplications.github.io/website-data/, same origin, no CORS)
///   localhost    "website-data/"   (index.html switches on location.hostname; a directory junction under wwwroot, see scripts/link-data.ps1)
/// A path is given relative to the website-data root, e.g. <c>DataUrl.For("prism/oracle-vocab.txt")</c>.
/// Relative values resolve against the page's base href (/tools/), so both forms work with a plain HttpClient.
/// Small files that belong to one tool (forecaster-history.json, content.wal) still live in wwwroot/data and are not routed here.
/// </summary>
public static class DataUrl
{
    public static string Base { get; private set; } = "/website-data/";

    public static void Configure(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        Base = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }

    public static string For(string path) => Base + path.TrimStart('/');
}
