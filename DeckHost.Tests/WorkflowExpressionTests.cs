using DeckHost.Core.Workflows;

namespace DeckHost.Tests;

/// <summary>The expression facade over NCalc: what workflows rely on, so a library upgrade that changes it fails here.</summary>
public sealed class WorkflowExpressionTests
{
    private static readonly Dictionary<string, string> Variables = new(StringComparer.Ordinal)
    {
        ["count"] = "5",
        ["name"] = "Marco",
        ["on"] = "true",
        ["profile:mode"] = "night",
        ["control:taps"] = "3",
        ["ten"] = "10",
    };

    private static WorkflowExpressionResult Eval(string expression, DateTime? now = null) =>
        WorkflowExpression.Evaluate(expression, name => Variables.GetValueOrDefault(name, string.Empty), now is { } at ? () => at : null);

    [TestCase("1 + 2 * 3", "7")]
    [TestCase("(1 + 2) * 3", "9")]
    [TestCase("10 % 4", "2")]
    [TestCase("2 ** 3", "8")]
    [TestCase("[count] * 2", "10")]
    [TestCase("[count] + [ten]", "15")]
    [TestCase("round(2.5)", "3")]
    [TestCase("round(2.567, 1)", "2.6")]
    [TestCase("clamp([count], 0, 3)", "3")]
    [TestCase("0.1 + 0.2", "0.3")]
    public void Arithmetic_follows_precedence_with_invariant_numbers(string expression, string expected)
    {
        var result = Eval(expression);

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(result.Value.Kind, Is.EqualTo(WorkflowValueKind.Number));
        Assert.That(result.Value.Text, Is.EqualTo(expected));
    }

    [TestCase("[count] > 3", true)]
    [TestCase("[count] == 5", true)]
    [TestCase("[ten] > 9", true)]
    [TestCase("[on] && [count] > 3", true)]
    [TestCase("[on] and not [on]", false)]
    [TestCase("[name] == 'Marco'", true)]
    [TestCase("contains([name], 'ARC')", true)]
    [TestCase("startswith([name], 'm')", true)]
    [TestCase("endswith([name], 'x')", false)]
    [TestCase("[count] in (1, 5, 7)", true)]
    [TestCase("[count] > 3 || 1 / 0 > 1", true)]
    public void Comparisons_and_logic_give_true_or_false(string expression, bool expected)
    {
        var result = Eval(expression);

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(result.Value.Kind, Is.EqualTo(WorkflowValueKind.Bool));
        Assert.That(result.Value.Bool, Is.EqualTo(expected));
    }

    [TestCase("[name] + '!'", "Marco!")]
    [TestCase("[count] + ' taps'", "5 taps")]
    [TestCase("upper([name])", "MARCO")]
    [TestCase("lower('ABC') + trim('  x ')", "abcx")]
    [TestCase("replace([name], 'arc', 'ARC')", "MARCo")]
    [TestCase("substr([name], 1, 3)", "arc")]
    [TestCase("substr([name], 2)", "rco")]
    [TestCase("text([count])", "5")]
    [TestCase("[count] > 3 ? 'big' : 'small'", "big")]
    [TestCase("if([count] > 9, 'a', 'b')", "b")]
    public void Text_functions_and_joining_work_and_a_number_joins_text_with_plus(string expression, string expected)
    {
        var result = Eval(expression);

        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(result.Value.Text, Is.EqualTo(expected));
        Assert.That(result.Value.Kind, Is.EqualTo(WorkflowValueKind.Text));
    }

    [Test]
    public void Functions_accept_any_of_three_spellings_and_conversion_functions_convert()
    {
        Assert.That(Eval("len('abc')").Value.Number, Is.EqualTo(3));
        Assert.That(Eval("LEN('abc')").Value.Number, Is.EqualTo(3));
        Assert.That(Eval("Len('abc')").Value.Number, Is.EqualTo(3));
        Assert.That(Eval("number('4') + 1").Value.Number, Is.EqualTo(5));
        Assert.That(Eval("number('') + 1").Value.Number, Is.EqualTo(1), "an empty variable is 0");
        Assert.That(Eval("bool('yes')").Value.Bool, Is.True);
        Assert.That(Eval("bool('off')").Value.Bool, Is.False);
        Assert.That(Eval("bool('maybe')").Ok, Is.False);
    }

    [Test]
    public void Variables_in_brackets_can_name_a_scope_and_an_unset_variable_is_empty()
    {
        Assert.That(Eval("[profile:mode]").Value.Text, Is.EqualTo("night"));
        Assert.That(Eval("[control:taps] + 1").Value.Number, Is.EqualTo(4));
        Assert.That(Eval("len([nothing])").Value.Number, Is.EqualTo(0));
        Assert.That(Eval("[nothing] == ''").Value.Bool, Is.True);
    }

    [Test]
    public void The_time_functions_read_the_clock_and_number_monday_as_one()
    {
        var sunday = new DateTime(2026, 11, 22, 17, 45, 0);
        Assert.That(Eval("hour()", sunday).Value.Number, Is.EqualTo(17));
        Assert.That(Eval("minute()", sunday).Value.Number, Is.EqualTo(45));
        Assert.That(Eval("weekday()", sunday).Value.Number, Is.EqualTo(7));
        Assert.That(Eval("weekday()", sunday.AddDays(1)).Value.Number, Is.EqualTo(1));
        Assert.That(Eval("hour() >= 9 && hour() < 18", sunday).Value.Bool, Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("1 +")]
    [TestCase("(1")]
    [TestCase("nofunction(1)")]
    [TestCase("1 / 0")]
    [TestCase("pow(2, 100000)")]
    [TestCase("[count] + 'x' * 2")]
    [TestCase("max(1, 'a')")]
    [TestCase("len()")]
    [TestCase("substr('a')")]
    public void A_bad_expression_is_a_message_never_an_exception(string expression)
    {
        var result = Eval(expression);

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void An_overlong_expression_is_refused_before_it_is_parsed()
    {
        var result = Eval(new string('1', WorkflowExpression.MaxLength + 1));

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("longer than"));
    }

    [Test]
    public void Only_the_listed_functions_exist_and_the_error_names_the_unknown_one()
    {
        var result = Eval("system('rm')");

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("system"));
    }

    [TestCase("true", WorkflowValueKind.Bool)]
    [TestCase("FALSE", WorkflowValueKind.Bool)]
    [TestCase("12", WorkflowValueKind.Number)]
    [TestCase("-3.5", WorkflowValueKind.Number)]
    [TestCase("1e3", WorkflowValueKind.Number)]
    [TestCase("12,5", WorkflowValueKind.Text)]
    [TestCase("", WorkflowValueKind.Text)]
    [TestCase("NaN", WorkflowValueKind.Text)]
    [TestCase("hello", WorkflowValueKind.Text)]
    public void Stored_text_is_read_back_with_its_type(string stored, WorkflowValueKind expected) =>
        Assert.That(WorkflowValue.Parse(stored).Kind, Is.EqualTo(expected));

    [TestCase("true", true)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("", false)]
    [TestCase("false", false)]
    [TestCase("something", true)]
    public void What_an_If_node_makes_of_a_value(string stored, bool expected) =>
        Assert.That(WorkflowValue.Parse(stored).IsTrue, Is.EqualTo(expected));
}
