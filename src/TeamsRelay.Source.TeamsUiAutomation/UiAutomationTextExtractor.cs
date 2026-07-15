using System.Windows.Automation;
using System.Runtime.InteropServices;
using TeamsRelay.Core;

namespace TeamsRelay.Source.TeamsUiAutomation;

internal interface IUiAutomationNode
{
    string Name { get; }

    string ControlTypeProgrammaticName { get; }

    IEnumerable<IUiAutomationNode> EnumerateChildren();
}

internal static class UiAutomationTextExtractor
{
    internal const int MaxParts = 12;
    internal const int MaxVisitedElements = 256;
    internal const int MaxDepth = 12;

    public static string ExtractText(IUiAutomationNode root)
    {
        var parts = new List<string>(MaxParts);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Append(parts, seen, root.Name);

        var visited = 1;
        var stack = new Stack<NodeFrame>();
        PushChildren(root, depth: 1, stack);

        while (stack.Count > 0 && parts.Count < MaxParts && visited < MaxVisitedElements)
        {
            var frame = stack.Pop();
            visited++;

            if (IsAllowedControlType(frame.Node.ControlTypeProgrammaticName))
            {
                Append(parts, seen, frame.Node.Name);
            }

            if (parts.Count >= MaxParts || frame.Depth >= MaxDepth || visited >= MaxVisitedElements)
            {
                continue;
            }

            PushChildren(frame.Node, frame.Depth + 1, stack);
        }

        return string.Join(" | ", parts);
    }

    public static string ExtractText(AutomationElement root)
    {
        return ExtractText(new AutomationElementNode(root));
    }

    private static void PushChildren(IUiAutomationNode node, int depth, Stack<NodeFrame> stack)
    {
        var children = new List<IUiAutomationNode>();
        foreach (var child in node.EnumerateChildren())
        {
            children.Add(child);
        }

        for (var index = children.Count - 1; index >= 0; index--)
        {
            stack.Push(new NodeFrame(children[index], depth));
        }
    }

    private static void Append(List<string> parts, HashSet<string> seen, string? value)
    {
        var normalized = TextUtilities.NormalizeWhitespace(value);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 2 || !seen.Add(normalized))
        {
            return;
        }

        parts.Add(normalized);
    }

    private static bool IsAllowedControlType(string programmaticName)
    {
        return programmaticName.EndsWith(".Text", StringComparison.Ordinal)
            || programmaticName.EndsWith(".Document", StringComparison.Ordinal)
            || programmaticName.EndsWith(".ListItem", StringComparison.Ordinal)
            || programmaticName.EndsWith(".Custom", StringComparison.Ordinal)
            || programmaticName.EndsWith(".Pane", StringComparison.Ordinal)
            || programmaticName.EndsWith(".Group", StringComparison.Ordinal);
    }

    private readonly record struct NodeFrame(IUiAutomationNode Node, int Depth);

    private sealed class AutomationElementNode : IUiAutomationNode
    {
        private readonly AutomationElement _element;

        public AutomationElementNode(AutomationElement element)
        {
            _element = element;
        }

        public string Name
        {
            get
            {
                try
                {
                    return _element.Current.Name ?? string.Empty;
                }
                catch (ElementNotAvailableException)
                {
                    return string.Empty;
                }
                catch (COMException)
                {
                    return string.Empty;
                }
                catch (InvalidOperationException)
                {
                    return string.Empty;
                }
            }
        }

        public string ControlTypeProgrammaticName
        {
            get
            {
                try
                {
                    return _element.Current.ControlType.ProgrammaticName ?? string.Empty;
                }
                catch (ElementNotAvailableException)
                {
                    return string.Empty;
                }
                catch (COMException)
                {
                    return string.Empty;
                }
                catch (InvalidOperationException)
                {
                    return string.Empty;
                }
            }
        }

        public IEnumerable<IUiAutomationNode> EnumerateChildren()
        {
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? child;

            try
            {
                child = walker.GetFirstChild(_element);
            }
            catch (ElementNotAvailableException)
            {
                yield break;
            }
            catch (COMException)
            {
                yield break;
            }
            catch (InvalidOperationException)
            {
                yield break;
            }

            while (child is not null)
            {
                yield return new AutomationElementNode(child);

                try
                {
                    child = walker.GetNextSibling(child);
                }
                catch (ElementNotAvailableException)
                {
                    yield break;
                }
                catch (COMException)
                {
                    yield break;
                }
                catch (InvalidOperationException)
                {
                    yield break;
                }
            }
        }
    }
}
