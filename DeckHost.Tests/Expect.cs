namespace DeckHost.Tests;

/// <summary>
/// Assertions that also return what they checked, built on NUnit's: the one item of a collection,
/// a value as its type, every item checked.
/// </summary>
internal static class Expect
{
    /// <summary>The collection's only item (fails when it has none or several).</summary>
    public static T Single<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        Assert.That(list, Has.Exactly(1).Items);
        return list[0];
    }

    /// <summary>The only item that matches (fails when none or several do).</summary>
    public static T Single<T>(IEnumerable<T> items, Func<T, bool> match)
    {
        var matching = items.Where(match).ToList();
        Assert.That(matching, Has.Exactly(1).Items, "items that match");
        return matching[0];
    }

    /// <summary>The value as exactly <typeparamref name="T"/> (fails for another type).</summary>
    public static T IsType<T>(object? value)
    {
        Assert.That(value, Is.TypeOf<T>());
        return (T)value!;
    }

    /// <summary>Runs <paramref name="check"/> on every item and reports all failures together.</summary>
    public static void All<T>(IEnumerable<T> items, Action<T> check) =>
        Assert.Multiple(() =>
        {
            foreach (var item in items)
            {
                check(item);
            }
        });
}
