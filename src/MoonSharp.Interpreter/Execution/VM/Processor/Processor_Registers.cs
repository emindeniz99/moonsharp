using System;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	// SPIKE (branch spike/register-vm): the dispatch loop for RegisterProto code. See RegisterProto.cs.
	//
	// A register frame is an ordinary CallStackItem whose BasePointer is its first register, with the
	// arguments already in the parameter registers (ArgCount 0, the function value right below). Error
	// unwinding, PopScriptFrame and the debugger's stack walk therefore treat it like a stack frame.
	// Calls from one register function to another stay in this loop (Flags has RegisterCall); every other
	// call and every slow path leaves the frame's next pc in SavedPc and returns to the stack loop, which
	// comes back through the instruction's resume trampoline (OpCode.RegResume, RegisterResume).
	sealed partial class Processor
	{
		/// <summary>
		/// Entered from the stack loop at a BeginFn that has register code: the frame was pushed by a stack
		/// call (function, arguments, then BasePointer). Returns the stack address to continue at.
		/// </summary>
		private int RegisterEnter(Instruction beginFn)
		{
			RegisterProto p = beginFn.Register;
			ref CallStackItem frame = ref m_ExecutionStack.Peek();

			Closure closure = frame.Function;
			if (closure.Register == null)
				closure.Register = p;

			int argc = frame.ArgCount;
			int bp = frame.BasePointer - argc;

			// A last argument that is a call result (f(g())) or no value: expand it as ExecArgs does.
			if (argc > 0)
			{
				DataType lt = m_ValueStack.Items[bp + argc - 1].Type;

				if (lt == DataType.Tuple || lt == DataType.Void)
					argc = ExpandLastArgument(bp, argc);
			}

			frame.BasePointer = bp;
			frame.ArgCount = 0;
			frame.Flags |= CallStackItemFlags.RegisterFrame;
			frame.Debug_Symbols = p.Symbols;

			int top = bp + p.FrameSize;

			if (m_ValueStack.Items.Length < top)
				m_ValueStack.Expand(top - m_ValueStack.Count, LuaValue.Nil);

			LuaValue[] items = m_ValueStack.Items;
			int np = Math.Min(argc, p.NumParams);

			for (int k = 0; k < np; k++)
				items[bp + k] = ParameterValue(items[bp + k]);

			for (int k = np; k < p.FrameSize; k++)
				items[bp + k] = LuaValue.Nil;

			// Arguments past the frame.
			for (int k = bp + p.FrameSize; k < m_ValueStack.Count; k++)
				items[k] = default(LuaValue);

			m_ValueStack.Count = top;

			if (p.EnvReg >= 0)
				items[bp + p.EnvReg] = frame.Upvalues[0].Get();

			return RegisterLoop(p, bp, 0);
		}

		private int ExpandLastArgument(int bp, int argc)
		{
			LuaValue last = m_ValueStack.Items[bp + argc - 1];
			m_ValueStack.CropAtCount(bp + argc - 1);

			if (last.Type == DataType.Tuple)
			{
				DynValue[] tuple = last.TupleValue.Tuple;

				for (int k = 0; k < tuple.Length; k++)
					m_ValueStack.Push(tuple[k].ToLuaValue());

				return argc - 1 + tuple.Length;
			}

			return argc - 1;
		}

		/// <summary>
		/// The stack loop reached a resume trampoline: the slow path of the top frame's instruction before
		/// its SavedPc finished, and its result is on top of the value stack.
		/// </summary>
		private int RegisterResume()
		{
			ref CallStackItem frame = ref m_ExecutionStack.Peek();
			RegisterProto p = frame.Function.Register;
			int bp = frame.BasePointer;
			int pc = RegisterComplete(ref p.Code[frame.SavedPc - 1], bp, frame.SavedPc);
			return RegisterLoop(p, bp, pc);
		}

		/// <summary>
		/// Takes the result of a slow path from the top of the value stack into the instruction's
		/// destination; returns the next pc (a compare-and-jump may branch).
		/// </summary>
		private int RegisterComplete(ref RInstr ins, int bp, int pc)
		{
			switch (ins.Op)
			{
				case ROp.CallD:
				case ROp.SetField:
				case ROp.SetTable:
				case ROp.SetTabUp:
					m_ValueStack.RemoveLast();
					return pc;
				case ROp.LtJf:
				case ROp.LtJt:
				case ROp.EqJf:
				case ROp.EqJt:
					{
						bool b = m_ValueStack.Pop().ToScalar().CastToBool();
						return (b == (ins.Op == ROp.LtJt || ins.Op == ROp.EqJt)) ? ins.C : pc;
					}
				case ROp.LeJf:
				case ROp.LeJt:
					{
						// ExecLessEq leaves a flag below the result: true when it called __lt with the operands
						// swapped, whose result is then negated (as CNot does).
						bool v = m_ValueStack.Pop().ToScalar().CastToBool();
						bool b = m_ValueStack.Pop().CastToBool() ? !v : v;
						return (b == (ins.Op == ROp.LeJt)) ? ins.C : pc;
					}
				default:
					{
						LuaValue v = ParameterValue(m_ValueStack.Pop());
						m_ValueStack.Items[bp + ins.A] = v;
						return pc;
					}
			}
		}

		private int RegisterLoop(RegisterProto p, int bp, int pc)
		{
			RInstr[] code = p.Code;
			LuaValue[] K = p.K;
			LuaValue[] items = m_ValueStack.Items;
			Upvalue[] upvalues = m_ExecutionStack.Peek().Upvalues;
			DebugContext debug = m_Debug;
			LuaValue retval;

			while (true)
			{
				// The interrupt handler and instruction count, as in the stack loop.
				if (--debug.Countdown <= 0)
				{
					m_ExecutionStack.Peek().SavedPc = pc;
					ExecutionCheckpoint();
				}

				ref RInstr ins = ref code[pc++];

				switch (ins.Op)
				{
					case ROp.Move:
						items[bp + ins.A] = items[bp + ins.B];
						break;
					case ROp.LoadK:
						items[bp + ins.A] = K[ins.B];
						break;
					case ROp.GetUpval:
						items[bp + ins.A] = upvalues[ins.B].Get();
						break;
					case ROp.SetUpval:
						upvalues[ins.B].Set(items[bp + ins.A]);
						break;

					case ROp.AddRR:
						{
							ref LuaValue l = ref items[bp + ins.B];
							ref LuaValue r = ref items[bp + ins.C];

							if (l.Type == DataType.Number && r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number + r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.AddRK:
						{
							ref LuaValue l = ref items[bp + ins.B];

							if (l.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number + K[ins.C].Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.AddKR:
						{
							ref LuaValue r = ref items[bp + ins.C];

							if (r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(K[ins.B].Number + r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.SubRR:
						{
							ref LuaValue l = ref items[bp + ins.B];
							ref LuaValue r = ref items[bp + ins.C];

							if (l.Type == DataType.Number && r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number - r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.SubRK:
						{
							ref LuaValue l = ref items[bp + ins.B];

							if (l.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number - K[ins.C].Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.SubKR:
						{
							ref LuaValue r = ref items[bp + ins.C];

							if (r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(K[ins.B].Number - r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.MulRR:
						{
							ref LuaValue l = ref items[bp + ins.B];
							ref LuaValue r = ref items[bp + ins.C];

							if (l.Type == DataType.Number && r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number * r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.MulRK:
						{
							ref LuaValue l = ref items[bp + ins.B];

							if (l.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number * K[ins.C].Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.MulKR:
						{
							ref LuaValue r = ref items[bp + ins.C];

							if (r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(K[ins.B].Number * r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.DivRR:
						{
							ref LuaValue l = ref items[bp + ins.B];
							ref LuaValue r = ref items[bp + ins.C];

							if (l.Type == DataType.Number && r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number / r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.DivRK:
						{
							ref LuaValue l = ref items[bp + ins.B];

							if (l.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(l.Number / K[ins.C].Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.DivKR:
						{
							ref LuaValue r = ref items[bp + ins.C];

							if (r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(K[ins.B].Number / r.Number);
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.ModRR:
						{
							ref LuaValue l = ref items[bp + ins.B];
							ref LuaValue r = ref items[bp + ins.C];

							if (l.Type == DataType.Number && r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(Modulo(l.Number, r.Number));
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.ModRK:
						{
							ref LuaValue l = ref items[bp + ins.B];

							if (l.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(Modulo(l.Number, K[ins.C].Number));
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.ModKR:
						{
							ref LuaValue r = ref items[bp + ins.C];

							if (r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(Modulo(K[ins.B].Number, r.Number));
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.PowRR:
						{
							ref LuaValue l = ref items[bp + ins.B];
							ref LuaValue r = ref items[bp + ins.C];

							if (l.Type == DataType.Number && r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(Math.Pow(l.Number, r.Number));
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.PowRK:
						{
							ref LuaValue l = ref items[bp + ins.B];

							if (l.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(Math.Pow(l.Number, K[ins.C].Number));
								break;
							}
						}
						goto arithmetic_slow;
					case ROp.PowKR:
						{
							ref LuaValue r = ref items[bp + ins.C];

							if (r.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(Math.Pow(K[ins.B].Number, r.Number));
								break;
							}
						}
						goto arithmetic_slow;

					case ROp.Unm:
						{
							ref LuaValue v = ref items[bp + ins.B];

							if (v.Type == DataType.Number)
							{
								items[bp + ins.A] = LuaValue.FromNumber(-v.Number);
								break;
							}

							m_ExecutionStack.Peek().SavedPc = pc;
							m_ValueStack.Push(v);
							int ip = ExecNeg(null, ins.D);
							if (ip != ins.D) return ip;
							pc = RegisterComplete(ref ins, bp, pc);
							items = m_ValueStack.Items;
						}
						break;
					case ROp.Not:
						items[bp + ins.A] = LuaValue.FromBoolean(!items[bp + ins.B].CastToBool());
						break;

					case ROp.Jmp:
						pc = ins.A;
						break;
					case ROp.JmpIf:
						if (items[bp + ins.A].CastToBool())
							pc = ins.B;
						break;
					case ROp.JmpIfNot:
						if (!items[bp + ins.A].CastToBool())
							pc = ins.B;
						break;

					case ROp.LtJf:
					case ROp.LtJt:
					case ROp.LeJf:
					case ROp.LeJt:
					case ROp.EqJf:
					case ROp.EqJt:
						{
							ROp op = ins.Op;
							ref LuaValue l = ref (ins.A >= 0 ? ref items[bp + ins.A] : ref K[~ins.A]);
							ref LuaValue r = ref (ins.B >= 0 ? ref items[bp + ins.B] : ref K[~ins.B]);
							int res;

							if (l.Type == DataType.Number && r.Type == DataType.Number)
							{
								if (op <= ROp.LtJt) res = l.Number < r.Number ? 1 : 0;
								else if (op <= ROp.LeJt) res = l.Number <= r.Number ? 1 : 0;
								else res = l.Number == r.Number ? 1 : 0;
							}
							else
							{
								res = FusedCompare(op <= ROp.LtJt ? OpCode.JLt : (op <= ROp.LeJt ? OpCode.JLe : OpCode.JEq), ref l, ref r);
							}

							if (res >= 0)
							{
								// Jt variants are the odd ones.
								if ((res != 0) == (((op - ROp.LtJf) & 1) != 0))
									pc = ins.C;
								break;
							}

							m_ExecutionStack.Peek().SavedPc = pc;
							m_ValueStack.Push(l);
							m_ValueStack.Push(r);
							int ip = op <= ROp.LtJt ? ExecLess(null, ins.D) : (op <= ROp.LeJt ? ExecLessEq(null, ins.D) : ExecEq(null, ins.D));
							if (ip != ins.D) return ip;
							pc = RegisterComplete(ref ins, bp, pc);
							items = m_ValueStack.Items;
						}
						break;

					case ROp.ForNum:
						{
							ref LuaValue v = ref items[bp + ins.A];

							if (v.Type != DataType.Number)
							{
								double? n = v.ToScalar().CastToNumber();

								if (!n.HasValue)
								{
									m_ExecutionStack.Peek().SavedPc = pc;
									throw ScriptRuntimeException.ConvertToNumberFailed(ins.B);
								}

								v = LuaValue.FromNumber(n.Value);
							}
						}
						break;
					case ROp.ForPrep:
						{
							int a = bp + ins.A;
							double counter = items[a].Number, limit = items[a + 1].Number, step = items[a + 2].Number;

							if (step > 0 ? counter <= limit : counter >= limit)
								items[bp + ins.B] = LuaValue.FromNumber(counter);
							else
								pc = ins.C;
						}
						break;
					case ROp.ForLoop:
						{
							int a = bp + ins.A;
							double step = items[a + 2].Number;
							double counter = items[a].Number + step;
							items[a].Number = counter;

							if (step > 0 ? counter <= items[a + 1].Number : counter >= items[a + 1].Number)
							{
								items[bp + ins.B] = LuaValue.FromNumber(counter);
								pc = ins.C;
							}
						}
						break;

					case ROp.GetTabUp:
					case ROp.GetField:
						{
							Instruction aux = p.Aux[ins.C];
							LuaValue obj = ins.Op == ROp.GetField ? items[bp + ins.B] : upvalues[ins.B].Get();

							if (obj.Type == DataType.Table)
							{
								FieldCache cache = aux.Cache;

								if (!cache.Disabled)
								{
									Table t = (Table)obj.Object;
									DynValue v = cache.Get(t) ?? cache.Lookup(t, out t);

									if (v != null)
									{
										items[bp + ins.A] = v.ToLuaValue();
										break;
									}
								}
							}

							m_ExecutionStack.Peek().SavedPc = pc;
							m_ValueStack.Push(obj);
							int ip = ExecIndex(aux, ins.D);
							if (ip != ins.D) return ip;
							pc = RegisterComplete(ref ins, bp, pc);
							items = m_ValueStack.Items;
						}
						break;
					case ROp.GetTable:
						{
							ref LuaValue obj = ref items[bp + ins.B];
							ref LuaValue key = ref (ins.C >= 0 ? ref items[bp + ins.C] : ref K[~ins.C]);

							if (obj.Type == DataType.Table)
							{
								Table t = (Table)obj.Object;
								DynValue v = TableGet(t, ref key);

								if (!v.IsNil())
								{
									items[bp + ins.A] = v.ToLuaValue();
									break;
								}

								if (t.MetaTable == null)
								{
									items[bp + ins.A] = LuaValue.Nil;
									break;
								}
							}

							m_ExecutionStack.Peek().SavedPc = pc;
							m_ValueStack.Push(obj);
							m_ValueStack.Push(key);
							int ip = ExecIndex(p.IndexAux, ins.D);
							if (ip != ins.D) return ip;
							pc = RegisterComplete(ref ins, bp, pc);
							items = m_ValueStack.Items;
						}
						break;
					case ROp.SetTabUp:
					case ROp.SetField:
						{
							Instruction aux = p.Aux[ins.B];
							LuaValue obj = ins.Op == ROp.SetField ? items[bp + ins.A] : upvalues[ins.A].Get();
							ref LuaValue value = ref items[bp + ins.C];

							if (obj.Type == DataType.Table && value.IsNotNil())
							{
								Table table = (Table)obj.Object;
								FieldCache cache = aux.Cache;
								DynValue v = value.ToDynValue();

								if (cache.TrySet(table, v))
									break;

								int e = cache.FindLive(table);

								if (e >= 0)
								{
									table.SetEntryValue(e, v);
									break;
								}

								if (table.MetaTable == null)
								{
									cache.Add(table, v);
									break;
								}
							}

							m_ExecutionStack.Peek().SavedPc = pc;
							m_ValueStack.Push(value);
							m_ValueStack.Push(obj);
							int ip = ExecIndexSet(aux, ins.D);
							if (ip != ins.D) return ip;
							pc = RegisterComplete(ref ins, bp, pc);
							items = m_ValueStack.Items;
						}
						break;
					case ROp.SetTable:
						{
							ref LuaValue obj = ref items[bp + ins.A];
							ref LuaValue key = ref (ins.B >= 0 ? ref items[bp + ins.B] : ref K[~ins.B]);
							ref LuaValue value = ref items[bp + ins.C];

							if (obj.Type == DataType.Table && ((Table)obj.Object).MetaTable == null)
							{
								// A nil or NaN key raises an error here.
								m_ExecutionStack.Peek().SavedPc = pc;
								TableSet((Table)obj.Object, ref key, value.ToDynValue());
								break;
							}

							m_ExecutionStack.Peek().SavedPc = pc;
							m_ValueStack.Push(value);
							m_ValueStack.Push(obj);
							m_ValueStack.Push(key);
							int ip = ExecIndexSet(p.IndexSetAux, ins.D);
							if (ip != ins.D) return ip;
							pc = RegisterComplete(ref ins, bp, pc);
							items = m_ValueStack.Items;
						}
						break;

					case ROp.CallS:
					case ROp.CallD:
						{
							ref LuaValue fn = ref items[bp + ins.A];
							Closure callee = fn.Type == DataType.Function ? (Closure)fn.Object : null;
							RegisterProto cp = callee != null ? callee.Register : null;

							ref CallStackItem caller = ref m_ExecutionStack.Peek();
							caller.SavedPc = pc;

							if (cp == null)
							{
								// A C# function, a stack function or __call: the stack VM's call.
								for (int k = 0; k <= ins.B; k++)
									m_ValueStack.Push(items[bp + ins.A + k]);

								int ip = Internal_ExecCall(ins.B, ins.D, debugText: p.Aux[ins.C].Name);
								if (ip != ins.D) return ip;
								pc = RegisterComplete(ref ins, bp, pc);
								items = m_ValueStack.Items;
								break;
							}

							// A register function: its frame starts at the first argument.
							int cCalls = caller.CCalls;
							int nb = bp + ins.A + 1;
							int argc = ins.B;

							ref CallStackItem frame = ref m_ExecutionStack.Push();
							frame.BasePointer = nb;
							frame.ArgCount = 0;
							frame.ReturnAddress = ins.D;
							frame.Debug_EntryPoint = callee.EntryPointByteCodeLocation;
							frame.CallingSourceRef = p.Refs[pc - 1];
							frame.Function = callee;
							frame.Upvalues = callee.Upvalues;
							frame.Flags = CallStackItemFlags.RegisterCall | CallStackItemFlags.RegisterFrame;
							frame.Debug_Symbols = cp.Symbols;
							frame.CCalls = cCalls;

							int top = nb + cp.FrameSize;

							if (items.Length < top)
							{
								m_ValueStack.Expand(top - m_ValueStack.Count, LuaValue.Nil);
								items = m_ValueStack.Items;
							}

							for (int k = Math.Min(argc, cp.NumParams); k < cp.FrameSize; k++)
								items[nb + k] = LuaValue.Nil;

							m_ValueStack.Count = top;

							p = cp;
							code = p.Code;
							K = p.K;
							bp = nb;
							pc = 0;
							upvalues = callee.Upvalues;
						}
						break;

					case ROp.Ret0:
						retval = LuaValue.Void;
						goto do_return;
					case ROp.Ret1:
						retval = items[bp + ins.A];
						goto do_return;

					case ROp.CallT:
						{
							// return f(...): the call returns to a stack Ret 1 (D), so the stack VM's tail
							// call optimisation and multiple results apply. A C# function returns here.
							m_ExecutionStack.Peek().SavedPc = pc;

							for (int k = 0; k <= ins.B; k++)
								m_ValueStack.Push(items[bp + ins.A + k]);

							int ip = Internal_ExecCall(ins.B, ins.D, debugText: p.Aux[ins.C].Name);
							if (ip != ins.D) return ip;
							retval = m_ValueStack.Pop();
							items = m_ValueStack.Items;
						}
						goto do_return;

					default:
						throw new InternalErrorException("register opcode {0}", ins.Op);
				}

				continue;

			arithmetic_slow:
				{
					// Not two numbers: coercion of strings, metamethods and errors, by the stack VM's helper.
					ROp op = ins.Op;
					int kind = (op - ROp.AddRR) / 6;
					LuaValue l = kind == 2 ? K[ins.B] : items[bp + ins.B];
					LuaValue r = kind == 1 ? K[ins.C] : items[bp + ins.C];

					m_ExecutionStack.Peek().SavedPc = pc;
					m_ValueStack.Push(l);
					m_ValueStack.Push(r);
					int ip = ExecArithmetic(StackArithmetic(op), ins.D);
					if (ip != ins.D) return ip;
					pc = RegisterComplete(ref ins, bp, pc);
					items = m_ValueStack.Items;
				}
				continue;

			do_return:
				{
					ref CallStackItem frame = ref m_ExecutionStack.Peek();

					if ((frame.Flags & CallStackItemFlags.RegisterCall) == 0 || frame.Handlers != null)
					{
						// Back to a stack caller: as ExecRet does.
						CallStackItemHandlers handlers;
						int retpoint = PopScriptFrame(out handlers);
						m_ValueStack.Push(retval);

						if (handlers != null)
						{
							ApplyReturnHandlers(handlers, p.Refs[pc - 1]);

							if (handlers.ReturnsToPending)
								ReturnToPendingFrames();
						}

						return retpoint;
					}

					// Back to the register caller, in this loop.
					m_ExecutionStack.RemoveLast();

					if (m_OpenUpvalues != null)
						CloseUpvalues(bp);

					int oldTop = m_ValueStack.Count;
					ref CallStackItem caller = ref m_ExecutionStack.Peek();
					p = caller.Function.Register;
					code = p.Code;
					K = p.K;
					bp = caller.BasePointer;
					pc = caller.SavedPc;
					upvalues = caller.Upvalues;

					int top = bp + p.FrameSize;

					for (int k = top; k < oldTop; k++)
						items[k] = default(LuaValue);

					m_ValueStack.Count = top;

					ref RInstr call = ref code[pc - 1];

					if (call.Op == ROp.CallS)
						items[bp + call.A] = ParameterValue(retval);
				}
			}
		}

		private static OpCode StackArithmetic(ROp op)
		{
			switch ((int)(op - ROp.AddRR) % 6)
			{
				case 0: return OpCode.Add;
				case 1: return OpCode.Sub;
				case 2: return OpCode.Mul;
				case 3: return OpCode.Div;
				case 4: return OpCode.Mod;
				default: return OpCode.Power;
			}
		}
	}
}
