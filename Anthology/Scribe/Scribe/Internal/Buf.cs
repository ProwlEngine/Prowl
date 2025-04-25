namespace StbTrueTypeSharp
{
	public class Buf
	{
		public int cursor;
		public FakePtr<byte> data;
		public int size;

		public Buf(FakePtr<byte> p, ulong size)
		{
			data = p;
			this.size = (int)size;
			cursor = 0;
		}

		public byte stbtt__buf_get8()
		{
			if (cursor >= size)
				return 0;
			return data[cursor++];
		}

		public byte stbtt__buf_peek8()
		{
			if (cursor >= size)
				return 0;
			return data[cursor];
		}

		public void stbtt__buf_seek(int o)
		{
			cursor = o > size || o < 0 ? size : o;
		}

		public void stbtt__buf_skip(int o)
		{
			stbtt__buf_seek(cursor + o);
		}

		public uint stbtt__buf_get(int n)
		{
			var v = (uint)0;
			var i = 0;
			for (i = 0; i < n; i++)
				v = (v << 8) | stbtt__buf_get8();
			return v;
		}

		public Buf stbtt__buf_range(int o, int s)
		{
			var r = new Buf(FakePtr<byte>.Null, 0);
			if (o < 0 || s < 0 || o > size || s > size - o)
				return r;
			r.data = data + o;
			r.size = s;
			return r;
		}

		public Buf stbtt__cff_get_index()
		{
			var count = 0;
			var start = 0;
			var offsize = 0;
			start = cursor;
			count = (int)stbtt__buf_get(2);
			if (count != 0)
			{
				offsize = stbtt__buf_get8();
				stbtt__buf_skip(offsize * count);
				stbtt__buf_skip((int)(stbtt__buf_get(offsize) - 1));
			}

			return stbtt__buf_range(start, cursor - start);
		}

		public uint stbtt__cff_int()
		{
			var b0 = (int)stbtt__buf_get8();
			if (b0 >= 32 && b0 <= 246)
				return (uint)(b0 - 139);
			if (b0 >= 247 && b0 <= 250)
				return (uint)((b0 - 247) * 256 + stbtt__buf_get8() + 108);
			if (b0 >= 251 && b0 <= 254)
				return (uint)(-(b0 - 251) * 256 - stbtt__buf_get8() - 108);
			if (b0 == 28)
				return stbtt__buf_get(2);
			if (b0 == 29)
				return stbtt__buf_get(4);
			return 0;
		}

		public void stbtt__cff_skip_operand()
		{
			var v = 0;
			var b0 = (int)stbtt__buf_peek8();
			if (b0 == 30)
			{
				stbtt__buf_skip(1);
				while (cursor < size)
				{
					v = stbtt__buf_get8();
					if ((v & 0xF) == 0xF || v >> 4 == 0xF)
						break;
				}
			}
			else
			{
				stbtt__cff_int();
			}
		}

		public Buf stbtt__dict_get(int key)
		{
			stbtt__buf_seek(0);
			while (cursor < size)
			{
				var start = cursor;
				var end = 0;
				var op = 0;
				while (stbtt__buf_peek8() >= 28)
					stbtt__cff_skip_operand();
				end = cursor;
				op = stbtt__buf_get8();
				if (op == 12)
					op = stbtt__buf_get8() | 0x100;
				if (op == key)
					return stbtt__buf_range(start, end - start);
			}

			return stbtt__buf_range(0, 0);
		}

		public void stbtt__dict_get_ints(int key, int outcount, FakePtr<uint> _out_)
		{
			var i = 0;
			var operands = stbtt__dict_get(key);
			for (i = 0; i < outcount && operands.cursor < operands.size; i++)
				_out_[i] = operands.stbtt__cff_int();
		}

		public void stbtt__dict_get_ints(int key, out uint _out_)
		{
			var temp = new FakePtr<uint>(new uint[1]);

			stbtt__dict_get_ints(key, 1, temp);

			_out_ = temp[0];
		}

		public int stbtt__cff_index_count()
		{
			stbtt__buf_seek(0);
			return (int)stbtt__buf_get(2);
		}

		public Buf stbtt__cff_index_get(int i)
		{
			var count = 0;
			var offsize = 0;
			var start = 0;
			var end = 0;
			stbtt__buf_seek(0);
			count = (int)stbtt__buf_get(2);
			offsize = stbtt__buf_get8();
			stbtt__buf_skip(i * offsize);
			start = (int)stbtt__buf_get(offsize);
			end = (int)stbtt__buf_get(offsize);
			return stbtt__buf_range(2 + (count + 1) * offsize + start, end - start);
		}

		public static Buf stbtt__get_subrs(Buf cff, Buf fontdict)
		{
			var subrsoff = (uint)0;

			var private_loc = new uint[2];
			private_loc[0] = 0;
			private_loc[1] = 0;

			fontdict.stbtt__dict_get_ints(18, 2, new FakePtr<uint>(private_loc));
			if (private_loc[1] == 0 || private_loc[0] == 0)
				return new Buf(FakePtr<byte>.Null, 0);
			var pdict = cff.stbtt__buf_range((int)private_loc[1], (int)private_loc[0]);
			pdict.stbtt__dict_get_ints(19, 1, new FakePtr<uint>(subrsoff));
			if (subrsoff == 0)
				return new Buf(FakePtr<byte>.Null, 0);
			cff.stbtt__buf_seek((int)(private_loc[1] + subrsoff));
			return cff.stbtt__cff_get_index();
		}

		public Buf stbtt__get_subr(int n)
		{
			var count = stbtt__cff_index_count();
			var bias = 107;
			if (count >= 33900)
				bias = 32768;
			else if (count >= 1240)
				bias = 1131;
			n += bias;
			if (n < 0 || n >= count)
				return new Buf(FakePtr<byte>.Null, 0);
			return stbtt__cff_index_get(n);
		}
	}
}