namespace WinTabberUI.Infrastructure;


public sealed class RadixNode<T>
{
    public List<Edge<T>> Edges { get; } = new();

    public bool HasValue { get; set; }   // explicit presence
    public T Value { get; set; } = default!;
}

public sealed class Edge<T>
{
    public string Label { get; }
    public RadixNode<T> Target { get; }

    public Edge(string label, RadixNode<T> target)
    {
        Label = label;
        Target = target;
    }
}

