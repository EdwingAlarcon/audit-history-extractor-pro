using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AuditHistoryExtractorPro.Desktop;

/// <summary>
/// Modal window that shows a side-by-side visual diff of two text values
/// using DiffPlex for inline character-level highlighting.
/// </summary>
public partial class DiffViewerWindow : Window
{
    private static readonly SolidColorBrush DeletedLineBrush = new(Color.FromRgb(255, 205, 210));   // Light Red
    private static readonly SolidColorBrush DeletedCharBrush = new(Color.FromRgb(239, 154, 154));   // Medium Red
    private static readonly SolidColorBrush InsertedLineBrush = new(Color.FromRgb(200, 230, 201));  // Light Green
    private static readonly SolidColorBrush InsertedCharBrush = new(Color.FromRgb(165, 214, 167)); // Medium Green
    private static readonly SolidColorBrush ImaginaryLineBrush = new(Color.FromRgb(238, 238, 238)); // Gray
    private static readonly SolidColorBrush UnchangedBrush = Brushes.Transparent;

    public DiffViewerWindow(string oldText, string newText, string? fieldName = null)
    {
        InitializeComponent();
        FieldNameText.Text = string.IsNullOrWhiteSpace(fieldName)
            ? "Campo no especificado"
            : $"Campo: {fieldName}";

        RenderDiff(oldText ?? string.Empty, newText ?? string.Empty);
    }

    private void RenderDiff(string oldText, string newText)
    {
        var differ = new Differ();
        var builder = new SideBySideDiffBuilder(differ);
        var diffModel = builder.BuildDiffModel(oldText, newText);

        RenderPanel(OldValueBox, diffModel.OldText);
        RenderPanel(NewValueBox, diffModel.NewText);
    }

    private static void RenderPanel(System.Windows.Controls.RichTextBox box, DiffPaneModel pane)
    {
        var document = new FlowDocument
        {
            PageWidth = 2000,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 13
        };

        foreach (var line in pane.Lines)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };

            switch (line.Type)
            {
                case ChangeType.Deleted:
                    paragraph.Background = DeletedLineBrush;
                    AddSubPieces(paragraph, line, DeletedCharBrush);
                    break;

                case ChangeType.Inserted:
                    paragraph.Background = InsertedLineBrush;
                    AddSubPieces(paragraph, line, InsertedCharBrush);
                    break;

                case ChangeType.Modified:
                    paragraph.Background = line.Type == ChangeType.Deleted ? DeletedLineBrush : InsertedLineBrush;
                    AddSubPieces(paragraph, line, line.Type == ChangeType.Deleted ? DeletedCharBrush : InsertedCharBrush);
                    break;

                case ChangeType.Imaginary:
                    paragraph.Background = ImaginaryLineBrush;
                    paragraph.Inlines.Add(new Run(" ") { Foreground = Brushes.Transparent });
                    break;

                case ChangeType.Unchanged:
                default:
                    paragraph.Background = UnchangedBrush;
                    paragraph.Inlines.Add(new Run(line.Text ?? string.Empty));
                    break;
            }

            document.Blocks.Add(paragraph);
        }

        box.Document = document;
    }

    private static void AddSubPieces(Paragraph paragraph, DiffPiece line, SolidColorBrush highlightBrush)
    {
        if (line.SubPieces == null || line.SubPieces.Count == 0)
        {
            paragraph.Inlines.Add(new Run(line.Text ?? string.Empty));
            return;
        }

        foreach (var subPiece in line.SubPieces)
        {
            if (string.IsNullOrEmpty(subPiece.Text))
            {
                continue;
            }

            var run = new Run(subPiece.Text);

            if (subPiece.Type == ChangeType.Deleted ||
                subPiece.Type == ChangeType.Inserted ||
                subPiece.Type == ChangeType.Modified)
            {
                run.Background = highlightBrush;
                run.FontWeight = FontWeights.Bold;
            }

            paragraph.Inlines.Add(run);
        }

        // If no inline was added (all sub-pieces were empty), add the full text
        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.Add(new Run(line.Text ?? string.Empty));
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
