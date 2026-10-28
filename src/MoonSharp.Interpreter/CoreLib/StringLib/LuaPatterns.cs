// The matcher is a port of the pattern-matching part of lstrlib.c from Lua 5.2.4, working on string
// indices instead of char pointers.
//
// Lua License
// -----------
// Copyright (C) 1994-2015 Lua.org, PUC-Rio.
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Text;
using MoonSharp.Interpreter.Execution.VM;

namespace MoonSharp.Interpreter.CoreLib.StringLib
{
	/// <summary>
	/// string.find, match, gmatch and gsub with Lua 5.2 semantics. A pattern item matches one UTF-16 code
	/// unit and compares it whole. Character classes (%a, %d, ...) cover code units below 256, as
	/// classified by .NET, with %p as C's ispunct (printable, not a space, not alphanumeric); other code
	/// units belong to no class. A match state with its capture arrays and output buffer is reused per
	/// thread, so a call allocates only its results.
	/// </summary>
	internal sealed class LuaPatterns
	{
		const int MaxCaptures = 32;
		// Maximum recursion depth of Match, as Lua's MAXCCALLS.
		const int MaxCalls = 200;
		const int CapUnfinished = -1;
		const int CapPosition = -2;
		const char Esc = '%';
		// Calls of Match between two reports to the interrupt handler (ScriptExecutionContext.LibraryWork).
		const int StepBatch = 1024;

		const byte Alpha = 1, Cntrl = 2, Digit = 4, Lower = 8, Punct = 16, Space = 32, Upper = 64, XDigit = 128;

		static readonly byte[] s_Classes = BuildClasses();
		static readonly char[] s_Specials = "^$*+?.([%-".ToCharArray();

		[ThreadStatic]
		static LuaPatterns t_Free;

		string m_Src;
		int m_SrcEnd;
		string m_Pat;
		int m_PatEnd;
		int m_Level;
		int m_Depth;
		int m_Steps = StepBatch;
		ScriptExecutionContext m_Context;
		readonly int[] m_CapInit = new int[MaxCaptures];
		readonly int[] m_CapLen = new int[MaxCaptures];
		readonly StringBuilder m_Buffer = new StringBuilder();

		static byte[] BuildClasses()
		{
			var classes = new byte[256];

			for (int i = 0; i < 256; i++)
			{
				char c = (char)i;
				int f = 0;
				if (char.IsLetter(c)) f |= Alpha;
				if (char.IsControl(c)) f |= Cntrl;
				if (c >= '0' && c <= '9') f |= Digit;
				if (char.IsLower(c)) f |= Lower;
				if (!char.IsControl(c) && !char.IsWhiteSpace(c) && !char.IsLetterOrDigit(c)) f |= Punct;
				if (c == ' ' || (c >= '\t' && c <= '\r')) f |= Space;
				if (char.IsUpper(c)) f |= Upper;
				if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')) f |= XDigit;
				classes[i] = (byte)f;
			}

			return classes;
		}

		// The state is taken from the per-thread slot while in use, so a callback of gsub that calls
		// the string library again gets a state of its own. A state lost to an error is not returned.
		static LuaPatterns Rent(ScriptExecutionContext executionContext, string src, string pat)
		{
			LuaPatterns ms = t_Free ?? new LuaPatterns();
			t_Free = null;
			ms.m_Context = executionContext;
			ms.m_Src = src;
			ms.m_SrcEnd = src.Length;
			ms.m_Pat = pat;
			ms.m_PatEnd = pat.Length;
			return ms;
		}

		void Release()
		{
			m_Context = null;
			m_Src = null;
			m_Pat = null;
			t_Free = this;
		}

		// ---------------------------------------------------------------------------------------
		// Library functions
		// ---------------------------------------------------------------------------------------

