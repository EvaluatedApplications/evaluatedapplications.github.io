using System.Text;
using EvalApp.Consumer;

namespace SiteKit.Render;

/// <summary>The one real side effect in the render pipeline: write the composed HTML to disk.
/// Declares ResourceKind.DiskIO and is added via AddStep&lt;WriteStaticFileStep&gt;() so the
/// EvalApp builder auto-gates it — per evalapp-owner's review, this was already correct as
/// sketched, no change needed. Requires a public parameterless ctor (AddStep&lt;TStep&gt;()'s
/// `where TStep : class, new()` constraint).</summary>
public sealed class WriteStaticFileStep : SideEffectStep<PageRenderJob>
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public override ResourceKind? ResourceKind => EvalApp.Consumer.ResourceKind.DiskIO;

    public override async ValueTask<PageRenderJob> ExecuteAsync(PageRenderJob data, CancellationToken ct)
    {
        if (data.OutputPath is null || data.FinalHtml is null)
            throw new InvalidOperationException("WriteStaticFileStep ran before ComposeHtml — OutputPath/FinalHtml is null.");

        var dir = Path.GetDirectoryName(data.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(data.OutputPath, data.FinalHtml, Utf8NoBom, ct);
        return data;
    }
}
