using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public readonly struct KeywordState
    {
        public static KeywordState Empty => new KeywordState();

        public readonly string[] Keywords;
        public readonly int Hash;

        public KeywordState(params string[] keywords)
        {
            this.Keywords = keywords;
            this.Hash = ComputeHash();
        }

        public override int GetHashCode() => Hash;

        // From https://stackoverflow.com/questions/670063/getting-hash-of-a-list-of-strings-regardless-of-order, which also provides a generic way to get an order-independent hash code
        private int ComputeHash()
        {
            int hash = 0;
            int curHash;
    
            // Stores number of occurences so far of each value.
            var valueCounts = new Dictionary<string, int>();
    
            foreach (string keyword in Keywords)
            {
                curHash = keyword.GetHashCode();
    
                if (valueCounts.TryGetValue(keyword, out int bitOffset))
                    valueCounts[keyword] = bitOffset + 1;
                else
                    valueCounts.Add(keyword, bitOffset);
    
                // The current hash code is shifted (with wrapping) one bit
                // further left on each successive recurrence of a certain
                // value to widen the distribution.
                // 37 is an arbitrary low prime number that helps the
                // algorithm to smooth out the distribution.
                hash = unchecked(hash + ((curHash << bitOffset) | (curHash >> (32 - bitOffset))) * 37);
            }
    
            return hash;
        }
    }
}