		internal static DynValue Find(ScriptExecutionContext executionContext, CallbackArguments args, bool find)
		{
			string name = find ? "find" : "match";
			string s = CheckString(args, 0, name);
			string p = CheckString(args, 1, name);
			int ls = s.Length;

			int init = OptInt(args, 2, name, 1);
			if (init < 0)
				init = (-init > ls) ? 0 : ls + init + 1;
			if (init < 1)
				init = 1;
			else if (init > ls + 1)
				return DynValue.Nil;

			if (find && (args.GetLuaValue(3).CastToBool() || p.IndexOfAny(s_Specials) < 0))
			{
				int at = s.IndexOf(p, init - 1, StringComparison.Ordinal);
				if (at < 0)
					return DynValue.Nil;
				return DynValue.NewTuple(DynValue.NewNumber(at + 1), DynValue.NewNumber(at + p.Length));
			}

			LuaPatterns ms = Rent(executionContext, s, p);
			bool anchor = p.Length > 0 && p[0] == '^';
			int pstart = anchor ? 1 : 0;
			int s1 = init - 1;

			do
			{
				ms.m_Level = 0;
				ms.m_Depth = MaxCalls;
				int e = ms.Match(s1, pstart);

				if (e >= 0)
				{
					DynValue result;

					if (find)
					{
						var values = new DynValue[ms.m_Level + 2];
						values[0] = DynValue.NewNumber(s1 + 1);
						values[1] = DynValue.NewNumber(e);
						for (int i = 0; i < ms.m_Level; i++)
							values[i + 2] = ms.GetCapture(i, -1, -1);
						result = DynValue.NewTuple(values);
					}
					else
					{
						result = ms.GetCaptures(s1, e);
					}

					ms.Release();
					return result;
				}
			}
			while (s1++ < ms.m_SrcEnd && !anchor);

			ms.Release();
			return DynValue.Nil;
		}

		// The iterator's state lives in its delegate, not in CallbackFunction.AdditionalData: a call through
		// pcall or another C# function runs with that function's execution context.
		sealed class GMatchState
		{
			public string Src;
			public string Pat;
			public int Pos;

			public DynValue Step(ScriptExecutionContext executionContext, CallbackArguments args)
			{
				LuaPatterns ms = Rent(executionContext, Src, Pat);

				for (int src = Pos; src <= ms.m_SrcEnd; src++)
				{
					ms.m_Level = 0;
					ms.m_Depth = MaxCalls;
					int e = ms.Match(src, 0);

					if (e >= 0)
					{
						// An empty match moves on by one position.
						Pos = (e == src) ? e + 1 : e;
						DynValue result = ms.GetCaptures(src, e);
						ms.Release();
						return result;
					}
				}

				ms.Release();
				return DynValue.Nil;
			}
		}

		internal static DynValue GMatch(CallbackArguments args)
		{
			string s = CheckString(args, 0, "gmatch");
			string p = CheckString(args, 1, "gmatch");

			var state = new GMatchState { Src = s, Pat = p };
			return DynValue.NewCallback(new CallbackFunction(state.Step, "gmatch"));
		}

		internal static DynValue GSub(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			string src = CheckString(args, 0, "gsub");
			string p = CheckString(args, 1, "gsub");
			DataType replType = args.GetLuaValue(2).Type;
			int maxN = OptInt(args, 3, "gsub", src.Length + 1);

			// Everything is read from args before the first callback runs: the arguments may be a view
			// of the VM's stack, which a callback reuses.
			string replString = null;
			DynValue repl = null;

			switch (replType)
			{
				case DataType.Number:
				case DataType.String:
					replString = CheckString(args, 2, "gsub");
					break;
				case DataType.Table:
				case DataType.Function:
				case DataType.ClrFunction:
					repl = args[2];
					break;
				default:
					throw ScriptRuntimeException.BadArgument(2, "gsub", "string/function/table expected");
			}

			LuaPatterns ms = Rent(executionContext, src, p);
			StringBuilder b = ms.m_Buffer;
			bool anchor = p.Length > 0 && p[0] == '^';
			int pstart = anchor ? 1 : 0;
			int s = 0, n = 0;
			// src[copied..s) has not been written to the buffer yet: text between matches is copied in
			// one piece, and a subject without matches is returned as it is.
			int copied = 0;

			while (n < maxN)
			{
				ms.m_Level = 0;
				ms.m_Depth = MaxCalls;
				int e = ms.Match(s, pstart);

				if (e >= 0)
				{
					n++;
					b.Append(src, copied, s - copied);
					copied = e;

					if (replString != null)
						ms.AddString(b, replString, s, e);
					else
						ms.AddValue(executionContext, b, repl, s, e);
				}

				if (e > s)
					s = e;
				else if (s < ms.m_SrcEnd)
					s++;
				else
					break;

				if (anchor)
					break;
			}

			string result;

			if (n == 0)
			{
				result = src;
			}
			else
			{
				b.Append(src, copied, ms.m_SrcEnd - copied);
				result = b.ToString();
			}

			b.Length = 0;
			if (b.Capacity > 4096)
				b.Capacity = 256;

			ms.Release();
			return DynValue.NewTuple(DynValue.NewString(result), DynValue.NewNumber(n));
		}

