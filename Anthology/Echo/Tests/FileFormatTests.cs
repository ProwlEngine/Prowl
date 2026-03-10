namespace Prowl.Echo.Test
{
    public class FileFormatTests
    {
        /// <summary>
        /// Creates a compound with a variety of types for roundtrip testing.
        /// </summary>
        private static EchoObject CreateTestCompound()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("int", new EchoObject(42));
            compound.Add("string", new EchoObject("hello world"));
            compound.Add("bool", new EchoObject(true));
            compound.Add("null", new EchoObject(EchoType.Null, null));
            compound.Add("double", new EchoObject(3.14159));
            compound.Add("long", new EchoObject(9876543210L));

            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject(1));
            list.ListAdd(new EchoObject(2));
            list.ListAdd(new EchoObject(3));
            compound.Add("list", list);

            var nested = EchoObject.NewCompound();
            nested.Add("key", new EchoObject("value"));
            nested.Add("number", new EchoObject(99));
            compound.Add("nested", nested);

            return compound;
        }

        #region IFileFormat Interface Tests

        [Theory]
        [InlineData(typeof(EchoTextFormat))]
        [InlineData(typeof(EchoBinaryFormat))]
        [InlineData(typeof(JsonFileFormat))]
        [InlineData(typeof(BsonFileFormat))]
        [InlineData(typeof(YamlFileFormat))]
        [InlineData(typeof(XmlFileFormat))]
        public void AllFormats_RoundtripCompound(Type formatType)
        {
            var format = (IFileFormat)Activator.CreateInstance(formatType)!;
            var original = CreateTestCompound();

            using var stream = new MemoryStream();
            format.WriteTo(original, stream);
            stream.Position = 0;
            var result = format.ReadFrom(stream);

            Assert.Equal(EchoType.Compound, result.TagType);
            Assert.Equal("hello world", result.Get("string").StringValue);
            Assert.True(result.Get("bool").BoolValue);
            Assert.Equal(EchoType.Null, result.Get("null").TagType);
            Assert.Equal(3, result.Get("list").Count);
            Assert.Equal("value", result.Get("nested").Get("key").StringValue);
        }

        [Theory]
        [InlineData(typeof(EchoTextFormat))]
        [InlineData(typeof(EchoBinaryFormat))]
        [InlineData(typeof(JsonFileFormat))]
        [InlineData(typeof(BsonFileFormat))]
        [InlineData(typeof(YamlFileFormat))]
        [InlineData(typeof(XmlFileFormat))]
        public void AllFormats_FileIO(Type formatType)
        {
            var format = (IFileFormat)Activator.CreateInstance(formatType)!;
            var original = CreateTestCompound();
            var tempFile = Path.GetTempFileName();

            try
            {
                format.WriteToFile(original, tempFile);
                var result = format.ReadFromFile(tempFile);

                Assert.Equal(EchoType.Compound, result.TagType);
                Assert.Equal("hello world", result.Get("string").StringValue);
                Assert.Equal(3, result.Get("list").Count);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        #endregion

        #region EchoText / EchoBinary Format Wrappers

        [Fact]
        public void EchoTextFormat_ExactRoundtrip()
        {
            var original = EchoObject.NewCompound();
            original.Add("byte", new EchoObject((byte)255));
            original.Add("sbyte", new EchoObject((sbyte)-128));
            original.Add("short", new EchoObject((short)1000));
            original.Add("float", new EchoObject(3.14f));

            var text = EchoTextFormat.Instance.WriteToString(original);
            var result = EchoTextFormat.Instance.ReadFromString(text);

            Assert.Equal((byte)255, result.Get("byte").ByteValue);
            Assert.Equal((sbyte)-128, result.Get("sbyte").sByteValue);
            Assert.Equal((short)1000, result.Get("short").ShortValue);
            Assert.Equal(3.14f, result.Get("float").FloatValue);
        }

        [Fact]
        public void EchoBinaryFormat_ExactRoundtrip()
        {
            var original = EchoObject.NewCompound();
            original.Add("byte", new EchoObject((byte)255));
            original.Add("ushort", new EchoObject((ushort)60000));
            original.Add("decimal", new EchoObject(123.456m));
            original.Add("bytes", new EchoObject(new byte[] { 1, 2, 3 }));

            var bytes = EchoBinaryFormat.Instance.WriteToBytes(original);
            var result = EchoBinaryFormat.Instance.ReadFromBytes(bytes);

            Assert.Equal((byte)255, result.Get("byte").ByteValue);
            Assert.Equal((ushort)60000, result.Get("ushort").UShortValue);
            Assert.Equal(123.456m, result.Get("decimal").DecimalValue);
            Assert.Equal(new byte[] { 1, 2, 3 }, result.Get("bytes").ByteArrayValue);
        }

        #endregion

        #region JSON Tests

        [Fact]
        public void Json_RoundtripPrimitives()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("int", new EchoObject(42));
            compound.Add("long", new EchoObject(9876543210L));
            compound.Add("double", new EchoObject(3.14));
            compound.Add("string", new EchoObject("test"));
            compound.Add("bool_true", new EchoObject(true));
            compound.Add("bool_false", new EchoObject(false));
            compound.Add("null", new EchoObject(EchoType.Null, null));

            var json = compound.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(42, result.Get("int").IntValue);
            Assert.Equal(9876543210L, result.Get("long").LongValue);
            Assert.Equal(3.14, result.Get("double").DoubleValue);
            Assert.Equal("test", result.Get("string").StringValue);
            Assert.True(result.Get("bool_true").BoolValue);
            Assert.False(result.Get("bool_false").BoolValue);
            Assert.Equal(EchoType.Null, result.Get("null").TagType);
        }

        [Fact]
        public void Json_NaNAndInfinity()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("nan", new EchoObject(double.NaN));
            compound.Add("pos_inf", new EchoObject(double.PositiveInfinity));
            compound.Add("neg_inf", new EchoObject(double.NegativeInfinity));
            compound.Add("float_nan", new EchoObject(float.NaN));
            compound.Add("float_pos_inf", new EchoObject(float.PositiveInfinity));
            compound.Add("float_neg_inf", new EchoObject(float.NegativeInfinity));

            var json = compound.WriteToJson();

            // NaN/Infinity are written as strings in JSON (not valid JSON numbers)
            Assert.Contains("\"NaN\"", json);
            Assert.Contains("\"Infinity\"", json);
            Assert.Contains("\"-Infinity\"", json);

            // They roundtrip as strings since JSON has no native NaN/Infinity
            var result = EchoObject.ReadFromJson(json);
            Assert.Equal("NaN", result.Get("nan").StringValue);
            Assert.Equal("Infinity", result.Get("pos_inf").StringValue);
            Assert.Equal("-Infinity", result.Get("neg_inf").StringValue);
        }

        [Fact]
        public void Json_ByteArrayRoundtripsAsBase64String()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("data", new EchoObject(new byte[] { 0, 1, 255, 128 }));

            var json = compound.WriteToJson();
            // ByteArray is written as base64 string — on read it comes back as a string
            var result = EchoObject.ReadFromJson(json);
            Assert.Equal(EchoType.String, result.Get("data").TagType);
            Assert.Equal(Convert.ToBase64String(new byte[] { 0, 1, 255, 128 }), result.Get("data").StringValue);
        }

        [Fact]
        public void Json_CompactMode()
        {
            var format = new JsonFileFormat { Indented = false };
            var compound = EchoObject.NewCompound();
            compound.Add("a", new EchoObject(1));
            compound.Add("b", new EchoObject(2));

            var json = format.WriteToString(compound);

            // Compact: no newlines or extra spaces
            Assert.DoesNotContain("\n", json);
            Assert.Equal("{\"a\":1,\"b\":2}", json);

            var result = format.ReadFromString(json);
            Assert.Equal(1, result.Get("a").IntValue);
            Assert.Equal(2, result.Get("b").IntValue);
        }

        [Fact]
        public void Json_ReadGenericJson()
        {
            var json = """
            {
                "name": "John",
                "age": 30,
                "active": true,
                "score": 95.5,
                "address": {
                    "city": "Melbourne",
                    "zip": "3000"
                },
                "tags": ["admin", "user"],
                "metadata": null
            }
            """;

            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(EchoType.Compound, result.TagType);
            Assert.Equal("John", result.Get("name").StringValue);
            Assert.Equal(30, result.Get("age").IntValue);
            Assert.True(result.Get("active").BoolValue);
            Assert.Equal(95.5, result.Get("score").DoubleValue);
            Assert.Equal("Melbourne", result.Get("address").Get("city").StringValue);
            Assert.Equal(2, result.Get("tags").Count);
            Assert.Equal("admin", result.Get("tags").List[0].StringValue);
            Assert.Equal(EchoType.Null, result.Get("metadata").TagType);
        }

        [Fact]
        public void Json_ReadGenericArray()
        {
            var json = """[1, 2, 3, "four", true, null]""";

            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(EchoType.List, result.TagType);
            Assert.Equal(6, result.Count);
            Assert.Equal(1, result.List[0].IntValue);
            Assert.Equal("four", result.List[3].StringValue);
            Assert.True(result.List[4].BoolValue);
            Assert.Equal(EchoType.Null, result.List[5].TagType);
        }

        [Fact]
        public void Json_NestedStructures()
        {
            var inner = EchoObject.NewCompound();
            inner.Add("a", new EchoObject(1));

            var list = EchoObject.NewList();
            list.ListAdd(inner.Clone());
            list.ListAdd(new EchoObject("text"));

            var outer = EchoObject.NewCompound();
            outer.Add("items", list);

            var json = outer.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(1, result.Get("items").List[0].Get("a").IntValue);
            Assert.Equal("text", result.Get("items").List[1].StringValue);
        }

        [Fact]
        public void Json_DeeplyNested()
        {
            // Build 20 levels deep
            var current = EchoObject.NewCompound();
            current.Add("leaf", new EchoObject("deep"));

            for (int i = 0; i < 20; i++)
            {
                var parent = EchoObject.NewCompound();
                parent.Add("child", current);
                current = parent;
            }

            var json = current.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            // Navigate down 20 levels
            var node = result;
            for (int i = 0; i < 20; i++)
                node = node.Get("child");
            Assert.Equal("deep", node.Get("leaf").StringValue);
        }

        [Fact]
        public void Json_ConvenienceMethods()
        {
            var original = new EchoObject(42);
            var json = original.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(42, result.IntValue);
        }

        [Fact]
        public void Json_EmptyCompoundAndList()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("empty_list", EchoObject.NewList());
            compound.Add("empty_obj", EchoObject.NewCompound());

            var json = compound.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(0, result.Get("empty_list").Count);
            Assert.Equal(0, result.Get("empty_obj").Count);
        }

        [Fact]
        public void Json_SpecialCharactersInStrings()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("escaped", new EchoObject("line1\nline2\ttab\"quote"));
            compound.Add("unicode", new EchoObject("hello 世界"));
            compound.Add("backslash", new EchoObject("C:\\path\\to\\file"));
            compound.Add("control", new EchoObject("null\0char"));

            var json = compound.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal("line1\nline2\ttab\"quote", result.Get("escaped").StringValue);
            Assert.Equal("hello 世界", result.Get("unicode").StringValue);
            Assert.Equal("C:\\path\\to\\file", result.Get("backslash").StringValue);
            Assert.Equal("null\0char", result.Get("control").StringValue);
        }

        [Fact]
        public void Json_EmptyString()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("empty", new EchoObject(""));

            var json = compound.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(EchoType.String, result.Get("empty").TagType);
            Assert.Equal("", result.Get("empty").StringValue);
        }

        [Fact]
        public void Json_LargeNumbers()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("max_int", new EchoObject(int.MaxValue));
            compound.Add("min_int", new EchoObject(int.MinValue));
            compound.Add("max_long", new EchoObject(long.MaxValue));
            compound.Add("min_long", new EchoObject(long.MinValue));
            compound.Add("zero", new EchoObject(0));
            compound.Add("negative", new EchoObject(-1));

            var json = compound.WriteToJson();
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal(int.MaxValue, result.Get("max_int").IntValue);
            Assert.Equal(int.MinValue, result.Get("min_int").IntValue);
            Assert.Equal(long.MaxValue, result.Get("max_long").LongValue);
            Assert.Equal(long.MinValue, result.Get("min_long").LongValue);
            Assert.Equal(0, result.Get("zero").IntValue);
            Assert.Equal(-1, result.Get("negative").IntValue);
        }

        [Fact]
        public void Json_FloatWritesDecimalPoint()
        {
            // Ensure integer-valued floats/doubles still look like floats in JSON
            var compound = EchoObject.NewCompound();
            compound.Add("whole_double", new EchoObject(1.0));
            compound.Add("whole_float", new EchoObject(1.0f));

            var json = compound.WriteToJson();

            // Should contain "1.0" not just "1"
            Assert.Contains("1.0", json);
        }

        [Fact]
        public void Json_SurrogatePairRoundtrip()
        {
            // U+1F600 (😀) is encoded as surrogate pair \uD83D\uDE00 in JSON
            var json = "{\"emoji\": \"\\uD83D\\uDE00\"}";
            var result = EchoObject.ReadFromJson(json);

            Assert.Equal("\U0001F600", result.Get("emoji").StringValue);

            // Also verify roundtrip of strings containing supplementary characters
            var compound = EchoObject.NewCompound();
            compound.Add("emoji", new EchoObject("\U0001F600"));
            var written = compound.WriteToJson();
            var roundtripped = EchoObject.ReadFromJson(written);
            Assert.Equal("\U0001F600", roundtripped.Get("emoji").StringValue);
        }

        [Fact]
        public void Json_RejectsLeadingZeros()
        {
            Assert.Throws<InvalidDataException>(() => EchoObject.ReadFromJson("007"));
            Assert.Throws<InvalidDataException>(() => EchoObject.ReadFromJson("-007"));

            // But 0, 0.5, 0e1 should be fine
            Assert.Equal(0, EchoObject.ReadFromJson("0").IntValue);
            Assert.Equal(0.5, EchoObject.ReadFromJson("0.5").DoubleValue);
        }

        #endregion

        #region BSON Tests

        [Fact]
        public void Bson_RoundtripPrimitives()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("int", new EchoObject(42));
            compound.Add("long", new EchoObject(9876543210L));
            compound.Add("double", new EchoObject(3.14));
            compound.Add("string", new EchoObject("test"));
            compound.Add("bool", new EchoObject(true));
            compound.Add("null", new EchoObject(EchoType.Null, null));

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(42, result.Get("int").IntValue);
            Assert.Equal(9876543210L, result.Get("long").LongValue);
            Assert.Equal(3.14, result.Get("double").DoubleValue);
            Assert.Equal("test", result.Get("string").StringValue);
            Assert.True(result.Get("bool").BoolValue);
            Assert.Equal(EchoType.Null, result.Get("null").TagType);
        }

        [Fact]
        public void Bson_ByteArray()
        {
            var compound = EchoObject.NewCompound();
            var bytes = new byte[] { 0, 1, 2, 255, 128 };
            compound.Add("data", new EchoObject(bytes));

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(bytes, result.Get("data").ByteArrayValue);
        }

        [Fact]
        public void Bson_EmptyByteArray()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("data", new EchoObject(Array.Empty<byte>()));

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(Array.Empty<byte>(), result.Get("data").ByteArrayValue);
        }

        [Fact]
        public void Bson_NestedStructures()
        {
            var inner = EchoObject.NewCompound();
            inner.Add("x", new EchoObject(10));
            inner.Add("y", new EchoObject(20));

            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject("a"));
            list.ListAdd(new EchoObject("b"));

            var outer = EchoObject.NewCompound();
            outer.Add("point", inner);
            outer.Add("labels", list);

            var bson = outer.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(10, result.Get("point").Get("x").IntValue);
            Assert.Equal(20, result.Get("point").Get("y").IntValue);
            Assert.Equal("a", result.Get("labels").List[0].StringValue);
            Assert.Equal("b", result.Get("labels").List[1].StringValue);
        }

        [Fact]
        public void Bson_NonCompoundTopLevel()
        {
            // BSON wraps non-compound values; verify roundtrip
            var original = new EchoObject(42);
            var bson = original.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(42, result.IntValue);
        }

        [Fact]
        public void Bson_NonCompoundTopLevel_String()
        {
            var original = new EchoObject("hello");
            var bson = original.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal("hello", result.StringValue);
        }

        [Fact]
        public void Bson_NonCompoundTopLevel_List()
        {
            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject(1));
            list.ListAdd(new EchoObject(2));

            var bson = list.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(EchoType.List, result.TagType);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Bson_EmptyCollections()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("empty_list", EchoObject.NewList());
            compound.Add("empty_obj", EchoObject.NewCompound());

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(EchoType.List, result.Get("empty_list").TagType);
            Assert.Equal(0, result.Get("empty_list").Count);
            Assert.Equal(EchoType.Compound, result.Get("empty_obj").TagType);
            Assert.Equal(0, result.Get("empty_obj").Count);
        }

        [Fact]
        public void Bson_SmallIntTypesRoundtripAsInt32()
        {
            // BSON maps byte/sbyte/short/ushort to int32 — verify this works
            var compound = EchoObject.NewCompound();
            compound.Add("byte", new EchoObject((byte)200));
            compound.Add("sbyte", new EchoObject((sbyte)-50));
            compound.Add("short", new EchoObject((short)1000));
            compound.Add("ushort", new EchoObject((ushort)50000));

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            // All come back as int (BSON int32)
            Assert.Equal(EchoType.Int, result.Get("byte").TagType);
            Assert.Equal(200, result.Get("byte").IntValue);
            Assert.Equal(-50, result.Get("sbyte").IntValue);
            Assert.Equal(1000, result.Get("short").IntValue);
            Assert.Equal(50000, result.Get("ushort").IntValue);
        }

        [Fact]
        public void Bson_FloatRoundtripsAsDouble()
        {
            // BSON maps float to double
            var compound = EchoObject.NewCompound();
            compound.Add("float", new EchoObject(3.14f));

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(EchoType.Double, result.Get("float").TagType);
            Assert.Equal(3.14f, (float)result.Get("float").DoubleValue, 5);
        }

        [Fact]
        public void Bson_ConvenienceMethods()
        {
            var compound = CreateTestCompound();
            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal("hello world", result.Get("string").StringValue);
        }

        [Fact]
        public void Bson_CompoundWithDollarValueKey_NotMistaken()
        {
            // Ensure a user compound with only a key that looks like a wrapper doesn't collide
            var compound = EchoObject.NewCompound();
            compound.Add("$value", new EchoObject(42));
            compound.Add("other", new EchoObject("test"));

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            // Should remain a compound, not be unwrapped
            Assert.Equal(EchoType.Compound, result.TagType);
            Assert.Equal(42, result.Get("$value").IntValue);
            Assert.Equal("test", result.Get("other").StringValue);
        }

        [Fact]
        public void Bson_CompoundWithSingleDollarValueKey_Survives()
        {
            // Even a compound with exactly one "$value" key should roundtrip as compound
            var compound = EchoObject.NewCompound();
            compound.Add("$value", new EchoObject(42));

            var bson = compound.WriteToBson();
            var result = EchoObject.ReadFromBson(bson);

            Assert.Equal(EchoType.Compound, result.TagType);
            Assert.Equal(42, result.Get("$value").IntValue);
        }

        #endregion

        #region YAML Tests

        [Fact]
        public void Yaml_RoundtripPrimitives()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("int", new EchoObject(42));
            compound.Add("double", new EchoObject(3.14));
            compound.Add("string", new EchoObject("hello"));
            compound.Add("bool_true", new EchoObject(true));
            compound.Add("bool_false", new EchoObject(false));
            compound.Add("null", new EchoObject(EchoType.Null, null));

            var yaml = compound.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(42, result.Get("int").IntValue);
            Assert.Equal(3.14, result.Get("double").DoubleValue);
            Assert.Equal("hello", result.Get("string").StringValue);
            Assert.True(result.Get("bool_true").BoolValue);
            Assert.False(result.Get("bool_false").BoolValue);
            Assert.Equal(EchoType.Null, result.Get("null").TagType);
        }

        [Fact]
        public void Yaml_NestedStructures()
        {
            var inner = EchoObject.NewCompound();
            inner.Add("x", new EchoObject(10));
            inner.Add("y", new EchoObject(20));

            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject(1));
            list.ListAdd(new EchoObject(2));
            list.ListAdd(new EchoObject(3));

            var outer = EchoObject.NewCompound();
            outer.Add("point", inner);
            outer.Add("numbers", list);

            var yaml = outer.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(10, result.Get("point").Get("x").IntValue);
            Assert.Equal(20, result.Get("point").Get("y").IntValue);
            Assert.Equal(3, result.Get("numbers").Count);
            Assert.Equal(1, result.Get("numbers").List[0].IntValue);
        }

        [Fact]
        public void Yaml_ReadGenericYaml()
        {
            var yaml = @"
name: John
age: 30
active: true
score: 95.5
tags:
  - admin
  - user
address:
  city: Melbourne
  zip: ""3000""
";
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(EchoType.Compound, result.TagType);
            Assert.Equal("John", result.Get("name").StringValue);
            Assert.Equal(30, result.Get("age").IntValue);
            Assert.True(result.Get("active").BoolValue);
            Assert.Equal(95.5, result.Get("score").DoubleValue);
            Assert.Equal(2, result.Get("tags").Count);
            Assert.Equal("admin", result.Get("tags").List[0].StringValue);
            Assert.Equal("Melbourne", result.Get("address").Get("city").StringValue);
        }

        [Fact]
        public void Yaml_SpecialStrings()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("looks_bool", new EchoObject("true"));
            compound.Add("looks_num", new EchoObject("42"));
            compound.Add("with_colon", new EchoObject("key: value"));
            compound.Add("with_newline", new EchoObject("line1\nline2"));

            var yaml = compound.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            // Strings that look like bools/numbers should roundtrip as strings
            Assert.Equal(EchoType.String, result.Get("looks_bool").TagType);
            Assert.Equal("true", result.Get("looks_bool").StringValue);
            Assert.Equal(EchoType.String, result.Get("looks_num").TagType);
            Assert.Equal("42", result.Get("looks_num").StringValue);
            Assert.Equal("key: value", result.Get("with_colon").StringValue);
            Assert.Equal("line1\nline2", result.Get("with_newline").StringValue);
        }

        [Fact]
        public void Yaml_ByteArrayRoundtrip()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("data", new EchoObject(new byte[] { 0, 1, 255, 128 }));

            var yaml = compound.WriteToYaml();

            // Verify !!binary tag is used
            Assert.Contains("!!binary", yaml);

            var result = EchoObject.ReadFromYaml(yaml);
            Assert.Equal(EchoType.ByteArray, result.Get("data").TagType);
            Assert.Equal(new byte[] { 0, 1, 255, 128 }, result.Get("data").ByteArrayValue);
        }

        [Fact]
        public void Yaml_ListOfCompounds()
        {
            var list = EchoObject.NewList();
            for (int i = 0; i < 3; i++)
            {
                var item = EchoObject.NewCompound();
                item.Add("id", new EchoObject(i));
                item.Add("name", new EchoObject($"item{i}"));
                list.ListAdd(item);
            }

            var outer = EchoObject.NewCompound();
            outer.Add("items", list);

            var yaml = outer.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(3, result.Get("items").Count);
            Assert.Equal(0, result.Get("items").List[0].Get("id").IntValue);
            Assert.Equal("item0", result.Get("items").List[0].Get("name").StringValue);
            Assert.Equal(2, result.Get("items").List[2].Get("id").IntValue);
        }

        [Fact]
        public void Yaml_ConvenienceMethods()
        {
            var original = CreateTestCompound();
            var yaml = original.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal("hello world", result.Get("string").StringValue);
        }

        [Fact]
        public void Yaml_EmptyCollections()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("empty_list", EchoObject.NewList());
            compound.Add("empty_obj", EchoObject.NewCompound());

            var yaml = compound.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(0, result.Get("empty_list").Count);
            Assert.Equal(0, result.Get("empty_obj").Count);
        }

        [Fact]
        public void Yaml_EmptyString()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("empty", new EchoObject(""));

            var yaml = compound.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(EchoType.String, result.Get("empty").TagType);
            Assert.Equal("", result.Get("empty").StringValue);
        }

        [Fact]
        public void Yaml_NullVariants()
        {
            // Test reading various YAML null representations
            var yaml = @"
a: null
b: ~
c: Null
d: NULL
";
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(EchoType.Null, result.Get("a").TagType);
            Assert.Equal(EchoType.Null, result.Get("b").TagType);
            Assert.Equal(EchoType.Null, result.Get("c").TagType);
            Assert.Equal(EchoType.Null, result.Get("d").TagType);
        }

        [Fact]
        public void Yaml_BoolVariants()
        {
            var yaml = @"
a: true
b: True
c: TRUE
d: false
e: False
f: FALSE
";
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.True(result.Get("a").BoolValue);
            Assert.True(result.Get("b").BoolValue);
            Assert.True(result.Get("c").BoolValue);
            Assert.False(result.Get("d").BoolValue);
            Assert.False(result.Get("e").BoolValue);
            Assert.False(result.Get("f").BoolValue);
        }

        [Fact]
        public void Yaml_NaNAndInfinityRoundtrip()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("nan", new EchoObject(double.NaN));
            compound.Add("pos_inf", new EchoObject(double.PositiveInfinity));
            compound.Add("neg_inf", new EchoObject(double.NegativeInfinity));
            compound.Add("float_nan", new EchoObject(float.NaN));

            var yaml = compound.WriteToYaml();

            Assert.Contains(".nan", yaml);
            Assert.Contains(".inf", yaml);
            Assert.Contains("-.inf", yaml);

            var result = EchoObject.ReadFromYaml(yaml);
            Assert.True(double.IsNaN(result.Get("nan").DoubleValue));
            Assert.True(double.IsPositiveInfinity(result.Get("pos_inf").DoubleValue));
            Assert.True(double.IsNegativeInfinity(result.Get("neg_inf").DoubleValue));
            Assert.True(double.IsNaN(result.Get("float_nan").DoubleValue));
        }

        [Fact]
        public void Yaml_ReadNaNInfVariants()
        {
            var yaml = @"
a: .nan
b: .NaN
c: .NAN
d: .inf
e: .Inf
f: -.inf
g: -.Inf
";
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.True(double.IsNaN(result.Get("a").DoubleValue));
            Assert.True(double.IsNaN(result.Get("b").DoubleValue));
            Assert.True(double.IsNaN(result.Get("c").DoubleValue));
            Assert.True(double.IsPositiveInfinity(result.Get("d").DoubleValue));
            Assert.True(double.IsPositiveInfinity(result.Get("e").DoubleValue));
            Assert.True(double.IsNegativeInfinity(result.Get("f").DoubleValue));
            Assert.True(double.IsNegativeInfinity(result.Get("g").DoubleValue));
        }

        [Fact]
        public void Yaml_YesNoOnOffQuotedAsStrings()
        {
            // Strings that look like YAML 1.1 booleans should be quoted and roundtrip as strings
            var compound = EchoObject.NewCompound();
            compound.Add("a", new EchoObject("yes"));
            compound.Add("b", new EchoObject("no"));
            compound.Add("c", new EchoObject("on"));
            compound.Add("d", new EchoObject("off"));

            var yaml = compound.WriteToYaml();
            var result = EchoObject.ReadFromYaml(yaml);

            Assert.Equal(EchoType.String, result.Get("a").TagType);
            Assert.Equal("yes", result.Get("a").StringValue);
            Assert.Equal("no", result.Get("b").StringValue);
            Assert.Equal("on", result.Get("c").StringValue);
            Assert.Equal("off", result.Get("d").StringValue);
        }

        #endregion

        #region XML Tests

        [Fact]
        public void Xml_RoundtripAllTypes()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("byte", new EchoObject((byte)255));
            compound.Add("sbyte", new EchoObject((sbyte)-128));
            compound.Add("short", new EchoObject((short)1000));
            compound.Add("ushort", new EchoObject((ushort)60000));
            compound.Add("int", new EchoObject(42));
            compound.Add("uint", new EchoObject(42u));
            compound.Add("long", new EchoObject(9876543210L));
            compound.Add("ulong", new EchoObject(9876543210uL));
            compound.Add("float", new EchoObject(3.14f));
            compound.Add("double", new EchoObject(3.14159));
            compound.Add("decimal", new EchoObject(123.456m));
            compound.Add("string", new EchoObject("test"));
            compound.Add("bool", new EchoObject(true));
            compound.Add("null", new EchoObject(EchoType.Null, null));
            compound.Add("bytes", new EchoObject(new byte[] { 1, 2, 3 }));

            var xml = compound.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            // XML preserves exact types via type attributes
            Assert.Equal((byte)255, result.Get("byte").ByteValue);
            Assert.Equal((sbyte)-128, result.Get("sbyte").sByteValue);
            Assert.Equal((short)1000, result.Get("short").ShortValue);
            Assert.Equal((ushort)60000, result.Get("ushort").UShortValue);
            Assert.Equal(42, result.Get("int").IntValue);
            Assert.Equal(42u, result.Get("uint").UIntValue);
            Assert.Equal(9876543210L, result.Get("long").LongValue);
            Assert.Equal(9876543210uL, result.Get("ulong").ULongValue);
            Assert.Equal(3.14f, result.Get("float").FloatValue);
            Assert.Equal(3.14159, result.Get("double").DoubleValue);
            Assert.Equal(123.456m, result.Get("decimal").DecimalValue);
            Assert.Equal("test", result.Get("string").StringValue);
            Assert.True(result.Get("bool").BoolValue);
            Assert.Equal(EchoType.Null, result.Get("null").TagType);
            Assert.Equal(new byte[] { 1, 2, 3 }, result.Get("bytes").ByteArrayValue);
        }

        [Fact]
        public void Xml_NestedStructures()
        {
            var inner = EchoObject.NewCompound();
            inner.Add("a", new EchoObject(1));

            var list = EchoObject.NewList();
            list.ListAdd(new EchoObject(10));
            list.ListAdd(new EchoObject(20));

            var outer = EchoObject.NewCompound();
            outer.Add("child", inner);
            outer.Add("items", list);

            var xml = outer.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal(1, result.Get("child").Get("a").IntValue);
            Assert.Equal(2, result.Get("items").Count);
            Assert.Equal(10, result.Get("items").List[0].IntValue);
        }

        [Fact]
        public void Xml_SpecialKeyNames()
        {
            // Keys with characters invalid in XML element names
            var compound = EchoObject.NewCompound();
            compound.Add("$type", new EchoObject("MyClass"));
            compound.Add("$id", new EchoObject(1));

            var xml = compound.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal("MyClass", result.Get("$type").StringValue);
            Assert.Equal(1, result.Get("$id").IntValue);
        }

        [Fact]
        public void Xml_SpecialCharactersInStringValues()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("xml_chars", new EchoObject("<tag>&\"quote\"</tag>"));
            compound.Add("multiline", new EchoObject("line1\nline2"));

            var xml = compound.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal("<tag>&\"quote\"</tag>", result.Get("xml_chars").StringValue);
            // Note: newlines in text content survive as-is
        }

        [Fact]
        public void Xml_StringWithLeadingWhitespace()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("padded", new EchoObject("  hello  "));

            var xml = compound.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal("  hello  ", result.Get("padded").StringValue);
        }

        [Fact]
        public void Xml_EmptyCollections()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("empty_list", EchoObject.NewList());
            compound.Add("empty_obj", EchoObject.NewCompound());

            var xml = compound.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal(EchoType.List, result.Get("empty_list").TagType);
            Assert.Equal(0, result.Get("empty_list").Count);
            Assert.Equal(EchoType.Compound, result.Get("empty_obj").TagType);
            Assert.Equal(0, result.Get("empty_obj").Count);
        }

        [Fact]
        public void Xml_EmptyString()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("empty", new EchoObject(""));

            var xml = compound.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal(EchoType.String, result.Get("empty").TagType);
            Assert.Equal("", result.Get("empty").StringValue);
        }

        [Fact]
        public void Xml_ReadGenericXml()
        {
            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<config>
    <name>MyApp</name>
    <version>2</version>
    <debug>true</debug>
    <settings>
        <timeout>30</timeout>
        <retries>3</retries>
    </settings>
</config>";

            var result = EchoObject.ReadFromXml(xml);

            // Without type attributes, types are inferred
            Assert.Equal(EchoType.Compound, result.TagType);
            Assert.Equal("MyApp", result.Get("name").StringValue);
            Assert.Equal(2, result.Get("version").IntValue);
            Assert.True(result.Get("debug").BoolValue);
            Assert.Equal(30, result.Get("settings").Get("timeout").IntValue);
        }

        [Fact]
        public void Xml_ListOfCompounds()
        {
            var list = EchoObject.NewList();
            for (int i = 0; i < 3; i++)
            {
                var item = EchoObject.NewCompound();
                item.Add("id", new EchoObject(i));
                list.ListAdd(item);
            }

            var outer = EchoObject.NewCompound();
            outer.Add("items", list);

            var xml = outer.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal(3, result.Get("items").Count);
            Assert.Equal(0, result.Get("items").List[0].Get("id").IntValue);
            Assert.Equal(2, result.Get("items").List[2].Get("id").IntValue);
        }

        [Fact]
        public void Xml_ConvenienceMethods()
        {
            var original = CreateTestCompound();
            var xml = original.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal("hello world", result.Get("string").StringValue);
        }

        [Fact]
        public void Xml_KeyStartingWithDigit()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("123key", new EchoObject("value"));

            var xml = compound.WriteToXml();
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal("value", result.Get("123key").StringValue);
        }

        [Fact]
        public void Xml_CarriageReturnPreserved()
        {
            var compound = EchoObject.NewCompound();
            compound.Add("cr", new EchoObject("line1\rline2"));
            compound.Add("crlf", new EchoObject("line1\r\nline2"));

            var xml = compound.WriteToXml();

            // \r should be escaped as &#xD; to survive XML normalization
            Assert.Contains("&#xD;", xml);

            var result = EchoObject.ReadFromXml(xml);
            Assert.Equal("line1\rline2", result.Get("cr").StringValue);
            Assert.Equal("line1\r\nline2", result.Get("crlf").StringValue);
        }

        [Fact]
        public void Xml_HexNumericEntityReferences()
        {
            // Read XML with hex character references
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><echo type=\"string\">&#x41;&#x42;&#x43;</echo>";
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal("ABC", result.StringValue);
        }

        [Fact]
        public void Xml_SingleQuotedAttributes()
        {
            // XML allows both single and double quoted attribute values
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><echo type='compound'><name type='string'>test</name></echo>";
            var result = EchoObject.ReadFromXml(xml);

            Assert.Equal("test", result.Get("name").StringValue);
        }

        #endregion
    }
}
