//
// Copyright (c) 2010-2023 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Antmicro.Renode.Core;

namespace Antmicro.Renode.Integrations.Gcov
{
    public class Parser
    {
        public static List<FunctionExecution> CompileFunctionExecutions(IEnumerable<Parser> parsers)
        {
            var graphsByName = new Dictionary<string, FunctionExecution>();

            foreach(var parser in parsers)
            {
                foreach(var execution in parser.FunctionExecutions)
                {
                    if(!graphsByName.TryGetValue(execution.Name, out var graph))
                    {
                        graph = new FunctionExecution(execution.Name, execution.File);
                        graphsByName.Add(execution.Name, graph);
                    }
                    graph.MergeWith(execution);
                }
            }

            return graphsByName.Values.ToList();
        }

        public Parser(DWARF.DWARFReader dwarf, SymbolLookup lookup)
        {
            this.lookup = lookup;
            this.dwarf = dwarf;
        }

        public void PushBlockExecution(ulong pc)
        {
            if(!lookup.TryGetSymbolByAddress(pc, out var sym))
            {
                // TODO: shouldn't it be an error?
                return;
            }

            var isFunctionStart = sym.Start == pc || sym.Start == sym.End || functionExecutionsStack.Count == 0;
            var isRecursive = functionExecutionsStack.Count > 0 && functionExecutionsStack.Peek().Name == sym.Name;
            var isOnStack = functionExecutionsStack.Any(s => s.Name == sym.Name);

            if((isFunctionStart || !isOnStack) && !isRecursive)
            {
                // Function entry
                if(!PushFunctionExecution(pc, sym.Name))
                {
                    unmappableSymbols.Add((ulong)sym.Start);
                }
            }
            else if(!unmappableSymbols.Contains((ulong)sym.Start))
            {
                // Not a function entry
                var lastFex = functionExecutionsStack.Peek();
                if(lastFex.Name != sym.Name)
                {
                    functionExecutionsStack.Pop();
                    finishedFunctionExecutions.Add(lastFex);
                }

                functionExecutionsStack.Peek().PushBlock(pc);
            }
        }

        public IEnumerable<FunctionExecution> FunctionExecutions
        {
            get
            {
                while(functionExecutionsStack.Count > 0)
                {
                    var lastFex = functionExecutionsStack.Pop();
                    finishedFunctionExecutions.Add(lastFex);
                }
                return finishedFunctionExecutions;
            }
        }

        private bool PushFunctionExecution(ulong pc, string name)
        {
            if(!dwarf.TryGetLineForPC(pc, out var line))
            {
                return false;
            }

            var functionExecution = new FunctionExecution(name, line.FilePath);
            functionExecutionsStack.Push(functionExecution);
            functionExecution.PushBlock(pc);

            return true;
        }

        private readonly Stack<FunctionExecution> functionExecutionsStack = new Stack<FunctionExecution>();
        private readonly HashSet<ulong> unmappableSymbols = new HashSet<ulong>();
        private readonly List<FunctionExecution> finishedFunctionExecutions = new List<FunctionExecution>();

        private readonly DWARF.DWARFReader dwarf;
        private readonly SymbolLookup lookup;
    }
}
