using System.Windows;
using WinTabber.UI.Common.Hints;
using WinTabberUI.Infrastructure;

namespace WinTabber.Infrastructure.Tests;

public class TrieNodeTests
{
    [Test]
    public async Task  Basic()
    {
        string[] prefixes = [
            "CAT",
            "ABC",
            "TAC",
            "AB",
            "A"
        ];

        TrieItem<string>[] items = prefixes.Select(prefix => new TrieItem<string>(prefix, prefix)).ToArray();


        var node = HintTrie.Build(items);

        var result = TrieNode<string>.Search(node, "A").ToArray();

        Assert.Equals(result.Length, 3);
        await Assert.That(() => result.Any(s => s.Element == "ABC")).IsTrue();
        await Assert.That(() => result.Any(s => s.Element == "AB")).IsTrue();
        await Assert.That(() => result.Any(s => s.Element == "A")).IsTrue();
    }

    [Test]
    public async Task RadixTrieInsert()
    {
        var pool = new StringPool();
        var root = new RadixNode<int>();

        RadixTrie<int>.Insert(root, "ABC".AsSpan(), 1, pool);
        RadixTrie<int>.Insert(root, "AB".AsSpan(), 2, pool);
        RadixTrie<int>.Insert(root, "A1".AsSpan(), 3, pool);
        RadixTrie<int>.Insert(root, "ABCDE".AsSpan(), 4, pool);

        var x = RadixTrie<int>.TryGet(root, "A", out var xx);
        Assert.Equals(RadixTrie<int>.TryGet(root, "ABC", out var v1), true);
        Assert.Equals(v1, 1);
    }

    [Test]
    public async Task RadixSearchTest()
    {
        // Arrange
        var pool = new StringPool();
        var root = new RadixNode<int>();

        RadixTrie<int>.Insert(root, "ABC".AsSpan(), 1, pool);
        RadixTrie<int>.Insert(root, "AB".AsSpan(), 2, pool);
        RadixTrie<int>.Insert(root, "A1".AsSpan(), 3, pool);
        RadixTrie<int>.Insert(root, "B".AsSpan(), 4, pool);
        RadixTrie<int>.Insert(root, "BCD".AsSpan(), 5, pool);

        // Act
        var results = RadixTrie<int>.FindByPrefix(root, "A".AsSpan()).ToArray();

        // Assert
        await Assert.That(results).Contains(1); // ABC
        await Assert.That(results).Contains(2); // AB
        await Assert.That(results).Contains(3); // A1

        await Assert.That(results).DoesNotContain(4); // B
        await Assert.That(results).DoesNotContain(5); // BCD

        await Assert.That(results.Length).IsEqualTo(3);
    }

    [Test]
    public async Task HintDictionary()
    {
        var p = new GeneratedHintsProvider();

        IEnumerable<FrameworkElement> elements = Enumerable.Range(0, 20).Select(_ => (FrameworkElement)null!);

        var x = p.GetHints(elements).ToArray();


    }

    //[Test]
    //[Arguments(1, 2, 3)]
    //[Arguments(2, 3, 5)]
    //public async Task DataDrivenArguments(int a, int b, int c)
    //{
    //    Console.WriteLine("This one can accept arguments from an attribute");

    //    var result = a + b;

    //    await Assert.That(result).IsEqualTo(c);
    //}


}
