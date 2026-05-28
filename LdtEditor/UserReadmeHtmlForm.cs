using System.Text;
using Markdig;
using Microsoft.Web.WebView2.WinForms;

internal sealed class UserReadmeHtmlForm : Form
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private const string ReadmeHtmlShell = """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"/>
        <style>
        body { font-family: "Microsoft YaHei UI", "Segoe UI", sans-serif; font-size: 14px; line-height: 1.6;
               color: #24292f; margin: 0; padding: 20px 28px 32px; background: #fff;
               max-width: 920px; box-sizing: border-box; }
        h1 { font-size: 1.75em; font-weight: 600; margin: 0 0 0.6em; padding-bottom: 0.35em;
             border-bottom: 1px solid #d8dee4; line-height: 1.25; }
        h2 { font-size: 1.35em; font-weight: 600; margin: 1.4em 0 0.55em; padding-bottom: 0.25em;
             border-bottom: 1px solid #eaeef2; }
        h3 { font-size: 1.12em; font-weight: 600; margin: 1.1em 0 0.45em; }
        p { margin: 0.5em 0 0.85em; }
        strong { font-weight: 600; }
        hr { border: none; border-top: 1px solid #d8dee4; margin: 1.25em 0; }
        blockquote { margin: 0.6em 0; padding: 0.35em 0.9em; color: #57606a;
                     border-left: 4px solid #d0d7de; background: #f6f8fa; }
        blockquote p { margin: 0.35em 0; }
        code { font-family: Consolas, "Cascadia Mono", monospace; font-size: 0.9em;
               background: #f6f8fa; padding: 0.15em 0.4em; border-radius: 4px; }
        pre { background: #f6f8fa; padding: 12px 14px; overflow-x: auto; border-radius: 6px;
              border: 1px solid #d8dee4; margin: 0.65em 0 1em; }
        pre code { background: none; padding: 0; font-size: 0.88em; }
        a { color: #0969da; text-decoration: none; }
        a:hover { text-decoration: underline; }
        ul, ol { margin: 0.4em 0 0.85em; padding-left: 1.6em; }
        li { margin: 0.2em 0; }
        li > p { margin: 0.25em 0; }
        table { border-collapse: collapse; width: 100%; margin: 0.65em 0 1em; font-size: 0.95em; }
        th, td { border: 1px solid #d0d7de; padding: 8px 12px; text-align: left; vertical-align: top; }
        th { background: #f6f8fa; font-weight: 600; }
        tr:nth-child(even) td { background: #f9fafb; }
        </style></head><body>
        """;

    public UserReadmeHtmlForm(string markdown)
    {
        Text = "使用说明";
        Width = 880;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(webView);

        Shown += async (_, _) =>
        {
            try
            {
                await webView.EnsureCoreWebView2Async().ConfigureAwait(true);
                var bodyHtml = Markdown.ToHtml(NormalizeMarkdown(markdown), MarkdownPipeline);
                var html = ReadmeHtmlShell + bodyHtml + "</body></html>";
                webView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"无法加载说明视图（需要 WebView2 运行时）。\n\n{ex.Message}",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Close();
            }
        };
    }

    /// <summary>备用纯文本摘要无 Markdown 结构时，按行拆成段落显示。</summary>
    private static string NormalizeMarkdown(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var trimmed = source.TrimStart('\uFEFF');
        if (trimmed.Contains('#', StringComparison.Ordinal)
            || trimmed.Contains('|', StringComparison.Ordinal)
            || trimmed.Contains("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new StringBuilder(trimmed.Length + 64);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine();
                continue;
            }

            sb.AppendLine(line);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}