		// Strings and numbers are read without creating a DynValue; anything else converts or fails as before.
		static string CheckString(CallbackArguments args, int arg, string name)
		{
			LuaValue v = args.GetLuaValue(arg);
			return v.Type == DataType.String ? (string)v.Object : args[arg].CheckType(name, DataType.String, arg, TypeValidationFlags.AutoConvert).String;
		}

		static int OptInt(CallbackArguments args, int arg, string name, int defaultValue)
		{
			LuaValue v = args.GetLuaValue(arg);

			if (v.Type == DataType.Number)
				return (int)v.Number;
			if (v.Type == DataType.Nil)
				return defaultValue;

			DynValue d = args[arg].CheckType(name, DataType.Number, arg, TypeValidationFlags.AllowNil | TypeValidationFlags.AutoConvert);
			return d.IsNil() ? defaultValue : (int)d.Number;
		}

		void AddString(StringBuilder b, string news, int s, int e)
		{
			int l = news.Length;
			int i = 0;

			while (i < l)
			{
				int esc = news.IndexOf(Esc, i);

				if (esc < 0)
				{
					b.Append(news, i, l - i);
					return;
				}

				b.Append(news, i, esc - i);
				i = esc + 1;
				char c = i < l ? news[i] : '\0';

				if (c < '0' || c > '9')
				{
					if (c != Esc)
						throw new ScriptRuntimeException("invalid use of '%' in replacement string");
					b.Append(c);
				}
				else if (c == '0')
				{
					b.Append(m_Src, s, e - s);
				}
				else
				{
					int k = c - '1';

					if (k < m_Level && m_CapLen[k] >= 0)
						b.Append(m_Src, m_CapInit[k], m_CapLen[k]);
					else if (k == 0 && m_Level == 0)
						b.Append(m_Src, s, e - s);
					else
						b.Append(GetCapture(k, s, e).ToPrintString());
				}

				i++;
			}
		}

		void AddValue(ScriptExecutionContext executionContext, StringBuilder b, DynValue repl, int s, int e)
		{
			DynValue value;

			if (repl.Type == DataType.Table)
			{
				// Raw access, as before: __index is not consulted.
				value = repl.Table.Get(GetCapture(0, s, e));
			}
			else
			{
				int n = (m_Level == 0) ? 1 : m_Level;
				var captures = new DynValue[n];
				for (int i = 0; i < n; i++)
					captures[i] = GetCapture(i, s, e);

				value = executionContext.Call(repl, captures);

				if (value.Type == DataType.Tuple)
					value = value.Tuple.Length > 0 ? value.Tuple[0] : DynValue.Nil;
			}

			if (!value.CastToBool())
				b.Append(m_Src, s, e - s);
			else if (value.Type == DataType.String)
				b.Append(value.String);
			else if (value.Type == DataType.Number)
				b.Append(value.ToPrintString());
			else
				throw new ScriptRuntimeException("invalid replacement value (a {0})", value.Type.ToErrorTypeString());
		}

		// Capture i of the current match, or the whole match s..e when the pattern has no captures.
		DynValue GetCapture(int i, int s, int e)
		{
			if (i >= m_Level)
			{
				if (i == 0)
					return DynValue.NewString(m_Src.Substring(s, e - s));
				throw new ScriptRuntimeException("invalid capture index");
			}

			int l = m_CapLen[i];

			if (l == CapUnfinished)
				throw new ScriptRuntimeException("unfinished capture");
			if (l == CapPosition)
				return DynValue.NewNumber(m_CapInit[i] + 1);

			return DynValue.NewString(m_Src.Substring(m_CapInit[i], l));
		}

		DynValue GetCaptures(int s, int e)
		{
			if (m_Level <= 1)
				return GetCapture(0, s, e);

			var values = new DynValue[m_Level];
			for (int i = 0; i < m_Level; i++)
				values[i] = GetCapture(i, s, e);
			return DynValue.NewTuple(values);
		}

