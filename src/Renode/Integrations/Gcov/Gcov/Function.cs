//
// Copyright (c) 2010-2023 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System.Linq;
using System.Collections.Generic;

using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Integrations.Gcov
{
    public class Function : IGcdaWriter, IGcnoWriter
    {
        static private int nextId = 0;

        public Function(FunctionExecution graph, DWARF.DWARFReader dwarf)
        {
            this.graph = graph;
            identifier = nextId++;

            ProcessExecutedPCs(dwarf);
            GenerateBranchesInformation();
            ExpandGraph();
            ProcessGraph();

            artificial = graph.File == null || graph.File.Length == 0;
        }

        public void WriteGcno(Writer f)
        {
            if(artificial)
            {
                return;
            }

            GetGcnoFunctionHeaderRecord().Write(f);
            GetGcnoBlocksHeaderRecord().Write(f);
            WriteGcnoBlocks(f);
            WriteGcnoLines(f);
        }

        public void WriteGcda(Writer f)
        {
            if(artificial || blocksBranches.Count == 0)
            {
                return;
            }

            GetGcdaFunctionHeaderRecord().Write(f);
            GetGcdaCounterRecord().Write(f);
        }

        public override string ToString()
        {
            return $"{graph.File}:{graph.Name}:{startLine},{startColumn}-{endLine},{endColumn}";
        }

        private void ProcessExecutedPCs(DWARF.DWARFReader dwarf)
        {
            entryPc = ulong.MaxValue;
            startLine = int.MaxValue;
            startColumn = int.MaxValue;
            endLine = int.MinValue;
            endColumn = int.MinValue;

            foreach(var pc in graph.ExecutedPCs)
            {
                if(!dwarf.TryGetLineForPC(pc, out var line))
                {
                    Logger.Log(LogLevel.Warning, "Couldn't resolve line mapping for PC 0x{0:X}", pc);
                    continue;
                }

                pcToLine[pc] = line.LineNumber;

                if(pc < entryPc)
                {
                    entryPc = pc;
                    startLine = line.LineNumber;
                    startColumn = (int)line.Column;
                }

                if(endLine < line.LineNumber)
                {
                    endLine = line.LineNumber;
                    endColumn = (int)line.Column;
                }
            }
        }

        private void GenerateBranchesInformation()
        {
            entryIndex = int.MaxValue;

            if(graph.NextPCs.Count == 0)
            {
                blocksBranches.Add(entryPc, new List<Branch>());
                return;
            }

            // index 0 is reserved for entering the function
            // index 1 is reserved for leaving the function
            // blocks start from index 2
            var index = 2;
            foreach(var block in graph.NextPCs)
            {
                blockIndex[block.Key] = index;
                if(block.Key == entryPc)
                {
                    entryIndex = index;
                }

                index += 1;
            }

            foreach(var block in graph.NextPCs)
            {
                var branches = new List<Branch>();

                var possibleFallthoughBranch = block.Value.Where(x => x > block.Key).DefaultIfEmpty(ulong.MaxValue).Min();
                var singleBlock = block.Value.Count() == 1;

                foreach(var pc in block.Value)
                {
                    var destinationIndex = 1;
                    var flags = (pc == possibleFallthoughBranch)
                        ? BranchFlags.Fall
                        : BranchFlags.Tree;

                    if(!blockIndex.ContainsKey(pc))
                    {
                        flags = BranchFlags.Tree;
                    }
                    else
                    {
                        destinationIndex = blockIndex[pc];

                        if(singleBlock)
                        {
                            flags |= BranchFlags.Fall;
                        }
                    }

                    var branch = new Branch(flags, destinationIndex);
                    branches.Add(branch);
                }

                blocksBranches.Add(block.Key, branches);
            }
        }

        private void ExpandGraph()
        {
            var newBlocks = new Dictionary<ulong, List<Branch>>();
            foreach(var block in blocksBranches.Where(x => x.Value.Count > 0))
            {
                var isFallthroughBranch = (block.Value[0].Flags & BranchFlags.Fall) ==  BranchFlags.Fall;
                var isTreeBranch = (block.Value[0].Flags & BranchFlags.Tree) ==  BranchFlags.Tree;

                if(isFallthroughBranch && isTreeBranch)
                {
                    var branches = new List<Branch>();
                    // TODO: why + 1? shouldn't this be a PC?
                    newBlocks.Add(block.Key + 1, branches);

                    var branch = new Branch(BranchFlags.Fall, block.Value[0].DestinationIndex);
                    branches.Add(branch);

                    var index = blockIndex.Count + 2;
                    blockIndex[block.Key + 1] = index;

                    block.Value[0] = new Branch(block.Value[0].Flags, index);
                }
            }

            foreach(var block in newBlocks)
            {
                blocksBranches.Add(block.Key, block.Value);
            }
        }

        private void ProcessGraph()
        {
            // index 0 is reserved for entering the function
            // index 1 is reserved for leaving the function
            // blocks start from index 2
            var index = 2;
            entryIndex = index;
            foreach(var block in blocksBranches)
            {
                blockIndex[block.Key] = index;
                if(block.Key == entryPc)
                {
                    entryIndex = index;
                }

                index += 1;
            }
        }

        private Record GetGcnoFunctionHeaderRecord()
        {
            var functionHeader = new Record(GcnoTagId.Function);
            functionHeader.Push(identifier);
            functionHeader.Push(0x0); // line number checksum
            functionHeader.Push(0x0); // cfg checksum
            functionHeader.Push(graph.Name);
            functionHeader.Push(artificial ? (int)1 : (int)0);
            functionHeader.Push(graph.File);

            functionHeader.Push(startLine);
            functionHeader.Push(startColumn);
            functionHeader.Push(endLine);
            functionHeader.Push(endColumn);

            return functionHeader;
        }

        private Record GetGcnoBlocksHeaderRecord()
        {
            var blockHeader = new Record(GcnoTagId.Block);

            var numberOfBlocks = blocksBranches.Count + 1;
            var numberOfLines = blocksBranches.Where(pk => pcToLine.ContainsKey(pk.Key)).Count();

            blockHeader.Push(numberOfBlocks + numberOfLines);
            return blockHeader;
        }

        private void WriteGcnoBlocks(Writer writer)
        {
            // Writing the entry point block
            GetArcRecord(0, new List<Branch> { new Branch(BranchFlags.Fall, entryIndex) }).Write(writer);

            var index = 2;
            foreach(var block in blocksBranches)
            {
                GetArcRecord(index, block.Value).Write(writer);
                index += 1;
            }
        }

        private Record GetArcRecord(int index, List<Branch> branches)
        {
            var arc = new Record(GcnoTagId.Arc);
            arc.Push(index);
            foreach(var b in branches)
            {
                b.FillRecord(arc);
            }
            return arc;
        }

        private void WriteGcnoLines(Writer writer)
        {
            foreach(var branch in blocksBranches.Where(x => pcToLine.ContainsKey(x.Key)))
            {
                var pc = branch.Key;
                GetLineRecord(pc).Write(writer);
            }
        }

        private Record GetLineRecord(ulong pc)
        {
            var record = new Record(GcnoTagId.Line);

            record.Push(blockIndex[pc]); //index

            record.Push(0); // line number, 0 is followed by the filename
            record.Push(graph.File);

            record.Push(pcToLine[pc]); // line number

            record.Push(0); // line number
            record.Push(0); // null filename - concludes a list

            return record;
        }

        private Record GetGcdaFunctionHeaderRecord()
        {
            var functionHeader = new Record(GcdaTagId.Function);

            functionHeader.Push(identifier);
            functionHeader.Push(0); // line no checksum, must be the same as in GCNO
            functionHeader.Push(0); // cfg checksum, must be the same as in GCNO

            return functionHeader;
        }

        private Record GetGcdaCounterRecord()
        {
            var writtenBlocks = new HashSet<int>();

            var countableBlockPc = blocksBranches
                .Where(pk => pk.Value.Any(branch => (branch.Flags & (BranchFlags.Fall | BranchFlags.Tree)) == BranchFlags.Fall))
                .Select(pk => pk.Value.First(branch => (branch.Flags & BranchFlags.Fall) == BranchFlags.Fall).DestinationIndex)
                .Where(index => index != 1);

            var record = new Record(GcdaTagId.Counts);
            record.Push(graph.GetCallsCount(entryPc));
            foreach(var index in countableBlockPc)
            {
                var count = (writtenBlocks.Contains(index))
                    ? (ulong)0
                    : graph.GetCallsCount(blockIndex.First(block => block.Value == index).Key);

                record.Push(count);
                writtenBlocks.Add(index);
            }

            return record;
        }

        private int entryIndex;
        private ulong entryPc;
        private int startLine;
        private int startColumn;
        private int endLine;
        private int endColumn;

        private readonly FunctionExecution graph;
        private readonly int identifier;
        private readonly bool artificial;

        // maps PC to branches
        private readonly Dictionary<ulong, List<Branch>> blocksBranches = new Dictionary<ulong, List<Branch>>();
        // maps PC to block index
        private readonly Dictionary<ulong, int> blockIndex = new Dictionary<ulong, int>();
        // maps PC to line number
        private readonly Dictionary<ulong, int> pcToLine = new Dictionary<ulong, int>();
    }
}
