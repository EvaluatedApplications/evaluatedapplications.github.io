namespace Showroom.Services.PrismTab;

public enum TabId { Chat, Stories, Map, Creature, Forecaster }

/// <summary>
/// The five tabs of the Prism tool: slug (the <c>#</c> and <c>?tab=</c> value), title, and the real heading plus lede the static
/// shell and the live tab both show. The ledes carry no numbers: counts, accuracies and sizes are read off the loaded model or a
/// scoring run and shown by the tab itself.
/// </summary>
public static class PrismTabs
{
    public sealed record Info(TabId Tab, string Slug, string Title, string Heading, string Lede);

    public static readonly Info[] All =
    {
        new(TabId.Chat, "chat", "Chat", "Chat with Prism",
            "A tiny AI, running in your browser. Say something and it replies. It is still learning, so the replies are rough."),
        new(TabId.Stories, "stories", "Stories", "Nano Stories",
            "The same model, asked to write a story instead of hold a conversation. Give it an opening line and watch it continue, live, one word piece at a time, right here in your browser."),
        new(TabId.Map, "map", "Map", "The Cartographer",
            "Every word this model knows lives at a point in a real, high-dimensional space. Type a prompt and watch the exact path its own next-word decision takes through that space, decoded straight off the model's own forward pass."),
        new(TabId.Creature, "creature", "Creature", "The Creature",
            "The same model as the chat, reading a window of a grid as text and answering with a move. Press Run: it plays and trains at the same time."),
        new(TabId.Forecaster, "forecaster", "Forecaster", "The Forecaster",
            "The same model as the chat, reading hourly stock moves as words and calling the next one. Press Run: it plays and trains at the same time."),
    };

    public static Info Of(TabId tab) => All[(int)tab];

    /// <summary>Slug (or an old tool name) to a tab; null when it names none.</summary>
    public static TabId? FromSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        slug = slug.Trim().TrimStart('#').ToLowerInvariant();
        if (slug == "cartographer") return TabId.Map;   // the old tool's name
        foreach (var i in All) if (i.Slug == slug) return i.Tab;
        return null;
    }

    /// <summary>The tab a URL names: the fragment first, then <c>?tab=</c>; null when it names none (for example #explain-title).</summary>
    public static TabId? Named(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return null;
        var fromHash = FromSlug(u.Fragment);
        if (fromHash is not null) return fromHash;
        foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "tab" && FromSlug(Uri.UnescapeDataString(kv[1])) is { } t) return t;
        }
        return null;
    }

    /// <summary>The tab a URL names, Chat when it names none.</summary>
    public static TabId FromUri(string uri) => Named(uri) ?? TabId.Chat;
}