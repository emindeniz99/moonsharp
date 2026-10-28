using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.IO;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		const ulong DUMP_CHUNK_MAGIC = 0x1A0D234E4F4F4D1D;
		// 0x153 and 0x154 add the fused opcodes (OpCode values after IterUpd), which older readers do not
		// know. 0x154 carries source locations, 0x153 does not.
		const int DUMP_CHUNK_VERSION = 0x154;
		const int DUMP_CHUNK_VERSION_NO_LOCATIONS = 0x153;

		// The previous formats still load: they hold only opcodes that kept their values. 0x151 dumps
		// carry no source locations and load with every location at line 0.
		const int DUMP_CHUNK_VERSION_0x152 = 0x152;
		const int DUMP_CHUNK_VERSION_0x151 = 0x151;

		// Per-instruction source location markers (formats 0x152 and 0x154). A new location is
		// DUMP_SOURCEREF_NEW plus flag bits, then: line delta from the previous new location,
		// end line minus start line, start column, end column minus start column.
		const byte DUMP_SOURCEREF_NONE = 0;
		const byte DUMP_SOURCEREF_SAME = 1;
		const byte DUMP_SOURCEREF_NEW = 2;
		const byte DUMP_SOURCEREF_STEPSTOP = 4;
		const byte DUMP_SOURCEREF_NOBREAKPOINT = 8;

		internal static bool IsDumpStream(Stream stream)
		{
			if (stream.Length >= 8)
			{
				using (BinaryReader br = new BinaryReader(stream, Encoding.UTF8))
				{
					ulong magic = br.ReadUInt64();
					stream.Seek(-8, SeekOrigin.Current);
					return magic == DUMP_CHUNK_MAGIC;
				}
			}
			return false;
		}

		internal int Dump(Stream stream, int baseAddress, bool hasUpvalues, bool includeSourceLocations = true)
		{
			using (BinaryWriter bw = new BinDumpBinaryWriter(stream, Encoding.UTF8))
			{
				Dictionary<SymbolRef, int> symbolMap = new Dictionary<SymbolRef, int>();

				Instruction meta = FindMeta(ref baseAddress);

				if (meta == null)
					throw new ArgumentException("baseAddress");

				bw.Write(DUMP_CHUNK_MAGIC);
				bw.Write(includeSourceLocations ? DUMP_CHUNK_VERSION : DUMP_CHUNK_VERSION_NO_LOCATIONS);
				bw.Write(hasUpvalues);
				bw.Write(meta.NumVal);

				for (int i = 0; i <= meta.NumVal; i++)
				{
					SymbolRef[] symbolList;
					SymbolRef symbol;

					m_RootChunk.Code[baseAddress + i].GetSymbolReferences(out symbolList, out symbol);

					if (symbol != null)
						AddSymbolToMap(symbolMap, symbol);

					if (symbolList != null)
						foreach (var s in symbolList)
							AddSymbolToMap(symbolMap, s);
				}

				foreach (SymbolRef sr in symbolMap.Keys.ToArray())
				{
					if (sr.i_Env != null)
						AddSymbolToMap(symbolMap, sr.i_Env);
				}

				SymbolRef[] allSymbols = new SymbolRef[symbolMap.Count];

				foreach (KeyValuePair<SymbolRef, int> pair in symbolMap)
				{
					allSymbols[pair.Value] = pair.Key;
				}

				bw.Write(symbolMap.Count);

				foreach (SymbolRef sym in allSymbols)
					sym.WriteBinary(bw);

				foreach (SymbolRef sym in allSymbols)
					sym.WriteBinaryEnv(bw, symbolMap);

				for (int i = 0; i <= meta.NumVal; i++)
					m_RootChunk.Code[baseAddress + i].WriteBinary(bw, baseAddress, symbolMap);

				if (includeSourceLocations)
					WriteSourceRefs(bw, baseAddress, meta.NumVal);

				return meta.NumVal + baseAddress + 1;
			}
		}

		// Consecutive instructions usually share a location, so a repeat costs one byte; a new
		// location usually costs five (BinDumpBinaryWriter writes small ints in one byte).
		private void WriteSourceRefs(BinaryWriter bw, int baseAddress, int count)
		{
			SourceRef previous = null;
			int previousLine = 0;

			for (int i = 0; i <= count; i++)
			{
				SourceRef sref = m_RootChunk.Code[baseAddress + i].SourceCodeRef;

				if (sref == null)
				{
					bw.Write(DUMP_SOURCEREF_NONE);
				}
				else if (previous != null && SameLocation(previous, sref))
				{
					bw.Write(DUMP_SOURCEREF_SAME);
				}
				else
				{
					byte marker = DUMP_SOURCEREF_NEW;

					if (sref.IsStepStop)
						marker |= DUMP_SOURCEREF_STEPSTOP;

					if (sref.CannotBreakpoint)
						marker |= DUMP_SOURCEREF_NOBREAKPOINT;

					bw.Write(marker);
					bw.Write(sref.FromLine - previousLine);
					bw.Write(sref.ToLine - sref.FromLine);
					bw.Write(sref.FromChar);
					bw.Write(sref.ToChar - sref.FromChar);
					previousLine = sref.FromLine;
					previous = sref;
				}
			}
		}

		private static bool SameLocation(SourceRef a, SourceRef b)
		{
			return a.FromLine == b.FromLine && a.ToLine == b.ToLine && a.FromChar == b.FromChar && a.ToChar == b.ToChar
				&& a.IsStepStop == b.IsStepStop && a.CannotBreakpoint == b.CannotBreakpoint;
		}

		private static void ReadSourceRefs(BinaryReader br, List<Instruction> code, int baseAddress, int count, int sourceID)
		{
			SourceRef previous = null;
			int previousLine = 0;

			for (int i = 0; i <= count; i++)
			{
				byte marker = br.ReadByte();

				if (marker == DUMP_SOURCEREF_NONE)
				{
					code[baseAddress + i].SourceCodeRef = null;
				}
				else if (marker == DUMP_SOURCEREF_SAME && previous != null)
				{
					code[baseAddress + i].SourceCodeRef = previous;
				}
				else if ((marker & DUMP_SOURCEREF_NEW) != 0)
				{
					int fromLine = previousLine + br.ReadInt32();
					int toLine = fromLine + br.ReadInt32();
					int fromChar = br.ReadInt32();
					int toChar = fromChar + br.ReadInt32();

					previous = new SourceRef(sourceID, fromChar, toChar, fromLine, toLine, (marker & DUMP_SOURCEREF_STEPSTOP) != 0);

					if ((marker & DUMP_SOURCEREF_NOBREAKPOINT) != 0)
						previous.SetNoBreakPoint();

					previousLine = fromLine;

					code[baseAddress + i].SourceCodeRef = previous;
				}
				else
				{
					throw new ArgumentException("Invalid source location in dump");
				}
			}
		}

		private void AddSymbolToMap(Dictionary<SymbolRef, int> symbolMap, SymbolRef s)
		{
			if (!symbolMap.ContainsKey(s))
				symbolMap.Add(s, symbolMap.Count);
		}

		internal int Undump(Stream stream, int sourceID, Table envTable, out bool hasUpvalues)
		{
			int baseAddress = m_RootChunk.Code.Count;
			SourceRef sourceRef = new SourceRef(sourceID, 0, 0, 0, 0, false);

			using (BinaryReader br = new BinDumpBinaryReader(stream, Encoding.UTF8))
			{
				ulong headerMark = br.ReadUInt64();

				if (headerMark != DUMP_CHUNK_MAGIC)
					throw new ArgumentException("Not a MoonSharp chunk");

				int version = br.ReadInt32();

				bool hasLocations = version == DUMP_CHUNK_VERSION || version == DUMP_CHUNK_VERSION_0x152;

				if (!hasLocations && version != DUMP_CHUNK_VERSION_NO_LOCATIONS && version != DUMP_CHUNK_VERSION_0x151)
					throw new ArgumentException("Invalid version");

				hasUpvalues = br.ReadBoolean();

				int len = br.ReadInt32();

				int numSymbs = br.ReadInt32();
				SymbolRef[] allSymbs = new SymbolRef[numSymbs];

				for (int i = 0; i < numSymbs; i++)
					allSymbs[i] = SymbolRef.ReadBinary(br);

				for (int i = 0; i < numSymbs; i++)
					allSymbs[i].ReadBinaryEnv(br, allSymbs);

				for (int i = 0; i <= len; i++)
				{
					Instruction I = Instruction.ReadBinary(sourceRef, br, baseAddress, envTable, allSymbs);
					m_RootChunk.Code.Add(I);
				}

				if (hasLocations)
					ReadSourceRefs(br, m_RootChunk.Code, baseAddress, len, sourceID);

				// Code from a previous format has no fused instructions: fuse it as freshly compiled code.
				if (version == DUMP_CHUNK_VERSION_0x152 || version == DUMP_CHUNK_VERSION_0x151)
					InstructionFusion.Fuse(m_RootChunk.Code, baseAddress);

				return baseAddress;
			}
		}
	}
}
