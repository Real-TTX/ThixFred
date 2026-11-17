using System.Globalization;

namespace ThixFred.Core.Business;

/// <summary>
/// The calculator of the launcher: "3,5 * (2 + 1)^2", "sqrt(2)", "200 + 15%", "0xff", "5!". Its own small parser (no
/// scripting engine, nothing runs): + - * / ^ (also × ÷ ·), brackets, a decimal comma or point, % (of the left side for + and -,
/// otherwise /100), ! (factorial), hex 0x… and binary 0b…, constants pi π e and the functions sqrt (wurzel), sin cos tan asin acos
/// atan (radians), ln log log2 exp abs round floor ceil min max pow (arguments separated by ';' or ", ").
/// </summary>
public static class Calculator
{
    /// <summary>
    /// The value of <paramref name="text"/> when it is a calculation (a leading '=' forces it), otherwise null: "42" alone or
    /// "Word 2016" are no calculation, "0xff" and "2+2" are.
    /// </summary>
    public static double? TryEvaluate(string text)
    {
        var expression = text.Trim();
        var forced = expression.StartsWith('=');
        if (forced)
        {
            expression = expression[1..].Trim();
        }

        if (expression.Length == 0 || (!forced && !LooksLikeCalculation(expression)))
        {
            return null;
        }

        try
        {
            var parser = new Parser(expression);
            var value = parser.ParseAll();
            return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>The result as the user's culture writes it (no thousands separator: it is pasted somewhere).</summary>
    public static string Format(double value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var rounded = Math.Round(value, 10);
        if (Math.Abs(rounded) >= 1e15 || (rounded != 0 && Math.Abs(rounded) < 1e-9))
        {
            return value.ToString("G12", culture);
        }

        return rounded.ToString("0.##########", culture);
    }

    /// <summary>A whole number also in hex and binary ("0xFF · 0b11111111"), for the subtitle; null otherwise.</summary>
    public static string? OtherBases(double value)
    {
        if (value != Math.Floor(value) || Math.Abs(value) > long.MaxValue / 2 || value < 0)
        {
            return null;
        }

        var whole = (long)value;
        return $"0x{whole:X} · 0b{Convert.ToString(whole, 2)}";
    }

    private static bool LooksLikeCalculation(string text)
    {
        var hasDigit = false;
        var hasOperator = false;
        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character))
            {
                hasDigit = true;
            }
            else if ("+-*/^×÷·%!(".Contains(character))
            {
                hasOperator = true;
            }
            else if (char.IsLetter(character) || character is ' ' or ',' or '.' or ';' or ')' or 'π')
            {
            }
            else
            {
                return false;
            }
        }

        if (hasDigit && hasOperator)
        {
            return true;
        }

