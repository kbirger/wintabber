using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace WinTabberUI.Infrastructure;

public static class HintTrie
{
    public static TrieNode<T> Build<T>(IEnumerable<TrieItem<T>> values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));

        var root = new TrieNode<T>(default);

        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value.Prefix))
                continue; // or throw, depending on requirements

            var current = root;

            foreach (var ch in value.Prefix[0..^1])
            {
                current.TryAddValue(ch, default, out var next);

                current = next;
            }

            current.TryAddValue(value.Prefix[^1], value.Element, out var _);
        }

        return root;
    }
}

public record struct TrieItem<T>(string Prefix, T Element);

public class TrieNode<T>
{

    public TrieNode(T? element)
    {
        Element = element;

    }
    public T? Element { get; private set; }

    public void SetElement(T element)
    {
        if(Element is not null)
        {
            throw new InvalidOperationException("duplicate element");
        }

        Element = element;
    }
    public bool IsTerminal => Element is not null;


    private Dictionary<char, TrieNode<T>> _children = new Dictionary<char, TrieNode<T>>();
    public IReadOnlyDictionary<char, TrieNode<T>> Children => _children;

    public bool TryAddValue(char ch, T? element, out TrieNode<T> next)
    {
        if(!Children.TryGetValue(ch, out next))
        {
            next = new TrieNode<T>(element);
            _children[ch] = next;
            return false;
        }

        if(element is not null)
        {
            next.SetElement(element);
        }

        return true;
    }

    public static IEnumerable<TrieNode<T>> Search(TrieNode<T> root, string prefix)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        if (prefix == null) throw new ArgumentNullException(nameof(prefix));

        // Step 1: walk to the prefix node
        var current = root;
        foreach (var ch in prefix)
        {
            if (!current.Children.TryGetValue(ch, out var next))
                yield break; // prefix not found

            current = next;
        }

        // Step 2: enumerate terminal nodes in this subtree
        foreach (var node in EnumerateTerminals(current))
            yield return node;
    }

    private static IEnumerable<TrieNode<T>> EnumerateTerminals(TrieNode<T> node)
    {
        if (node.IsTerminal)
            yield return node;

        foreach (var child in node.Children.Values)
        {
            foreach (var match in EnumerateTerminals(child))
                yield return match;
        }
    }


}
