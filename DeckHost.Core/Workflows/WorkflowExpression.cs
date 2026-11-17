using System.Globalization;
using NCalc;

namespace DeckHost.Core.Workflows;

/// <summary>The kinds of value a workflow variable holds (it is stored as text and read back by <see cref="WorkflowValue.Parse"/>).</summary>
public enum WorkflowValueKind
{
    Text,
    Number,
    Bool,
}

/// <summary>A variable's value with its type: text, a number or true / false.</summary>
public readonly record struct WorkflowValue(WorkflowValueKind Kind, string Text, double Number, bool Bool)
{
    public static WorkflowValue OfText(string text) => new(WorkflowValueKind.Text, text, 0, false);

    public static WorkflowValue OfNumber(double number) => new(WorkflowValueKind.Number, FormatNumber(number), number, false);

    public static WorkflowValue OfBool(bool value) => new(WorkflowValueKind.Bool, value ? "true" : "false", 0, value);

    /// <summary>Reads stored text: <c>true</c> / <c>false</c> are booleans, a number in invariant form is a number, anything else is text.</summary>
    public static WorkflowValue Parse(string? text)
    {
        text ??= string.Empty;
        var trimmed = text.Trim();
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return OfBool(true);
        }

        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return OfBool(false);
        }

        return trimmed.Length > 0 && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? new WorkflowValue(WorkflowValueKind.Number, text, number, false)
            : OfText(text);
    }

    public static string FormatNumber(double number) => number.ToString("0.#########", CultureInfo.InvariantCulture);

    /// <summary>What an If node makes of the value: a boolean as it is, a number unless it is 0, text unless it is empty, "false" or "0".</summary>
    public bool IsTrue => Kind switch
    {
        WorkflowValueKind.Bool => Bool,
        WorkflowValueKind.Number => Number != 0,
        _ => Text.Trim().Length > 0 && !string.Equals(Text.Trim(), "false", StringComparison.OrdinalIgnoreCase) && Text.Trim() != "0",
    };

    public string KindName => Kind switch
    {
        WorkflowValueKind.Number => "number",
        WorkflowValueKind.Bool => "true / false",
        _ => Text.Length == 0 ? "empty" : "text",
    };
}

