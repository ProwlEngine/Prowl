// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.CompilerServices;

using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

// WARNING: These tests capture line numbers are so are sensitive to the addition or removal of source lines.
// If you modify this file, make sure to re-run every test as the line numbers may have changed!

public class DebugStackTraceTests
{
    [Fact]
    public void FromStackTrace_HasExpectedProperties()
    {
        DebugStackTrace? st = DebugStackTrace.FromStackTrace(new StackTrace(skipFrames: 0, fNeedFileInfo: true));
        Assert.NotNull(st);
        Assert.Equal(nameof(DebugStackTraceTests), st.TypeName);
        Assert.True(st.LineCount >= 1);
        Assert.StartsWith($"at Prowl.Editor.Test.DebugStackTraceTests.{nameof(FromStackTrace_HasExpectedProperties)}()", st.GetLine(0));
        Assert.NotNull(st.GetLine(st.LineCount - 1));
        Assert.Null(st.GetAssetSourceLocation("Z:\\NonExistentDirectory\\"));
        Assert.Equal((Path: GetSourcePath(), Line: 21, Column: 9), st.GetAssetSourceLocation(Path.GetDirectoryName(GetSourcePath())!));
        Assert.StartsWith($"   at Prowl.Editor.Test.DebugStackTraceTests.{nameof(FromStackTrace_HasExpectedProperties)}()", st.ToString());
    }

    [Fact]
    public void FromException_HasExpectedProperties()
    {
        DebugStackTrace? st = null;
        string? message = null;
        try
        {
            throw new InvalidOperationException("Test message");
        }
        catch (Exception ex)
        {
            st = DebugStackTrace.FromException(ex, out message);
        }
        Assert.Equal("System.InvalidOperationException: Test message", message);
        Assert.NotNull(st);
        Assert.Equal(nameof(DebugStackTraceTests), st.TypeName);
        Assert.True(st.LineCount >= 1);
        Assert.StartsWith($"at Prowl.Editor.Test.DebugStackTraceTests.{nameof(FromException_HasExpectedProperties)}()", st.GetLine(0));
        Assert.NotNull(st.GetLine(st.LineCount - 1));
        Assert.Null(st.GetAssetSourceLocation("Z:\\NonExistentDirectory\\"));
        Assert.Equal((Path: GetSourcePath(), Line: 39, Column: 13), st.GetAssetSourceLocation(Path.GetDirectoryName(GetSourcePath())!));
        Assert.StartsWith($"""
            System.InvalidOperationException: Test message
               at Prowl.Editor.Test.DebugStackTraceTests.{nameof(FromException_HasExpectedProperties)}()
            """, st.ToString());
    }

    [Fact]
    public void FromException_HandlesNestedExceptions()
    {
        DebugStackTrace? st = null;
        string? message = null;
        try
        {
            throw new InvalidOperationException("Test message");
        }
        catch (Exception ex)
        {
            try
            {
                WrapException(ex);
            }
            catch (Exception ex2)
            {
                st = DebugStackTrace.FromException(ex2, out message);
            }
        }
        Assert.Equal("""
            System.NotImplementedException: Wrapping
             ---> System.InvalidOperationException: Test message
            """, message);
        Assert.NotNull(st);
        Assert.Equal(nameof(DebugStackTraceTests), st.TypeName);
        Assert.True(st.LineCount >= 1);
        Assert.StartsWith($"at Prowl.Editor.Test.DebugStackTraceTests.{nameof(FromException_HandlesNestedExceptions)}()", st.GetLine(0));
        Assert.NotNull(st.GetLine(st.LineCount - 1));
        Assert.StartsWith($"""
            System.NotImplementedException: Wrapping
             ---> System.InvalidOperationException: Test message
               at Prowl.Editor.Test.DebugStackTraceTests.{nameof(FromException_HandlesNestedExceptions)}()
            """, st.ToString());
    }

    private static string GetSourcePath([CallerFilePath] string? filePath = null) => filePath!;

    private static void WrapException(Exception ex) => throw new NotImplementedException("Wrapping", ex);
}
