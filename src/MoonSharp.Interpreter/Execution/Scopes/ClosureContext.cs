using System.Collections.Generic;

namespace MoonSharp.Interpreter.Execution
{
	/// <summary>
	/// The scope of a closure (container of upvalues)
	/// </summary>
	internal class ClosureContext : List<DynValue>
	{
		// The upvalue symbols of the function prototype, shared by every closure made from it. Names are
		// only needed by the debugger and the debug library, so they are built on first use.
		readonly SymbolRef[] m_SymbolRefs;
		string[] m_Symbols;

		/// <summary>
		/// Gets the symbols.
		/// </summary>
		public string[] Symbols
		{
			get
			{
				if (m_Symbols == null)
				{
					string[] names = new string[m_SymbolRefs.Length];

					for (int i = 0; i < names.Length; i++)
						names[i] = m_SymbolRefs[i].i_Name;

					m_Symbols = names;
				}

				return m_Symbols;
			}
		}

		internal ClosureContext(SymbolRef[] symbols, IEnumerable<DynValue> values)
			: this(symbols)
		{
			this.AddRange(values);
		}

		/// <summary>
		/// Creates an empty context sized for the symbols; the caller adds one value per symbol.
		/// </summary>
		internal ClosureContext(SymbolRef[] symbols)
			: base(symbols.Length)
		{
			m_SymbolRefs = symbols;
		}

		internal ClosureContext()
		{
			m_Symbols = new string[0];
		}

	}
}