        // "0xff" / "0b1010" alone: the value in decimal
        var lower = text.ToLowerInvariant();
        return lower.Length > 2 && (lower.StartsWith("0x") || lower.StartsWith("0b")) && !lower.Contains(' ');
    }

    /// <summary>A recursive descent over the text; every error is a <see cref="FormatException"/>.</summary>
    private sealed class Parser(string text)
    {
        private int _position;

        public double ParseAll()
        {
            var value = ParseSum();
            SkipSpaces();
            if (_position < text.Length)
            {
                throw new FormatException($"Unexpected '{text[_position]}'");
            }

            return value;
        }

        private double ParseSum()
        {
            var value = ParseProduct(out _);
            while (true)
            {
                SkipSpaces();
                if (!TryTake('+') && !TryTake('-'))
                {
                    return value;
                }

                var minus = text[_position - 1] == '-';
                var right = ParseProduct(out var isPercent);
                // "200 + 15%" = 230, as on a pocket calculator
                if (isPercent)
                {
                    right *= value;
                }

                value = minus ? value - right : value + right;
            }
        }

        private double ParseProduct(out bool isPercent)
        {
            var value = ParsePower(out isPercent);
            while (true)
            {
                SkipSpaces();
                if (TryTake('*') || TryTake('×') || TryTake('·'))
                {
                    value *= ParsePower(out _);
                }
                else if (TryTake('/') || TryTake('÷'))
                {
                    var divisor = ParsePower(out _);
                    value = divisor == 0 ? throw new FormatException("Division by zero") : value / divisor;
                }
                else if (_position < text.Length && (text[_position] == '(' || char.IsLetter(text[_position]) || text[_position] == 'π'))
                {
                    value *= ParsePower(out _); // "2pi", "3(4+1)"
                }
                else
                {
                    return value;
                }

                isPercent = false;
            }
        }

        private double ParsePower(out bool isPercent)
        {
            var value = ParseUnary(out isPercent);
            SkipSpaces();
            if (TryTake('^'))
            {
                isPercent = false;
                value = Math.Pow(value, ParsePower(out _)); // right associative: 2^3^2 = 2^9
            }

            return value;
        }

        private double ParseUnary(out bool isPercent)
        {
            SkipSpaces();
            if (TryTake('-'))
            {
                return -ParseUnary(out isPercent);
            }

            if (TryTake('+'))
            {
                return ParseUnary(out isPercent);
            }

            return ParsePostfix(out isPercent);
        }

        private double ParsePostfix(out bool isPercent)
        {
            var value = ParsePrimary();
            isPercent = false;
            while (true)
            {
                SkipSpaces();
                if (TryTake('%'))
                {
                    value /= 100;
                    isPercent = true;
                }
                else if (TryTake('!'))
                {
                    value = Factorial(value);
                }
                else
                {
                    return value;
                }
            }
        }

        private double ParsePrimary()
        {
            SkipSpaces();
            if (_position >= text.Length)
            {
                throw new FormatException("Unexpected end");
            }

            var character = text[_position];
            if (TryTake('('))
            {
                var inner = ParseSum();
                SkipSpaces();
                // a missing ")" at the end is forgiven: "(2+3" while typing
                TryTake(')');
                return inner;
            }

            if (char.IsAsciiDigit(character) || character is '.' or ',')
            {
                return ParseNumber();
            }

            if (character == 'π')
            {
                _position++;
                return Math.PI;
            }

            if (char.IsLetter(character))
            {
                return ParseName();
            }

            throw new FormatException($"Unexpected '{character}'");
        }

        private double ParseNumber()
        {
            if (_position + 1 < text.Length && text[_position] == '0' && (text[_position + 1] is 'x' or 'X' or 'b' or 'B'))
            {
                var isHex = text[_position + 1] is 'x' or 'X';
                _position += 2;
                var start = _position;
                while (_position < text.Length && (isHex ? char.IsAsciiHexDigit(text[_position]) : text[_position] is '0' or '1'))
                {
                    _position++;
                }

                if (_position == start)
                {
                    throw new FormatException("Empty number");
                }

                return Convert.ToInt64(text[start.._position], isHex ? 16 : 2);
            }

            var digits = new System.Text.StringBuilder();
            var hasSeparator = false;
            while (_position < text.Length)
            {
                var character = text[_position];
                if (char.IsAsciiDigit(character))
                {
                    digits.Append(character);
                }
                else if ((character == '.' || (character == ',' && NextIsDigit())) && !hasSeparator)
                {
                    // "1,5" is a decimal comma; "max(1, 2)" separates (a space follows)
                    digits.Append('.');
                    hasSeparator = true;
                }
                else
                {
                    break;
                }

                _position++;
            }

            if (digits.Length == 0 || digits.ToString() == ".")
            {
                throw new FormatException("Empty number");
            }

            var number = double.Parse(digits.ToString(), CultureInfo.InvariantCulture);
            if (_position < text.Length && text[_position] is 'e' or 'E' && _position + 1 < text.Length &&
                (char.IsAsciiDigit(text[_position + 1]) || (text[_position + 1] is '-' or '+' && _position + 2 < text.Length && char.IsAsciiDigit(text[_position + 2]))))
            {
                _position++;
                var sign = TryTake('-') ? -1 : TryTake('+') ? 1 : 1;
                var start = _position;
                while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                {
                    _position++;
                }

                number *= Math.Pow(10, sign * int.Parse(text[start.._position], CultureInfo.InvariantCulture));
            }

            return number;
        }

        private bool NextIsDigit() => _position + 1 < text.Length && char.IsAsciiDigit(text[_position + 1]);

        private double ParseName()
        {
            var start = _position;
            while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] == '_'))
            {
                _position++;
            }

            var name = text[start.._position].ToLowerInvariant();
            switch (name)
            {
                case "pi":
                    return Math.PI;
                case "e":
                    return Math.E;
            }

            SkipSpaces();
            if (!TryTake('('))
            {
                throw new FormatException($"Unknown name {name}");
            }

            var arguments = new List<double> { ParseSum() };
            while (true)
            {
                SkipSpaces();
                if (TryTake(';') || TryTake(','))
                {
                    arguments.Add(ParseSum());
                    continue;
                }

                TryTake(')');
                break;
            }

            return Call(name, arguments);
        }

        private static double Call(string name, List<double> arguments)
        {
            var x = arguments[0];
            double Second() => arguments.Count > 1 ? arguments[1] : throw new FormatException($"{name} needs two values");
            return name switch
            {
                "sqrt" or "wurzel" => x < 0 ? throw new FormatException("Negative root") : Math.Sqrt(x),
                "cbrt" => Math.Cbrt(x),
                "sin" => Math.Sin(x),
                "cos" => Math.Cos(x),
                "tan" => Math.Tan(x),
                "asin" => Math.Asin(x),
                "acos" => Math.Acos(x),
                "atan" => Math.Atan(x),
                "ln" => Math.Log(x),
                "log" => arguments.Count > 1 ? Math.Log(x, Second()) : Math.Log10(x),
                "log2" => Math.Log2(x),
                "exp" => Math.Exp(x),
                "abs" => Math.Abs(x),
                "round" => arguments.Count > 1 ? Math.Round(x, (int)Second(), MidpointRounding.AwayFromZero) : Math.Round(x, MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(x),
                "ceil" => Math.Ceiling(x),
                "min" => arguments.Min(),
                "max" => arguments.Max(),
                "pow" => Math.Pow(x, Second()),
                _ => throw new FormatException($"Unknown function {name}"),
            };
        }

        private static double Factorial(double value)
        {
            if (value < 0 || value != Math.Floor(value) || value > 170)
            {
                throw new FormatException("Factorial of a negative, broken or too large number");
            }

            var result = 1.0;
            for (var factor = 2; factor <= (int)value; factor++)
            {
                result *= factor;
            }

            return result;
        }

        private void SkipSpaces()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private bool TryTake(char character)
        {
            if (_position < text.Length && text[_position] == character)
            {
                _position++;
                return true;
            }

            return false;
        }
    }
}
