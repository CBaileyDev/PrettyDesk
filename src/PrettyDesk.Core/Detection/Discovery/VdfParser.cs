using System.Text;

namespace PrettyDesk.Core.Detection.Discovery;

/// <summary>A node of Valve's KeyValues text format: either a string value or a set of children.</summary>
public sealed class VdfNode
{
    public string? Value { get; init; }

    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.GetValueOrDefault(key);

    public string? Text(string key) => Children.GetValueOrDefault(key)?.Value;
}

/// <summary>
/// Minimal, forgiving parser for <c>libraryfolders.vdf</c> and <c>appmanifest_*.acf</c> (FR-DET-9). Malformed input
/// yields an empty/partial tree instead of throwing: a broken launcher file must never break startup.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var root = new VdfNode();
        var position = 0;
        ParseBlock(text, ref position, root, depth: 0);
        return root;
    }

    private static void ParseBlock(string text, ref int position, VdfNode into, int depth)
    {
        while (true)
        {
            var key = NextToken(text, ref position, out var kind);
            if (kind is TokenKind.End or TokenKind.CloseBrace)
            {
                return;
            }

            if (kind != TokenKind.String || key is null || depth > 32)
            {
                return;
            }

            var value = NextToken(text, ref position, out kind);
            if (kind == TokenKind.OpenBrace)
            {
                var child = new VdfNode();
                ParseBlock(text, ref position, child, depth + 1);
                into.Children[key] = child;
            }
            else if (kind == TokenKind.String)
            {
                into.Children[key] = new VdfNode { Value = value };
            }
            else
            {
                return;
            }
        }
    }

    private enum TokenKind
    {
        String,
        OpenBrace,
        CloseBrace,
        End,
    }

    private static string? NextToken(string text, ref int position, out TokenKind kind)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
            }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }
            else
            {
                break;
            }
        }

        if (position >= text.Length)
        {
            kind = TokenKind.End;
            return null;
        }

        switch (text[position])
        {
            case '{':
                position++;
                kind = TokenKind.OpenBrace;
                return null;
            case '}':
                position++;
                kind = TokenKind.CloseBrace;
                return null;
            case '"':
                position++;
                var builder = new StringBuilder();
                while (position < text.Length && text[position] != '"')
                {
                    if (text[position] == '\\' && position + 1 < text.Length)
                    {
                        position++;
                        builder.Append(text[position] switch { 'n' => '\n', 't' => '\t', var other => other });
                    }
                    else
                    {
                        builder.Append(text[position]);
                    }

                    position++;
                }

                position++;
                kind = TokenKind.String;
                return builder.ToString();
            default:
                kind = TokenKind.End;
                return null;
        }
    }
}