		// ---------------------------------------------------------------------------------------
		// Matcher. Positions are indices; -1 is no match.
		// ---------------------------------------------------------------------------------------

		int Match(int s, int p)
		{
			if (m_Depth-- == 0)
				throw new ScriptRuntimeException("pattern too complex");

			// Every match attempt and every backtracking step comes through here: count them, so a long
			// gsub or a pattern that backtracks for minutes can be interrupted.
			if (--m_Steps == 0)
			{
				m_Steps = StepBatch;
				m_Context.LibraryWork(StepBatch);
			}

			while (p != m_PatEnd)
			{
				switch (m_Pat[p])
				{
					case '(':
						if (p + 1 < m_PatEnd && m_Pat[p + 1] == ')')
							s = StartCapture(s, p + 2, CapPosition);
						else
							s = StartCapture(s, p + 1, CapUnfinished);
						goto done;

					case ')':
						s = EndCapture(s, p + 1);
						goto done;

					case '$':
						if (p + 1 == m_PatEnd)
						{
							if (s != m_SrcEnd)
								s = -1;
							goto done;
						}
						break;

					case Esc:
						if (p + 1 == m_PatEnd)
							break;

						char next = m_Pat[p + 1];

						if (next == 'b')
						{
							s = MatchBalance(s, p + 2);
							if (s < 0)
								goto done;
							p += 4;
							continue;
						}

						if (next == 'f')
						{
							p += 2;
							if (p == m_PatEnd || m_Pat[p] != '[')
								throw new ScriptRuntimeException("missing '[' after '%f' in pattern");

							int ep = ClassEnd(p);
							char previous = (s == 0) ? '\0' : m_Src[s - 1];
							char current = (s < m_SrcEnd) ? m_Src[s] : '\0';

							if (!MatchBracketClass(previous, p, ep - 1) && MatchBracketClass(current, p, ep - 1))
							{
								p = ep;
								continue;
							}

							s = -1;
							goto done;
						}

						if (next >= '0' && next <= '9')
						{
							s = MatchCapture(s, next);
							if (s < 0)
								goto done;
							p += 2;
							continue;
						}

						break;
				}

				// A single-character class with an optional suffix.
				int end = ClassEnd(p);
				char suffix = (end < m_PatEnd) ? m_Pat[end] : '\0';

				if (!SingleMatch(s, p, end))
				{
					if (suffix == '*' || suffix == '?' || suffix == '-')
					{
						p = end + 1;
						continue;
					}

					s = -1;
					goto done;
				}

				switch (suffix)
				{
					case '?':
						int res = Match(s + 1, end + 1);
						if (res >= 0)
						{
							s = res;
							goto done;
						}
						p = end + 1;
						continue;

					case '+':
						s = MaxExpand(s + 1, p, end);
						goto done;

					case '*':
						s = MaxExpand(s, p, end);
						goto done;

					case '-':
						s = MinExpand(s, p, end);
						goto done;

					default:
						s++;
						p = end;
						continue;
				}
			}

			done:
			m_Depth++;
			return s;
		}

		int ClassEnd(int p)
		{
			char c = m_Pat[p++];

			if (c == Esc)
			{
				if (p == m_PatEnd)
					throw new ScriptRuntimeException("malformed pattern (ends with '%')");
				return p + 1;
			}

			if (c == '[')
			{
				if (p < m_PatEnd && m_Pat[p] == '^')
					p++;

				// Look for a ']'. The first character is always a member, so "[]]" holds a ']'.
				do
				{
					if (p == m_PatEnd)
						throw new ScriptRuntimeException("malformed pattern (missing ']')");
					if (m_Pat[p++] == Esc && p < m_PatEnd)
						p++;
				}
				while (p == m_PatEnd || m_Pat[p] != ']');

				return p + 1;
			}

			return p;
		}

