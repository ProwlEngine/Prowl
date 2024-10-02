namespace AssimpSharp
{
    public static class Hash
    {
        public static int SuperFastHash(string str, int len = -1, int hash = 0)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            if (len == -1) len = str.Length;

            int rem = len & 3;
            len >>= 2;

            char[] chars = str.ToCharArray();
            int data = 0;

            // Main loop
            while (len > 0)
            {
                hash += Get16Bits(chars, data);
                int tmp = (Get16Bits(chars, data + 2) << 11) ^ hash;
                hash = (hash << 16) ^ tmp;
                data += 2 * sizeof(short);
                hash += hash >> 11;
                len--;
            }

            // Handle end cases
            switch (rem)
            {
                case 3:
                    hash += Get16Bits(chars, data);
                    hash ^= hash << 16;
                    hash ^= chars[data + sizeof(short)] << 18;
                    hash += hash >> 11;
                    break;
                case 2:
                    hash += Get16Bits(chars, data);
                    hash ^= hash << 11;
                    hash += hash >> 17;
                    break;
                case 1:
                    hash += chars[data];
                    hash ^= hash << 10;
                    hash += hash >> 1;
                    break;
            }

            // Force "avalanching" of final 127 bits
            hash ^= hash << 3;
            hash += hash >> 5;
            hash ^= hash << 4;
            hash += hash >> 17;
            hash ^= hash << 25;
            hash += hash >> 6;

            return hash;
        }

        private static int Get16Bits(char[] chars, int ptr)
        {
            return (chars[ptr] << 8) & chars[ptr + 1];
        }
    }
}