# Prowl.Quire

A fast Markdown parser for the Prowl Game Engine ecosystem. Quire turns Markdown
text into a flat tree of blocks and inlines. It has no fonts, no layout and no rendering, so it can
be used anywhere.

## Usage

```csharp
using Prowl.Quire;

var document = MarkdownDocument.Parse(source);

foreach (int block in document.ChildrenOf(document.Root))
{
    var node = document.GetBlock(block);
    switch (node.Kind)
    {
        case BlockKind.Heading:
            Console.WriteLine($"h{node.Level}: {document.BlockText(block)}");
            break;

        case BlockKind.CodeBlock:
            Console.WriteLine($"code ({document.TextOf(node.Info)}): {document.TextOf(node.Text)}");
            break;
    }
}
```

### The tree

Blocks and inlines live in two flat arrays and are linked by index. `FirstChild` and `NextSibling` walk the tree, and `-1` terminates. There are no
per-node lists and no substrings.

```csharp
foreach (int child in document.ChildrenOf(block)) { }        // block children
foreach (int inline in document.InlinesOf(block)) { }        // a block's inline run
foreach (int child in document.InlineChildrenOf(inline)) { } // nested inlines
```

Three text accessors, which differ in what they cover:

```csharp
document.TextOf(span)            // one TextSpan
document.PlainText(inline)       // one inline and its descendants
document.PlainTextRun(first)     // an inline, its siblings and their descendants
document.BlockText(block)        // every inline attached to a block
```

A link label is a *run*, so `PlainTextRun(link.FirstChild)` is what reads it.