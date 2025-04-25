using System;
using System.Collections.Generic;

namespace Scribe.Internal
{
    class KerningTable
    {
        Dictionary<uint, int> table;

        KerningTable(Dictionary<uint, int> table)
        {
            this.table = table;
        }

        public FUnit Lookup(int left, int right)
        {
            var key = (uint)left << 16 | (uint)right;
            int value;
            if (table.TryGetValue(key, out value))
                return (FUnit)value;
            return (FUnit)0;
        }

        public static KerningTable ReadKern(DataReader reader, TableRecord[] tables)
        {
            // kern table is optional
            if (!SfntTables.SeekToTable(reader, tables, FourCC.Kern))
                return null;

            // skip version
            reader.Skip(sizeof(short));

            // read each subtable and accumulate kerning values
            var tableData = new Dictionary<uint, int>();
            var subtableCount = reader.ReadUInt16BE();
            for (int i = 0; i < subtableCount; i++)
            {
                // skip version
                var currentOffset = reader.Position;
                reader.Skip(sizeof(short));

                var length = reader.ReadUInt16BE();
                var coverage = reader.ReadUInt16BE();

                // we (and Windows) only support Format 0 tables
                // only care about tables with horizontal kerning data
                var kc = (KernCoverage)coverage;
                if ((coverage & FormatMask) == 0 && (kc & KernCoverage.Horizontal) != 0 && (kc & KernCoverage.CrossStream) == 0)
                {
                    // read the number of entries; skip over the rest of the header
                    var entryCount = reader.ReadUInt16BE();
                    reader.Skip(sizeof(short) * 3);

                    var isMin = (kc & KernCoverage.Minimum) != 0;
                    var isOverride = (kc & KernCoverage.Override) != 0;

                    // read in each entry and accumulate its kerning data
                    for (int j = 0; j < entryCount; j++)
                    {
                        var left = reader.ReadUInt16BE();
                        var right = reader.ReadUInt16BE();
                        var value = reader.ReadInt16BE();

                        // look up the current value, if we have one; if not, start at zero
                        int current = 0;
                        var key = (uint)left << 16 | right;
                        tableData.TryGetValue(key, out current);

                        if (isMin)
                        {
                            if (current < value)
                                tableData[key] = value;
                        }
                        else if (isOverride)
                            tableData[key] = value;
                        else
                            tableData[key] = current + value;
                    }
                }

                // jump to the next subtable
                reader.Seek(currentOffset + length);
            }

            return new KerningTable(tableData);
        }

        const uint FormatMask = 0xFFFF0000;

        [Flags]
        enum KernCoverage
        {
            None = 0,
            Horizontal = 0x1,
            Minimum = 0x2,
            CrossStream = 0x4,
            Override = 0x8
        }
    }
}
