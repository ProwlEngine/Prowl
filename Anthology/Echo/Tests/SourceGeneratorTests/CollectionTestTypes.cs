using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

[GenerateSerializer]
public partial class ObjectWithList
{
    public List<int> Numbers = new();
    public List<string?> Strings = new();
}

[GenerateSerializer]
public partial class ObjectWithArray
{
    public int[] Numbers = Array.Empty<int>();
    public string?[] Strings = Array.Empty<string>();
}

[GenerateSerializer]
public partial class ObjectWithDictionary
{
    public Dictionary<string, int> StringToInt = new();
    public Dictionary<int, string?> IntToString = new();
}

[GenerateSerializer]
public partial class ObjectWithHashSet
{
    public HashSet<int> Numbers = new();
    public HashSet<string?> Strings = new();
}

[GenerateSerializer]
public partial class ObjectWithQueue
{
    public Queue<int> Numbers = new();
    public Queue<string?> Strings = new();
}

[GenerateSerializer]
public partial class ObjectWithStack
{
    public Stack<int> Numbers = new();
    public Stack<string?> Strings = new();
}

[GenerateSerializer]
public partial class ObjectWithLinkedList
{
    public LinkedList<int> Numbers = new();
    public LinkedList<string?> Strings = new();
}

[GenerateSerializer]
public partial class ObjectWithNestedCollections
{
    public List<List<int>> NestedLists = new();
    public Dictionary<string, List<int>> DictOfLists = new();
    public List<Dictionary<int, string>> ListOfDicts = new();
}

[GenerateSerializer]
public partial class ObjectWithJaggedArray
{
    public int[][] JaggedArray = Array.Empty<int[]>();
    public string?[][] JaggedStringArray = Array.Empty<string[]>();
}

[GenerateSerializer]
public partial class ObjectWithMultidimensionalArray
{
    public int[,] TwoDArray = new int[0, 0];
    public string[,,] ThreeDArray = new string[0, 0, 0];
}

[GenerateSerializer]
public partial class ObjectWithSortedCollections
{
    public SortedSet<int> SortedNumbers = new();
    public SortedDictionary<string, int> SortedDict = new();
}

[GenerateSerializer]
public partial class ObjectWithObservableCollection
{
    public System.Collections.ObjectModel.ObservableCollection<int> Numbers = new();
    public System.Collections.ObjectModel.Collection<string> Strings = new();
}

[GenerateSerializer]
public partial class ObjectWithEmptyCollections
{
    public List<int> EmptyList = new();
    public Dictionary<string, int> EmptyDict = new();
    public HashSet<int> EmptySet = new();
}

[GenerateSerializer]
public partial class ObjectWithNullCollections
{
    public List<int>? NullableList = null;
    public Dictionary<string, int>? NullableDict = null;
    public int[]? NullableArray = null;
}
