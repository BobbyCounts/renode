//
// Copyright (c) 2010-2023 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;

namespace Antmicro.Renode.Integrations.Gcov
{
    public struct Branch
    {
        public Branch(BranchFlags flags, int destinationIndex)
        {
            Flags = flags;
            DestinationIndex = destinationIndex;
        }

        public void FillRecord(Record r)
        {
            r.Push(DestinationIndex);
            r.Push((int)Flags);
        }

        public int DestinationIndex { get; }
        public BranchFlags Flags { get; }
    }
}
