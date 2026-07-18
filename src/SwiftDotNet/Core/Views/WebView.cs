namespace SwiftDotNet;

/// <summary>
/// Embeds live web content. Point it at a URL (<c>new WebView("https://swift.org")</c>) or render an
/// HTML string (<c>WebView.FromHtml("&lt;h1&gt;Hi&lt;/h1&gt;")</c>).
/// </summary>
/// <remarks>
/// Backed by the platform's native web engine — WKWebView (iOS/macOS), WebView2 (Windows),
/// android.webkit.WebView (Android), and an <c>&lt;iframe&gt;</c> on the Web backend. tvOS has no web
/// engine, so it renders a placeholder. GTK falls back to a link unless WebKitGTK is present.
/// </remarks>
public sealed class WebView : View
{
    readonly string? _url;
    readonly string? _html;

    /// <summary>Load a remote page by URL.</summary>
    public WebView(string url) => _url = url;

    WebView(string? url, string? html)
    {
        _url = url;
        _html = html;
    }

    /// <summary>Render an HTML string directly, with no network round-trip.</summary>
    public static WebView FromHtml(string html) => new(url: null, html: html);

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("WebView", path);
        if (_url is not null) node.Props["url"] = _url;
        if (_html is not null) node.Props["html"] = _html;
        return node;
    }
}
