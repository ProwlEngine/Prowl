using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.Panels;

[EditorWindow("Prowl/News")]
public class NewsPanel : DockPanel
{
    public override string Title => "News";
    public override string Icon => EditorIcons.Newspaper;

    private const float ToolbarHeight = 30f;
    private const float CardInnerPad = 12f;
    private const float TitleHeight = 20f;
    private const float DateHeight = 14f;
    private const float DescHeight = 30f;
    private const float BtnHeight = 26f;
    private const float CardHeight = CardInnerPad + TitleHeight + 4f + DateHeight + 6f + DescHeight + 8f + BtnHeight + CardInnerPad;

    private List<NewsPost> _posts = [];
    private bool _isLoading;
    private string? _loadError;
    private bool _initialized;

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (!_initialized && !_isLoading)
        {
            _initialized = true;
            _ = LoadPostsAsync();
        }

        using (paper.Column("news_root").Size(width, height).Enter())
        {
            DrawToolbar(paper);
            DrawContent(paper, font, width, height - ToolbarHeight);
        }
    }

    private void DrawToolbar(Paper paper)
    {
        using (paper.Row("news_toolbar")
            .Height(ToolbarHeight)
            .ChildLeft(6).ChildRight(6).ChildTop(4).ChildBottom(4)
            .RowBetween(8)
            .Enter())
        {
            EditorGUI.Button(paper, "news_refresh", $"{EditorIcons.ArrowsRotate}  Refresh", width: 86)
                .OnValueChanged(clicked =>
                {
                    if (!_isLoading)
                        _ = LoadPostsAsync();
                });
        }
    }

    private void DrawContent(Paper paper, FontFile font, float width, float height)
    {
        if (_isLoading)
        {
            paper.Box("news_loading").Size(width, height)
                .Text("Loading news...", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        if (_loadError != null)
        {
            paper.Box("news_error").Size(width, height)
                .Text($"Could not load news: {_loadError}", font)
                .TextColor(Color.FromArgb(255, 220, 80, 80))
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        if (_posts.Count == 0)
        {
            paper.Box("news_empty").Size(width, height)
                .Text("No news posts yet.", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        float cardWidth = width - 16f;

        Origami.ScrollView(paper, "news_scroll", width, height)
            .Padding(8f, 8f, 8f, 8f)
            .ColSpacing(6f)
            .Body(() =>
            {
                for (int i = 0; i < _posts.Count; i++)
                    DrawCard(paper, font, i, _posts[i], cardWidth);
            });
    }

    private static void DrawCard(Paper paper, FontFile font, int index, NewsPost post, float cardWidth)
    {
        string dateStr = post.PublishedAt.HasValue
            ? post.PublishedAt.Value.ToString("MMMM d, yyyy")
            : "Draft";

        bool canOpen = !string.IsNullOrEmpty(post.Slug);
        string slug = post.Slug ?? "";

        using (paper.Column($"news_card_{index}")
            .Width(cardWidth)
            .Height(CardHeight)
            .BackgroundColor(EditorTheme.Neutral400)
            .Rounded(4f)
            .ChildLeft(CardInnerPad).ChildRight(CardInnerPad)
            .ChildTop(CardInnerPad).ChildBottom(CardInnerPad)
            .ColBetween(4f)
            .Enter())
        {
            paper.Box($"news_card_{index}_title")
                .Height(TitleHeight)
                .Text(post.Title, font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(13f)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box($"news_card_{index}_date")
                .Height(DateHeight)
                .Text(dateStr, font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(11f)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box($"news_card_{index}_desc")
                .Height(DescHeight)
                .Text(post.Description, font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(11f)
                .Alignment(TextAlignment.Left)
                .Wrap(TextWrapMode.Wrap)
                .Clip();

            using (paper.Row($"news_card_{index}_btns")
                .Height(BtnHeight)
                .RowBetween(0)
                .Enter())
            {
                paper.Box($"news_card_{index}_spacer").Width(UnitValue.Stretch(1));

                if (canOpen)
                {
                    EditorGUI.Button(paper, $"news_card_{index}_read", $"{EditorIcons.ArrowUpRightFromSquare}  Read More", width: 108)
                        .OnValueChanged(clicked => OpenUrl($"https://prowlengine.com/news/{slug}"));
                }
            }
        }
    }

    private async Task LoadPostsAsync()
    {
        _isLoading = true;
        _loadError = null;
        try
        {
            _posts = await ProwlService.FetchNewsPostsAsync();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            _posts = [];
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
