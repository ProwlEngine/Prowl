namespace Prowl.Editor.Utilities.SemVersion.Parser;

public class Range
{
    private readonly string _range;
    private List<List<RangeCondition>> _orGroups = [];

    public Range(string range)
    {
        _range = range.Trim();
        ParseRange();
    }

    private void ParseRange()
    {
        if (string.IsNullOrWhiteSpace(_range))
        {
            throw new ArgumentException("Range cannot be empty");
        }

        List<string> tokens = TokenizeRange();
        var andGroup = new List<RangeCondition>();
        var stack = new Stack<(List<List<RangeCondition>> orGroups, List<RangeCondition> currentAnd)>();

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];

            switch (token)
            {
                case "(":
                    stack.Push((_orGroups, andGroup));
                    _orGroups = [];
                    andGroup = [];
                    continue;

                case ")":
                    if (andGroup.Count > 0)
                    {
                        _orGroups.Add(andGroup);
                    }
                    var subRange = new Range(_orGroups);
                    (List<List<RangeCondition>> parentOrGroups, List<RangeCondition> parentAndGroup) = stack.Pop();
                    _orGroups = parentOrGroups;
                    andGroup = parentAndGroup;
                    andGroup.Add(new RangeCondition { SubRange = subRange });
                    continue;

                case "||":
                    if (andGroup.Count > 0)
                    {
                        _orGroups.Add(andGroup);
                        andGroup = [];
                    }
                    continue;

                case "&&":
                    continue;
            }

            if (IsOperator(token))
            {
                if (i + 1 >= tokens.Count)
                    throw new ArgumentException($"Operator {token} must be followed by a version");

                SemanticVersion version = ParseVersion(tokens[i + 1]);
                andGroup.Add(new RangeCondition { Operator = token, Version = version });
                i++; // Skip version token
            }
            else if (IsVersion(token))
            {
                // Implicit equality
                SemanticVersion version = ParseVersion(token);
                andGroup.Add(new RangeCondition { Operator = "==", Version = version });
            }
            else
            {
                throw new ArgumentException($"Invalid token: {token}");
            }
        }

        if (andGroup.Count > 0)
        {
            _orGroups.Add(andGroup);
        }
    }

    // Constructor for subranges created from parentheses
    private Range(List<List<RangeCondition>> orGroups)
    {
        _orGroups = orGroups;
    }

    private List<string> TokenizeRange()
    {
        var tokens = new List<string>();
        string currentToken = string.Empty;

        for (int i = 0; i < _range.Length; i++)
        {
            char c = _range[i];

            if (char.IsWhiteSpace(c))
            {
                if (!string.IsNullOrEmpty(currentToken))
                {
                    tokens.Add(currentToken);
                    currentToken = string.Empty;
                }
                continue;
            }

            // Handle parentheses
            if (c == '(' || c == ')')
            {
                if (!string.IsNullOrEmpty(currentToken))
                {
                    tokens.Add(currentToken);
                    currentToken = string.Empty;
                }
                tokens.Add(c.ToString());
                continue;
            }

            // Handle operators
            if (">=<=!|&".Contains(c))
            {
                if (!string.IsNullOrEmpty(currentToken))
                {
                    tokens.Add(currentToken);
                    currentToken = string.Empty;
                }

                // Check for double-character operators
                if (i + 1 < _range.Length && ">=<=!|&".Contains(_range[i + 1]))
                {
                    tokens.Add(_range.Substring(i, 2));
                    i++; // Skip next character
                }
                else
                {
                    tokens.Add(c.ToString());
                }
                continue;
            }

            currentToken += c;
        }

        if (!string.IsNullOrEmpty(currentToken))
        {
            tokens.Add(currentToken);
        }

        return tokens;
    }

    private static bool IsOperator(string token)
    {
        return token is ">" or ">=" or "<" or "<=" or "==" or "!=";
    }

    private static bool IsVersion(string token)
    {
        return char.IsDigit(token[0]) || token == "*";
    }

    private static SemanticVersion ParseVersion(string version)
    {
        // Handle wildcard versions
        if (version == "*")
        {
            return SemanticVersion.Parse("0.0.0");
        }

        return SemanticVersion.Parse(version);
    }

    public bool IsSatisfied(SemanticVersion version)
    {
        // Return true for empty range
        if (_orGroups.Count == 0)
            return true;

        // Any OR group must be satisfied
        return _orGroups.Any(andGroup =>
            // All conditions in AND group must be satisfied
            andGroup.All(condition => condition.IsSatisfied(version)));
    }

    private class RangeCondition
    {
        public string Operator { get; set; }
        public SemanticVersion Version { get; set; }
        public Range SubRange { get; set; }

        public bool IsSatisfied(SemanticVersion version)
        {
            // Handle subranges (from parentheses)
            if (SubRange != null)
            {
                return SubRange.IsSatisfied(version);
            }

            int comparison = version.CompareTo(Version);

            return Operator switch
            {
                ">" => comparison > 0,
                ">=" => comparison >= 0,
                "<" => comparison < 0,
                "<=" => comparison <= 0,
                "==" => comparison == 0,
                "!=" => comparison != 0,
                _ => throw new ArgumentException($"Unsupported operator: {Operator}")
            };
        }
    }

    public override string ToString()
    {
        return _range;
    }
}
