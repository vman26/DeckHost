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

    private const string NightMode = "night";

    // ---------------------------------------------------------------- arithmetic

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
    public void Evaluate_Arithmetic_FollowsPrecedenceWithInvariantNumbers(string expression, string expected)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(result.Value.Kind, Is.EqualTo(WorkflowValueKind.Number));
        Assert.That(result.Value.Text, Is.EqualTo(expected));
    }

    // ---------------------------------------------------------------- comparisons and logic

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
    public void Evaluate_ComparisonsAndLogic_GiveTrueOrFalse(string expression, bool expected)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(result.Value.Kind, Is.EqualTo(WorkflowValueKind.Bool));
        Assert.That(result.Value.Bool, Is.EqualTo(expected));
    }

    // ---------------------------------------------------------------- text

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
    public void Evaluate_TextFunctionsAndJoining_GiveText(string expression, string expected)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(result.Value.Text, Is.EqualTo(expected));
        Assert.That(result.Value.Kind, Is.EqualTo(WorkflowValueKind.Text));
    }

    // ---------------------------------------------------------------- functions

    [TestCase("len('abc')")]
    [TestCase("LEN('abc')")]
    [TestCase("Len('abc')")]
    public void Evaluate_AFunctionInAnyOfThreeSpellings_Works(string expression)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Value.Number, Is.EqualTo(3));
    }

    [TestCase("number('4') + 1", 5)]
    [TestCase("number('') + 1", 1)] // an empty variable is 0
    public void Evaluate_NumberConversion_ReadsTextAsANumber(string expression, double expected)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Value.Number, Is.EqualTo(expected));
    }

    [TestCase("bool('yes')", true)]
    [TestCase("bool('off')", false)]
    public void Evaluate_BoolConversion_ReadsWordsAsTrueOrFalse(string expression, bool expected)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Value.Bool, Is.EqualTo(expected));
    }

    [Test]
    public void Evaluate_BoolConversionOfAnUnknownWord_IsNotOk()
    {
        // Act
        var result = Eval("bool('maybe')");

        // Assert
        Assert.That(result.Ok, Is.False);
    }

    // ---------------------------------------------------------------- variables

    [Test]
    public void Evaluate_ABracketedVariableWithAScope_ReadsThatScope()
    {
        // Act
        var result = Eval("[profile:mode]");

        // Assert
        Assert.That(result.Value.Text, Is.EqualTo(NightMode));
    }

    [Test]
    public void Evaluate_AControlScopedNumber_CanBeAddedTo()
    {
        // Act
        var result = Eval("[control:taps] + 1");

        // Assert
        Assert.That(result.Value.Number, Is.EqualTo(4));
    }

    [TestCase("len([nothing])", 0)]
    public void Evaluate_AnUnsetVariable_HasNoText(string expression, double expected)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Value.Number, Is.EqualTo(expected));
    }

    [Test]
    public void Evaluate_AnUnsetVariable_EqualsTheEmptyText()
    {
        // Act
        var result = Eval("[nothing] == ''");

        // Assert
        Assert.That(result.Value.Bool, Is.True);
    }

    // ---------------------------------------------------------------- time

    private static readonly DateTime Sunday = new(2026, 11, 22, 17, 45, 0);

    [TestCase("hour()", 17)]
    [TestCase("minute()", 45)]
    [TestCase("weekday()", 7)]
    public void Evaluate_ATimeFunction_ReadsTheClockAndNumbersSundayAsSeven(string expression, double expected)
    {
        // Act
        var result = Eval(expression, Sunday);

        // Assert
        Assert.That(result.Value.Number, Is.EqualTo(expected));
    }

    [Test]
    public void Evaluate_WeekdayOnAMonday_IsOne()
    {
        // Act
        var result = Eval("weekday()", Sunday.AddDays(1));

        // Assert
        Assert.That(result.Value.Number, Is.EqualTo(1));
    }

    [Test]
    public void Evaluate_AWorkingHoursCheck_IsTrueDuringTheDay()
    {
        // Act
        var result = Eval("hour() >= 9 && hour() < 18", Sunday);

        // Assert
        Assert.That(result.Value.Bool, Is.True);
    }

    // ---------------------------------------------------------------- bad input

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
    public void Evaluate_ABadExpression_IsAMessageNeverAnException(string expression)
    {
        // Act
        var result = Eval(expression);

        // Assert
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Evaluate_AnOverlongExpression_IsRefusedBeforeItIsParsed()
    {
        // Act
        var result = Eval(new string('1', WorkflowExpression.MaxLength + 1));

        // Assert
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("longer than"));
    }

    [Test]
    public void Evaluate_AFunctionThatIsNotListed_IsRefusedAndNamed()
    {
        // Act
        var result = Eval("system('rm')");

        // Assert
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Error, Does.Contain("system"));
    }

    // ---------------------------------------------------------------- stored values

    [TestCase("true", WorkflowValueKind.Bool)]
    [TestCase("FALSE", WorkflowValueKind.Bool)]
    [TestCase("12", WorkflowValueKind.Number)]
    [TestCase("-3.5", WorkflowValueKind.Number)]
    [TestCase("1e3", WorkflowValueKind.Number)]
    [TestCase("12,5", WorkflowValueKind.Text)]
    [TestCase("", WorkflowValueKind.Text)]
    [TestCase("NaN", WorkflowValueKind.Text)]
    [TestCase("hello", WorkflowValueKind.Text)]
    public void Parse_StoredText_IsReadBackWithItsType(string stored, WorkflowValueKind expected)
    {
        // Act
        var kind = WorkflowValue.Parse(stored).Kind;

        // Assert
        Assert.That(kind, Is.EqualTo(expected));
    }

    [TestCase("true", true)]
    [TestCase("1", true)]
    [TestCase("0", false)]
    [TestCase("", false)]
    [TestCase("false", false)]
    [TestCase("something", true)]
    public void IsTrue_AStoredValue_IsWhatAnIfNodeMakesOfIt(string stored, bool expected)
    {
        // Act
        var isTrue = WorkflowValue.Parse(stored).IsTrue;

        // Assert
        Assert.That(isTrue, Is.EqualTo(expected));
    }
}