		static bool MatchClass(char c, char cl)
		{
			int mask;

			switch (cl)
			{
				case 'a': case 'A': mask = Alpha; break;
				case 'c': case 'C': mask = Cntrl; break;
				case 'd': case 'D': mask = Digit; break;
				case 'g': case 'G': mask = Alpha | Digit | Punct; break;
				case 'l': case 'L': mask = Lower; break;
				case 'p': case 'P': mask = Punct; break;
				case 's': case 'S': mask = Space; break;
				case 'u': case 'U': mask = Upper; break;
				case 'w': case 'W': mask = Alpha | Digit; break;
				case 'x': case 'X': mask = XDigit; break;
				case 'z': case 'Z': return (c == '\0') == (cl == 'z');
				default: return cl == c;
			}

			bool res = c < 256 && (s_Classes[c] & mask) != 0;
			return (cl >= 'a') ? res : !res;
		}

		// p is the '[' of the set and ec its ']'.
		bool MatchBracketClass(char c, int p, int ec)
		{
			bool sig = true;

			if (m_Pat[p + 1] == '^')
			{
				sig = false;
				p++;
			}

			while (++p < ec)
			{
				char pc = m_Pat[p];

				if (pc == Esc)
				{
					p++;
					if (MatchClass(c, m_Pat[p]))
						return sig;
				}
				else if (m_Pat[p + 1] == '-' && p + 2 < ec)
				{
					p += 2;
					if (pc <= c && c <= m_Pat[p])
						return sig;
				}
				else if (pc == c)
				{
					return sig;
				}
			}

			return !sig;
		}

		bool SingleMatch(int s, int p, int ep)
		{
			if (s >= m_SrcEnd)
				return false;

			char c = m_Src[s];

			switch (m_Pat[p])
			{
				case '.': return true;
				case Esc: return MatchClass(c, m_Pat[p + 1]);
				case '[': return MatchBracketClass(c, p, ep - 1);
				default: return m_Pat[p] == c;
			}
		}

		int MatchBalance(int s, int p)
		{
			if (p >= m_PatEnd - 1)
				throw new ScriptRuntimeException("malformed pattern (missing arguments to '%b')");

			if (s >= m_SrcEnd || m_Src[s] != m_Pat[p])
				return -1;

			char b = m_Pat[p];
			char e = m_Pat[p + 1];
			int cont = 1;

			while (++s < m_SrcEnd)
			{
				char c = m_Src[s];

				if (c == e)
				{
					if (--cont == 0)
						return s + 1;
				}
				else if (c == b)
				{
					cont++;
				}
			}

			return -1;
		}

		int MaxExpand(int s, int p, int ep)
		{
			int i = 0;
			while (SingleMatch(s + i, p, ep))
				i++;

			// Try with the most repetitions first, then one fewer each time.
			while (i >= 0)
			{
				int res = Match(s + i, ep + 1);
				if (res >= 0)
					return res;
				i--;
			}

			return -1;
		}

		int MinExpand(int s, int p, int ep)
		{
			for (;;)
			{
				int res = Match(s, ep + 1);

				if (res >= 0)
					return res;
				if (SingleMatch(s, p, ep))
					s++;
				else
					return -1;
			}
		}

		int StartCapture(int s, int p, int what)
		{
			if (m_Level >= MaxCaptures)
				throw new ScriptRuntimeException("too many captures");

			m_CapInit[m_Level] = s;
			m_CapLen[m_Level] = what;
			m_Level++;

			int res = Match(s, p);
			if (res < 0)
				m_Level--;
			return res;
		}

		int EndCapture(int s, int p)
		{
			int l = CaptureToClose();
			m_CapLen[l] = s - m_CapInit[l];

			int res = Match(s, p);
			if (res < 0)
				m_CapLen[l] = CapUnfinished;
			return res;
		}

		int CaptureToClose()
		{
			for (int level = m_Level - 1; level >= 0; level--)
				if (m_CapLen[level] == CapUnfinished)
					return level;

			throw new ScriptRuntimeException("invalid pattern capture");
		}

		int MatchCapture(int s, char digit)
		{
			int l = digit - '1';

			if (l < 0 || l >= m_Level || m_CapLen[l] == CapUnfinished)
				throw new ScriptRuntimeException("invalid capture index %" + (l + 1));

			int len = m_CapLen[l];

			// A position capture has no text and never matches.
			if (len >= 0 && m_SrcEnd - s >= len && string.CompareOrdinal(m_Src, m_CapInit[l], m_Src, s, len) == 0)
				return s + len;

			return -1;
		}
	}
}
