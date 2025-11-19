using System.Collections;

namespace Aspect;

/// <summary>
/// Represents the arguments passed to a method.
/// Allows indexed access and modification of arguments.
/// </summary>
public class Arguments : IEnumerable<object?>
{
    private readonly object?[] _arguments;

    /// <summary>
    /// Initializes a new instance of the Arguments class.
    /// </summary>
    /// <param name="arguments">The array of arguments</param>
    public Arguments(object?[] arguments)
    {
        _arguments = arguments ?? Array.Empty<object?>();
    }

    /// <summary>
    /// Gets or sets the argument at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the argument</param>
    /// <returns>The argument value</returns>
    public object? this[int index]
    {
        get => _arguments[index];
        set => _arguments[index] = value;
    }

    /// <summary>
    /// Gets the number of arguments.
    /// </summary>
    public int Count => _arguments.Length;

    /// <summary>
    /// Gets the argument at the specified index with type safety.
    /// </summary>
    /// <typeparam name="T">The expected type of the argument</typeparam>
    /// <param name="index">The zero-based index of the argument</param>
    /// <returns>The argument value cast to the specified type</returns>
    public T GetArgument<T>(int index)
    {
        return (T)_arguments[index]!;
    }

    /// <summary>
    /// Sets the argument at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the argument</param>
    /// <param name="value">The new value</param>
    public void SetArgument(int index, object? value)
    {
        _arguments[index] = value;
    }

    /// <summary>
    /// Converts the arguments to an array.
    /// </summary>
    /// <returns>An array containing all arguments</returns>
    public object?[] ToArray()
    {
        return _arguments.ToArray();
    }

    public IEnumerator<object?> GetEnumerator()
    {
        return ((IEnumerable<object?>)_arguments).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _arguments.GetEnumerator();
    }
}
