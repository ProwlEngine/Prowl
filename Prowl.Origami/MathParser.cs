// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Prowl.OrigamiUI;

/// <summary>
/// Lightweight math expression parser for numeric fields.
/// Evaluates basic arithmetic: + - * / ^ ( )
/// Supports constants: pi, e, tau
/// Used by NumericField to allow users to type expressions like "2*3+1" or "360/16".
/// </summary>
internal static class MathParser
{
    /// <summary>
    /// Try to parse and evaluate a math expression. Returns true if successful.
    /// </summary>
    public static bool TryEvaluate(string expression, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        try
        {
            result = Evaluate(expression);
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch
        {
            return false;
        }
    }

    private static double Evaluate(string expression)
    {
        var clean = Regex.Replace(expression, @"\s+", "");
        var tokens = Tokenize(clean);
        var postfix = ShuntingYard(tokens);
        return EvaluatePostfix(postfix);
    }

    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        var matches = Regex.Matches(expression, @"(\+|-|\*|/|\^|\(|\))|(\d+(\.\d+)?)|([a-zA-Z]+)");

        for (int i = 0; i < matches.Count; i++)
        {
            string val = matches[i].Value;

            // Handle unary minus (negative numbers)
            if (val == "-" && (i == 0 || "^*/(-+".Contains(matches[i - 1].Value)))
            {
                if (i + 1 < matches.Count &&
                    double.TryParse("-" + matches[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    tokens.Add("-" + matches[++i].Value);
                }
                else
                {
                    tokens.Add("-1");
                    tokens.Add("*");
                }
            }
            else
            {
                tokens.Add(val);
            }
        }
        return tokens;
    }

    private static List<string> ShuntingYard(List<string> tokens)
    {
        var output = new List<string>();
        var ops = new Stack<string>();

        foreach (string token in tokens)
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || IsConstant(token))
            {
                output.Add(token);
            }
            else if (token == "(")
            {
                ops.Push(token);
            }
            else if (token == ")")
            {
                while (ops.Count > 0 && ops.Peek() != "(")
                    output.Add(ops.Pop());
                if (ops.Count == 0) throw new FormatException("Mismatched parentheses");
                ops.Pop();
            }
            else
            {
                while (ops.Count > 0 && Precedence(token) <= Precedence(ops.Peek()))
                    output.Add(ops.Pop());
                ops.Push(token);
            }
        }

        while (ops.Count > 0)
        {
            if (ops.Peek() == "(") throw new FormatException("Mismatched parentheses");
            output.Add(ops.Pop());
        }
        return output;
    }

    private static double EvaluatePostfix(List<string> postfix)
    {
        var stack = new Stack<double>();

        foreach (string token in postfix)
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                stack.Push(number);
            }
            else if (TryGetConstant(token, out double constVal))
            {
                stack.Push(constVal);
            }
            else
            {
                if (stack.Count < 2) throw new FormatException("Invalid expression");
                double r = stack.Pop();
                double l = stack.Pop();
                stack.Push(ApplyOp(token, l, r));
            }
        }

        if (stack.Count != 1) throw new FormatException("Invalid expression");
        return stack.Pop();
    }

    private static int Precedence(string op) => op switch
    {
        "+" or "-" => 1,
        "*" or "/" => 2,
        "^" => 3,
        _ => 0,
    };

    private static double ApplyOp(string op, double l, double r) => op switch
    {
        "+" => l + r,
        "-" => l - r,
        "*" => l * r,
        "/" => r == 0 ? double.NaN : l / r,
        "^" => Math.Pow(l, r),
        _ => throw new FormatException($"Unknown operator: {op}"),
    };

    private static bool IsConstant(string token) => token switch
    {
        "pi" or "PI" => true,
        "e" or "E" => true,
        "tau" or "TAU" => true,
        _ => false,
    };

    private static bool TryGetConstant(string token, out double value)
    {
        value = token switch
        {
            "pi" or "PI" => Math.PI,
            "e" or "E" => Math.E,
            "tau" or "TAU" => Math.Tau,
            _ => 0,
        };
        return IsConstant(token);
    }
}