/// <summary>The outcome of an expression: a value, or why it has none.</summary>
public readonly record struct WorkflowExpressionResult(WorkflowValue Value, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// Evaluates the expressions of Calculate, If and Switch with a library (NCalc) behind a narrow door: only the functions
/// listed here exist, variables are read as <c>[name]</c> (<c>[profile:name]</c> and <c>[control:name]</c> for a scope),
/// numbers are invariant, and every failure comes back as a message, never as an exception.
/// </summary>
public static class WorkflowExpression
{
    public const int MaxLength = 500;

    private const ExpressionOptions Options =
        ExpressionOptions.IgnoreCaseAtBuiltInFunctions | ExpressionOptions.RoundAwayFromZero;

    /// <param name="readVariable">The text of the variable <c>name</c> as written in the brackets (empty when it isn't set).</param>
    /// <param name="clock">The time for <c>hour()</c>, <c>minute()</c> and <c>weekday()</c>.</param>
    public static WorkflowExpressionResult Evaluate(string expression, Func<string, string> readVariable, Func<DateTime>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        var text = expression?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return Fail("There is no expression.");
        }

        if (text.Length > MaxLength)
        {
            return Fail($"The expression is longer than {MaxLength} characters.");
        }

        try
        {
            var parsed = new Expression(text, CultureInfo.InvariantCulture) { Options = Options };
            Register(parsed, clock ?? (static () => DateTime.Now));

            // "5" + " taps": a number next to text joins as text (NCalc alone refuses); two numbers still add.
            parsed.EvaluateBinary += static args =>
            {
                if (args.BinaryExpression.Type != BinaryExpressionType.Plus)
                {
                    return;
                }

                var left = args.LeftValue();
                var right = args.RightValue();
                if (left is string || right is string)
                {
                    args.Result = AsText(left) + AsText(right);
                }
            };
            foreach (var name in parsed.GetParameterNames())
            {
                parsed.Parameters[name] = ToObject(WorkflowValue.Parse(readVariable(name)));
            }

            return FromObject(parsed.Evaluate());
        }
        catch (Exception ex)
        {
            return Fail(Describe(ex));
        }
    }

    private static WorkflowExpressionResult Fail(string message) => new(default, message);

    private static object ToObject(WorkflowValue value) => value.Kind switch
    {
        WorkflowValueKind.Number => value.Number,
        WorkflowValueKind.Bool => value.Bool,
        _ => value.Text,
    };

    private static WorkflowExpressionResult FromObject(object? result) => result switch
    {
        bool flag => new(WorkflowValue.OfBool(flag), null),
        string text => new(WorkflowValue.OfText(text), null),
        char character => new(WorkflowValue.OfText(character.ToString()), null),
        null => Fail("The expression has no result."),
        _ when TryDouble(result, out var number) => double.IsFinite(number)
            ? new(WorkflowValue.OfNumber(number), null)
            : Fail("The result is too large or divides by zero."),
        _ => Fail("The expression has no usable result."),
    };

    private static bool TryDouble(object value, out double number)
    {
        switch (value)
        {
            case double d:
                number = d;
                return true;
            case int or long or short or byte or float or decimal or uint or ulong or ushort or sbyte:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static string Describe(Exception ex) => ex.GetType().Name switch
    {
        "NCalcParserException" => "That isn't a valid expression.",
        "NCalcFunctionNotFoundException" => ex.Message.Split('\n')[0].Replace("Function not found. Name:", "Unknown function:", StringComparison.Ordinal),
        nameof(FormatException) or nameof(InvalidCastException) => "Text and numbers can't be combined that way (use number(…) or text(…)).",
        nameof(OverflowException) or nameof(DivideByZeroException) => "The result is too large or divides by zero.",
        _ => ex.Message.Split('\n')[0],
    };

    // ---------------------------------------------------------------- functions

    private static void Register(Expression expression, Func<DateTime> clock)
    {
        void Add(string name, int min, int max, Func<object?[], object?> body)
        {
            // NCalc matches functions we add by exact name: lower case, Capitalized and UPPER CASE all work.
            var upper = char.ToUpperInvariant(name[0]) + name[1..];
            foreach (var spelling in new[] { name, upper, name.ToUpperInvariant() })
            {
                expression.Functions[spelling] = data =>
                {
                    if (data.Count < min || data.Count > max)
                    {
                        throw new ArgumentException($"{name}() takes {(min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min} to {max}")} argument(s).");
                    }

                    var arguments = new object?[data.Count];
                    for (var i = 0; i < arguments.Length; i++)
                    {
                        arguments[i] = data.Evaluate(i);
                    }

                    return body(arguments);
                };
            }
        }

        Add("len", 1, 1, static a => (double)AsText(a[0]).Length);
        Add("upper", 1, 1, static a => AsText(a[0]).ToUpperInvariant());
        Add("lower", 1, 1, static a => AsText(a[0]).ToLowerInvariant());
        Add("trim", 1, 1, static a => AsText(a[0]).Trim());
        Add("contains", 2, 2, static a => AsText(a[0]).Contains(AsText(a[1]), StringComparison.OrdinalIgnoreCase));
        Add("startswith", 2, 2, static a => AsText(a[0]).StartsWith(AsText(a[1]), StringComparison.OrdinalIgnoreCase));
        Add("endswith", 2, 2, static a => AsText(a[0]).EndsWith(AsText(a[1]), StringComparison.OrdinalIgnoreCase));
        Add("replace", 3, 3, static a => AsText(a[1]).Length == 0 ? AsText(a[0]) : AsText(a[0]).Replace(AsText(a[1]), AsText(a[2]), StringComparison.OrdinalIgnoreCase));
        Add("substr", 2, 3, static a =>
        {
            var text = AsText(a[0]);
            var start = Math.Clamp((int)Math.Floor(AsNumber(a[1])), 0, text.Length);
            var length = a.Length == 3 ? Math.Clamp((int)Math.Floor(AsNumber(a[2])), 0, text.Length - start) : text.Length - start;
            return text.Substring(start, length);
        });
        Add("number", 1, 1, static a => a[0] is string { Length: 0 } ? 0d : AsNumber(a[0]));
        Add("text", 1, 1, static a => AsText(a[0]));
        Add("bool", 1, 1, static a => a[0] is bool flag ? flag : ParseBool(AsText(a[0])) ?? throw new FormatException("Not true or false."));
        Add("clamp", 3, 3, static a => Math.Clamp(AsNumber(a[0]), AsNumber(a[1]), AsNumber(a[2])));
        Add("round", 1, 2, static a => a.Length == 1
            ? Math.Round(AsNumber(a[0]), MidpointRounding.AwayFromZero)
            : Math.Round(AsNumber(a[0]), Math.Clamp((int)AsNumber(a[1]), 0, 15), MidpointRounding.AwayFromZero));
        Add("hour", 0, 0, _ => (double)clock().Hour);
        Add("minute", 0, 0, _ => (double)clock().Minute);

        // 1 = Monday … 7 = Sunday.
        Add("weekday", 0, 0, _ => clock().DayOfWeek == DayOfWeek.Sunday ? 7d : (double)(int)clock().DayOfWeek);
    }

    /// <summary>true / false / 1 / 0 / yes / no / on / off (any case), or null when it is none of them.</summary>
    public static bool? ParseBool(string text) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => null,
    };

    private static string AsText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        _ when TryDouble(value, out var number) => WorkflowValue.FormatNumber(number),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static double AsNumber(object? value) => value switch
    {
        string text => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? parsed
            : throw new FormatException($"'{text}' is not a number."),
        bool => throw new FormatException("A true / false value is not a number."),
        _ when value is not null && TryDouble(value, out var number) => number,
        _ => throw new FormatException("Not a number."),
    };
}
