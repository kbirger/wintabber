using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabberUI.Infrastructure;

using System;

public class RadixTrie<T>
{
    private readonly RadixNode<T> _root;
    private readonly StringPool _stringPool;

    public RadixTrie(StringPool stringPool)
    {
        _root = new RadixNode<T>();
        _stringPool = stringPool;
    }

    public void Insert(ReadOnlySpan<char> key, T item)
    {
        RadixTrie<T>.Insert(_root, key, item, _stringPool);
    }

    public IReadOnlyList<T> FindByPrefix(ReadOnlySpan<char> prefix)
    {
        return RadixTrie<T>.FindByPrefix(_root, prefix);
    }



    public static void Insert(
        RadixNode<T> root,
        ReadOnlySpan<char> key,
        T value,
        StringPool pool)
    {
        var node = root;
        var remaining = key;

        while (remaining.Length > 0)
        {
            bool matchedEdge = false;

            for (int i = 0; i < node.Edges.Count; i++)
            {
                var edge = node.Edges[i];
                var edgeSpan = edge.Label.AsSpan();
                int common = CommonPrefixLength(edgeSpan, remaining);

                if (common == 0)
                    continue;

                matchedEdge = true;

                // Full edge match
                if (common == edgeSpan.Length)
                {
                    node = edge.Target;
                    remaining = remaining.Slice(common);

                    if (remaining.Length == 0)
                    {
                        node.Value = value;
                        node.HasValue = true;
                        return;
                    }

                    break;
                }

                // Partial match → split edge
                var splitNode = new RadixNode<T>();

                // Existing suffix
                var suffix = pool.Canonicalize(edgeSpan.Slice(common));
                splitNode.Edges.Add(new Edge<T>(suffix, edge.Target));

                // Replace old edge with prefix
                node.Edges[i] = new Edge<T>(
                    pool.Canonicalize(edgeSpan.Slice(0, common)),
                    splitNode
                );

                // Remaining key
                if (remaining.Length == common)
                {
                    splitNode.Value = value;
                    splitNode.HasValue = true;
                }
                else
                {
                    var rem = pool.Canonicalize(remaining.Slice(common));
                    splitNode.Edges.Add(
                        new Edge<T>(
                            rem,
                            new RadixNode<T>
                            {
                                Value = value,
                                HasValue = true
                            })
                    );
                }

                return;
            }

            // No edge matched → add new
            if (!matchedEdge)
            {
                var label = pool.Canonicalize(remaining);
                node.Edges.Add(
                    new Edge<T>(
                        label,
                        new RadixNode<T>
                        {
                            Value = value,
                            HasValue = true
                        })
                );
                return;
            }
        }
    }

    private static int CommonPrefixLength(
        ReadOnlySpan<char> a,
        ReadOnlySpan<char> b)
    {
        int len = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < len && a[i] == b[i]) i++;
        return i;
    }

    public static bool TryGet(
       RadixNode<T> root,
       ReadOnlySpan<char> key,
       out T value)
    {
        var node = root;
        var remaining = key;

        while (remaining.Length > 0)
        {
            Edge<T>? match = null;

            foreach (var edge in node.Edges)
            {
                if (remaining.StartsWith(edge.Label.AsSpan(),
                                          StringComparison.Ordinal))
                {
                    match = edge;
                    break;
                }
            }

            if (match == null)
            {
                value = default!;
                return false;
            }

            remaining = remaining.Slice(match.Label.Length);
            node = match.Target;
        }

        if (node.HasValue)
        {
            value = node.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public static IReadOnlyList<T> FindByPrefix(
    RadixNode<T> root,
    ReadOnlySpan<char> prefix)
    {
        var results = new List<T>();

        if (TryFindNodeForPrefix(root, prefix, out var node, out bool includeNode))
        {
            if (includeNode && node.HasValue)
                results.Add(node.Value);

            Collect(node, results);
        }

        return results;
    }

    private static void Collect(RadixNode<T> node, List<T> results)
    {
        foreach (var edge in node.Edges)
        {
            var child = edge.Target;

            if (child.HasValue)
                results.Add(child.Value);

            Collect(child, results);
        }
    }


    private static bool TryFindNodeForPrefix(
    RadixNode<T> node,
    ReadOnlySpan<char> prefix,
    out RadixNode<T> result,
    out bool includeNode)
    {
        includeNode = false;

        while (prefix.Length > 0)
        {
            bool matched = false;

            foreach (var edge in node.Edges)
            {
                var label = edge.Label.AsSpan();
                int common = CommonPrefixLength(label, prefix);

                if (common == 0)
                    continue;

                // Prefix fully consumed inside this edge
                if (common == prefix.Length)
                {
                    result = edge.Target;
                    includeNode = true;
                    return true;
                }

                // Edge fully matched, continue down
                if (common == label.Length)
                {
                    prefix = prefix.Slice(common);
                    node = edge.Target;
                    matched = true;
                    break;
                }

                // Partial mismatch → prefix not present
                result = null!;
                return false;
            }

            if (!matched)
            {
                result = null!;
                return false;
            }
        }

        result = node;
        includeNode = true;
        return true;
    }


    private static IEnumerable<T> Enumerate(RadixNode<T> node)
    {
        if (node.HasValue)
            yield return node.Value;

        foreach (var edge in node.Edges)
        {
            foreach (var value in Enumerate(edge.Target))
                yield return value;
        }
    }
}
