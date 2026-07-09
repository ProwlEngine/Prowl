// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using Prowl.Vector;

namespace Prowl.Runtime.Utils;

/// <summary>
/// A simple math parser that can evaluate basic arithmetic expressions.
/// Supports addition, subtraction, multiplication, division, and exponentiation.
/// Also supports parentheses and variables, and applies the C operator precedence.
///
/// Notes:
/// Ignores whitespace characters.
/// Variables are case-sensitive.
/// Variables must be defined in the Variables dictionary before parsing.
/// </summary>
public static class TinyMathParser
{
    public static readonly Dictionary<string, float> Variables = [];

    public static float Parse(string expression) => EvaluatePostfix(ShuntingYard(Tokenize(Regex.Replace(expression, @"\s+", ""))));

    private static List<string> Tokenize(string expression)
    {
        List<string> tokens = [];
        MatchCollection matches = Regex.Matches(expression, @"(\+|-|\*|/|\^|\(|\))|(\d+(\.\d+)?)|([a-zA-Z]+)");
        for (int i = 0; i < matches.Count; i++)
        {
            if (matches[i].Value == "-" && (i == 0 || "^*/(-+".Contains(matches[i - 1].Value)))
            {
                if (float.TryParse("-" + matches[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) tokens.Add("-" + matches[i++ + 1].Value);
            }
            else tokens.Add(matches[i].Value);
        }
        return tokens;
    }

    private static List<string> ShuntingYard(List<string> tokens)
    {
        List<string> output = [];
        Stack<string> operatorStack = new();
        foreach (string token in tokens)
        {
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || Variables.ContainsKey(token))
                output.Add(token);
            else if (token == "(")
                operatorStack.Push(token);
            else if (token == ")")
            {
                while (operatorStack.Count > 0 && operatorStack.Peek() != "(")
                    output.Add(operatorStack.Pop());
                if (operatorStack.Count == 0)
                    throw new ArgumentException("Mismatched parentheses");
                operatorStack.Pop();
            }
            else
            {
                while (operatorStack.Count > 0 && GetPrecedence(token) <= GetPrecedence(operatorStack.Peek()))
                    output.Add(operatorStack.Pop());
                operatorStack.Push(token);
            }
        }
        while (operatorStack.Count > 0)
        {
            if (operatorStack.Peek() == "(")
                throw new ArgumentException("Mismatched parentheses");
            output.Add(operatorStack.Pop());
        }
        return output;
    }

    private static float EvaluatePostfix(List<string> postfix)
    {
        Stack<float> stack = new();
        foreach (string token in postfix)
        {
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                stack.Push(number);
            else if (Variables.TryGetValue(token, out float variableValue))
                stack.Push(variableValue);
            else
            {
                if (stack.Count < 2)
                    throw new ArgumentException("Invalid expression");
                float result = ApplyOperator(token, stack.Pop(), stack.Pop());
                stack.Push(result);
            }
        }
        if (stack.Count != 1)
            throw new ArgumentException("Invalid expression");
        return stack.Pop();
    }

    private static int GetPrecedence(string op) => op switch { "+" or "-" => 1, "*" or "/" => 2, "^" => 3, _ => 0 };

    private static float ApplyOperator(string op, float r, float l) => op switch
    {
        "+" => l + r,
        "-" => l - r,
        "*" => l * r,
        "/" => l / r,
        "^" => Maths.Pow(l, r),
        _ => throw new ArgumentException($"Invalid operator: {op}"),
    };
}
