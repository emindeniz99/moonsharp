﻿using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using MoonSharp.Interpreter.DataStructs;
using MoonSharp.Interpreter.Debugging;
using MoonSharp.Interpreter.Interop;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		const int YIELD_SPECIAL_TRAP = -99;

		// Set when the loop must first return the value on top of the stack into the pending C# frame on
		// top of the call stack (see ClrCallWithHandlersReturned).
		bool m_ReturnToPending;

		internal long AutoYieldCounter = 0;

		private DynValue Processing_Loop(int instructionPtr)
		{
			return Processing_LoopValue(instructionPtr).ToDynValue();
		}

		// The loop itself. It returns the result as a VM value, so a typed call from C# reads it without a
		// DynValue (Processor_HostCall.cs).
		private LuaValue Processing_LoopValue(int instructionPtr)
		{
			// This is the main loop of the processor, has a weird control flow and needs to be as fast as possible.
			// This sentence is just a convoluted way to say "don't complain about gotos".

			long executedInstructions = 0;
			bool canAutoYield = (AutoYieldCounter > 0) && m_CanYield && (this.State != CoroutineState.Main);
			DebugContext debug = m_Debug;

			// Fetch from an array rather than through List<Instruction>. Code is only appended (load(),
			// string.dump), so this array stays valid; a null slot or an address past its end means code
			// was added after it was taken, and GetCodeArray returns one that holds it.
			Instruction[] code = m_RootChunk.GetCodeArray();
			InterpreterException unhandled = null;

			// Fused instructions take their unfused path while a debugger is attached, so it sees every
			// instruction of the sequences they replace (InstructionFusion).
			bool fuse = debug.DebuggerAttached == null;

			repeat_execution:

			// The running frame's base pointer, where its locals start. Ops that enter or leave a script
			// function refresh it.
			int basePointer = CurrentBasePointer();

			try
			{
				// A pending C# frame on top (after a resume, or after an error handler of the call it
				// waited for) takes the value on top of the stack as its result first.
				if (m_ReturnToPending)
				{
					m_ReturnToPending = false;
					ReturnToPendingFrames();
					basePointer = CurrentBasePointer();
				}

				if (instructionPtr < 0)
					goto return_to_native_code;

				while (true)
				{
					Instruction i;

					if ((uint)instructionPtr >= (uint)code.Length || (i = code[instructionPtr]) == null)
					{
						code = m_RootChunk.GetCodeArray();
						i = code[instructionPtr];
					}

					// One countdown, shared by every loop of this Script, drives the slow path: the
					// interrupt handler every INTERRUPT_INTERVAL instructions, and the debugger on every
					// instruction while one is attached (attaching sets the countdown to zero, so it
					// stops at the next instruction). Auto-yield counts per loop, as before.
					if ((--debug.Countdown <= 0) | canAutoYield)
					{
						if (debug.Countdown <= 0)
							ExecutionCheckpoint();

						fuse = debug.DebuggerAttached == null;

						if (!fuse)
						{
							ListenDebugger(i, instructionPtr);
						}

						if (canAutoYield && ++executedInstructions > AutoYieldCounter)
						{
							m_SavedInstructionPtr = instructionPtr;
							return DynValue.NewForcedYieldReq().ToLuaValue();
						}
					}

					++instructionPtr;

					switch (i.OpCode)
					{
						case OpCode.Nop:
						case OpCode.Debug:
						case OpCode.Meta:
							break;
						case OpCode.Pop:
							m_ValueStack.RemoveLast(i.NumVal);
							break;
						case OpCode.Copy:
							m_ValueStack.Push(m_ValueStack.Peek(i.NumVal));
							break;
						case OpCode.Swap:
							ExecSwap(i);
							break;
						case OpCode.Literal:
							m_ValueStack.Push(i.Value.ToLuaValue());
							break;
						case OpCode.Add:
						case OpCode.Sub:
						case OpCode.Mul:
						case OpCode.Div:
						case OpCode.Mod:
						case OpCode.Power:
							{
								int next = instructionPtr;
								instructionPtr = ExecArithmetic(i.OpCode, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.Concat:
							{
								int next = instructionPtr;
								instructionPtr = ExecConcat(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.Neg:
							{
								int next = instructionPtr;
								instructionPtr = ExecNeg(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.Eq:
							{
								int next = instructionPtr;
								instructionPtr = ExecEq(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.LessEq:
							{
								int next = instructionPtr;
								instructionPtr = ExecLessEq(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.Less:
							{
								int next = instructionPtr;
								instructionPtr = ExecLess(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.Len:
							{
								int next = instructionPtr;
								instructionPtr = ExecLen(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.Call:
						case OpCode.ThisCall:
							instructionPtr = Internal_ExecCall(i.NumVal, instructionPtr, null, null, i.OpCode == OpCode.ThisCall, i.Name);
							if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
							basePointer = CurrentBasePointer();
							break;
						case OpCode.Scalar:
							{
								ref LuaValue top = ref m_ValueStack.Peek();

								if (top.Type == DataType.Tuple)
									top = top.ToScalar();

								// (f()) is one value even when f returns none: nil.
								if (top.Type == DataType.Void)
									top = LuaValue.Nil;
							}
							break;
						case OpCode.Not:
							ExecNot(i);
							break;
						case OpCode.CNot:
							ExecCNot(i);
							break;
						case OpCode.JfOrPop:
						case OpCode.JtOrPop:
							instructionPtr = ExecShortCircuitingOperator(i, instructionPtr);
							if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
							break;
						case OpCode.JNil:
							{
								LuaValue v = m_ValueStack.Pop().ToScalar();

								if (v.Type == DataType.Nil || v.Type == DataType.Void)
									instructionPtr = i.NumVal;
							}
							if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
							break;
						case OpCode.Jf:
							instructionPtr = JumpBool(i, false, instructionPtr);
							if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
							break;
						case OpCode.Jump:
							instructionPtr = i.NumVal;
							if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
							break;
						case OpCode.MkTuple:
							ExecMkTuple(i);
							break;
						case OpCode.Clean:
							ClearBlockData(i, basePointer);
							break;
						case OpCode.Closure:
							ExecClosure(i);
							break;
						case OpCode.BeginFn:
							ExecBeginFn(i);
							break;
						case OpCode.ToBool:
							{
								ref LuaValue top = ref m_ValueStack.Peek();
								top = LuaValue.FromBoolean(top.ToScalar().CastToBool());
							}
							break;
						case OpCode.Args:
							ExecArgs(i);
							break;
						case OpCode.Ret:
							instructionPtr = ExecRet(i);
							if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
							if (instructionPtr < 0)
								goto return_to_native_code;
							basePointer = CurrentBasePointer();
							break;
						case OpCode.Incr:
							ExecIncr(i);
							break;
						case OpCode.ToNum:
							ExecToNum(i);
							break;
						case OpCode.JFor:
							instructionPtr = ExecJFor(i, instructionPtr);
							if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
							break;
						case OpCode.JLt:
						case OpCode.JLe:
						case OpCode.JGt:
						case OpCode.JGe:
						case OpCode.JEq:
						case OpCode.JNe:
							{
								LuaValue[] items = m_ValueStack.Items;
								int top = m_ValueStack.Count;
								int mode = i.NumVal2;
								LuaValue l, r;
								int pop;

								switch (mode)
								{
									case InstructionFusion.RightOnStack: l = items[top - 2]; r = items[top - 1]; pop = 2; break;
									case InstructionFusion.RightConst: l = items[top - 1]; r = i.Value.ToLuaValue(); pop = 1; break;
									case InstructionFusion.RightLocal: l = items[top - 1]; r = items[basePointer + i.Symbol.i_Index]; pop = 1; break;
									case InstructionFusion.LeftLocalConst: l = items[basePointer + i.Symbol.i_Index]; r = i.Value.ToLuaValue(); pop = 0; break;
									default: l = items[basePointer + i.Symbol.i_Index]; r = items[basePointer + i.NumVal3]; pop = 0; break;
								}

								int result = fuse ? FusedCompare(i.OpCode, ref l, ref r) : -1;

								if (result >= 0)
								{
									for (int k = 0; k < pop; k++)
										items[--top] = default(LuaValue);

									m_ValueStack.Count = top;

									// Branch as the Jf at the end of the sequence would.
									int jf = instructionPtr - 1 + i.NumVal;
									instructionPtr = result != 0 ? jf + 1 : code[jf].NumVal;
								}
								else if (mode == InstructionFusion.RightOnStack)
								{
									int next = instructionPtr;

									if (i.OpCode == OpCode.JGt || i.OpCode == OpCode.JGe)
									{
										items[top - 2] = r;
										items[top - 1] = l;
									}
									else if (i.OpCode == OpCode.JLt)
										instructionPtr = ExecLess(i, instructionPtr);
									else if (i.OpCode == OpCode.JLe)
										instructionPtr = ExecLessEq(i, instructionPtr);
									else
										instructionPtr = ExecEq(i, instructionPtr);

									// A metamethod written in Lua runs in a new frame: start it with its base pointer.
									if (instructionPtr != next)
									{
										if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
										basePointer = CurrentBasePointer();
									}
								}
								else
								{
									// Run the replaced Literal or Local: push the operand it pushed.
									m_ValueStack.Push(mode <= InstructionFusion.RightLocal ? r : l);
								}
							}
							break;
						case OpCode.AddF:
						case OpCode.SubF:
						case OpCode.MulF:
						case OpCode.DivF:
						case OpCode.ModF:
						case OpCode.PowF:
							{
								LuaValue[] items = m_ValueStack.Items;
								int top = m_ValueStack.Count;
								int mode = i.NumVal2;
								bool numbers;
								double l, r;
								int pop;

								switch (mode & InstructionFusion.ModeMask)
								{
									case InstructionFusion.RightOnStack:
										numbers = items[top - 2].Type == DataType.Number && items[top - 1].Type == DataType.Number;
										l = items[top - 2].Number; r = items[top - 1].Number; pop = 2;
										break;
									case InstructionFusion.RightConst:
										numbers = items[top - 1].Type == DataType.Number;
										l = items[top - 1].Number; r = i.Value.Number; pop = 1;
										break;
									case InstructionFusion.RightLocal:
										{
											ref LuaValue b = ref items[basePointer + i.Symbol.i_Index];
											numbers = items[top - 1].Type == DataType.Number && b.Type == DataType.Number;
											l = items[top - 1].Number; r = b.Number; pop = 1;
										}
										break;
									case InstructionFusion.LeftLocalConst:
										{
											ref LuaValue a = ref items[basePointer + i.Symbol.i_Index];
											numbers = a.Type == DataType.Number;
											l = a.Number; r = i.Value.Number; pop = 0;
										}
										break;
									default:
										{
											ref LuaValue a = ref items[basePointer + i.Symbol.i_Index];
											ref LuaValue b = ref items[basePointer + i.NumVal];
											numbers = a.Type == DataType.Number && b.Type == DataType.Number;
											l = a.Number; r = b.Number; pop = 0;
										}
										break;
								}

								if (numbers && fuse)
								{
									LuaValue result = LuaValue.FromNumber(FusedArithmetic(i.OpCode, l, r));
									int store = i.NumVal3;

									// The popped operands are numbers: their slots hold no object to clear.
									if (store >= 0)
									{
										m_ValueStack.Count = top - pop;
										items[basePointer + store] = result;
									}
									else if (pop > 0)
									{
										items[top - pop] = result;
										m_ValueStack.Count = top - pop + 1;
									}
									else
									{
										m_ValueStack.Push(result);
									}

									instructionPtr += InstructionFusion.ArithmeticLength(mode, store >= 0) - 1;
								}
								else if ((mode & InstructionFusion.ModeMask) == InstructionFusion.RightOnStack)
								{
									int next = instructionPtr;
									instructionPtr = ExecArithmetic(UnfusedArithmetic(i.OpCode), instructionPtr);

									// A metamethod written in Lua runs in a new frame: start it with its base pointer.
									if (instructionPtr != next)
									{
										if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
										basePointer = CurrentBasePointer();
									}
								}
								else
								{
									// Run the replaced Literal or Local: push the operand it pushed.
									m_ValueStack.Push((mode & InstructionFusion.ModeMask) == InstructionFusion.RightConst ? i.Value.ToLuaValue() : items[basePointer + i.Symbol.i_Index]);
								}
							}
							break;
						case OpCode.ForLoop:
							{
								// stack: limit, step, counter (numbers, checked by ToNum before the loop)
								LuaValue[] items = m_ValueStack.Items;
								int from = i.NumVal2;

								// Leave the iteration's block, as Clean does: closures that captured its
								// locals keep this iteration's values.
								if (m_OpenUpvalues != null)
									CloseUpvalues(basePointer + from);

								for (int k = from; k <= i.NumVal3; k++)
									items[basePointer + k] = LuaValue.Undeclared;

								int top = m_ValueStack.Count;
								double step = items[top - 2].Number;
								double counter = items[top - 1].Number + step;
								items[top - 1].Number = counter;

								if (step > 0 ? counter <= items[top - 3].Number : counter >= items[top - 3].Number)
								{
									items[basePointer + i.Symbol.i_Index] = LuaValue.FromNumber(counter);
									instructionPtr = i.NumVal;
								}
							}
							break;
						case OpCode.NewTable:
							m_ValueStack.Push(LuaValue.FromObject(DataType.Table, new Table(i.NumVal == 0 ? this.m_Script : null)));
							break;
						case OpCode.IterPrep:
							ExecIterPrep(i);
							break;
						case OpCode.IterUpd:
							ExecIterUpd(i);
							break;
						case OpCode.ExpTuple:
							ExecExpTuple(i);
							break;
						case OpCode.Local:
							m_ValueStack.Push(m_ValueStack.Items[basePointer + i.Symbol.i_Index]);
							break;
						case OpCode.Upvalue:
							m_ValueStack.Push(m_ExecutionStack.Peek().Upvalues[i.Symbol.i_Index].Get());
							break;
						case OpCode.StoreUpv:
							ExecStoreUpv(i);
							break;
						case OpCode.StoreLcl:
							m_ValueStack.Items[basePointer + i.Symbol.i_Index] = GetStoreValue(i);
							break;
						case OpCode.TblInitN:
							ExecTblInitN(i);
							break;
						case OpCode.TblInitI:
							ExecTblInitI(i);
							break;
						case OpCode.Index:
						case OpCode.IndexN:
						case OpCode.IndexL:
							{
								int next = instructionPtr;
								instructionPtr = ExecIndex(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.IndexSet:
						case OpCode.IndexSetN:
						case OpCode.IndexSetL:
							{
								int next = instructionPtr;
								instructionPtr = ExecIndexSet(i, instructionPtr);

								// A metamethod written in Lua runs in a new frame: start it with its base pointer.
								if (instructionPtr != next)
								{
									if (instructionPtr == YIELD_SPECIAL_TRAP) goto yield_to_calling_coroutine;
									basePointer = CurrentBasePointer();
								}
							}
							break;
						case OpCode.Invalid:
							throw new NotImplementedException(string.Format("Invalid opcode : {0}", i.Name));
						default:
							throw new NotImplementedException(string.Format("Execution for {0} not implented yet!", i.OpCode));
					}
				}

			yield_to_calling_coroutine:

				LuaValue yieldRequest = m_ValueStack.Pop().ToScalar();

				if (m_CanYield)
					return yieldRequest;
				else if (this.State == CoroutineState.Main)
					throw ScriptRuntimeException.CannotYieldMain();
				else
					throw ScriptRuntimeException.CannotYield();

			}
			catch (InterpreterException ex)
			{
				FillDebugData(ex, instructionPtr);
				unhandled = ex;

				// Script error handlers (pcall, xpcall) see runtime errors and compile errors raised while
				// running (require of a broken module), as in Lua; an interrupt passes them by.
				if (ex is ScriptRuntimeException || ex is SyntaxErrorException)
				{
					if (m_Debug.DebuggerAttached != null && ex is ScriptRuntimeException)
					{
						if (m_Debug.DebuggerAttached.SignalRuntimeException((ScriptRuntimeException)ex))
						{
							if (instructionPtr >= 0 && instructionPtr < this.m_RootChunk.Code.Count)
							{
								ListenDebugger(m_RootChunk.Code[instructionPtr], instructionPtr);
							}
						}
					}

					// Message handlers (xpcall) of the frames this error unwinds: down to the frame that
					// handles it or to this loop's entry point, not past them.
					for (int i = 0; i < m_ExecutionStack.Count; i++)
					{
						// Copies, not a ref: the message handler runs script code, which can move the frames.
						CallStackItemHandlers handlers = m_ExecutionStack.Peek(i).Handlers;
						CallStackItemFlags flags = m_ExecutionStack.Peek(i).Flags;

						if (handlers != null && handlers.ErrorHandlerBeforeUnwind != null)
							ex.DecoratedMessage = PerformMessageDecorationBeforeUnwind(handlers.ErrorHandlerBeforeUnwind, ex.DecoratedMessage, GetCurrentSourceRef(instructionPtr));

						if ((handlers != null && handlers.ErrorHandler != null) || (flags & CallStackItemFlags.EntryPoint) != 0)
							break;
					}

					while (m_ExecutionStack.Count > 0)
					{
						// Each frame takes its function and arguments with it (a pending C# frame has none
						// left), so the value stack ends where the handling frame's result goes.
						CallStackItem csi = PopToBasePointer();
						m_ValueStack.RemoveLast(csi.ArgCount + 1);

						if (csi.ErrorHandler != null)
						{
							instructionPtr = csi.ReturnAddress;

							var cbargs = new DynValue[] { DynValue.NewString(ex.DecoratedMessage) };

							DynValue handled = csi.ErrorHandler.Invoke(new ScriptExecutionContext(this, csi.ErrorHandler, GetCurrentSourceRef(instructionPtr)), cbargs);

							m_ValueStack.Push(handled);

							// The handled frame returned into a pending C# frame: that frame returns next,
							// inside the try block, so its own handlers see errors of its continuation.
							m_ReturnToPending = csi.Handlers.ReturnsToPending;

							goto repeat_execution;
						}
						else if ((csi.Flags & CallStackItemFlags.EntryPoint) != 0)
						{
							break;
						}
					}
				}
			}

			// Rethrow after the catch block, not inside it. A throw inside it starts a nested dispatch while
			// the frames it would unwind are still on the stack; through N nested loops (C# callbacks that
			// call back into Lua) CoreCLR then spends O(N^2) unwinding, minutes for a few thousand levels.
			// Capture the stack trace once, at the innermost loop: capturing it again at every level copies
			// a trace that grows with the depth, which is O(N^2) as well.
			unhandled.Rethrow();

			if (unhandled.DispatchInfo == null)
				unhandled.DispatchInfo = ExceptionDispatchInfo.Capture(unhandled);

			unhandled.DispatchInfo.Throw();
			return default(LuaValue);

		return_to_native_code:
			return m_ValueStack.Pop();


		}


		// Instructions between two calls of ScriptOptions.InterruptHandler (QuickJS polls as often).
		const int INTERRUPT_INTERVAL = 10000;

		private void ExecutionCheckpoint()
		{
			DebugContext debug = m_Debug;
			debug.Rearm(debug.DebuggerAttached != null ? 1 : INTERRUPT_INTERVAL);

			if (debug.Executed >= debug.NextInterruptCheck)
			{
				debug.NextInterruptCheck = debug.Executed + INTERRUPT_INTERVAL;

				Func<long, bool> handler = m_Script.Options.InterruptHandler;

				if (handler != null && handler(debug.Executed))
					throw new ScriptInterruptedException(debug.Executed);
			}
		}

		/// <summary>
		/// Called by library loops that can run long inside one instruction (string.gsub over a long
		/// subject, table.sort, table.concat, string.rep) with the steps they did: every INTERRUPT_INTERVAL
		/// steps the interrupt handler runs, as between instructions, so a time limit or a cancellation
		/// stops them too. The steps do not add to the instruction count the handler gets, which stays
		/// deterministic.
		/// </summary>
		internal void LibraryWork(int units)
		{
			DebugContext debug = m_Debug;

			if ((debug.LibraryWork += units) < INTERRUPT_INTERVAL)
				return;

			debug.LibraryWork = 0;

			Func<long, bool> handler = m_Script.Options.InterruptHandler;
			long executed = debug.Executed + debug.Armed - debug.Countdown;

			if (handler != null && handler(executed))
				throw new ScriptInterruptedException(executed);
		}

		internal string PerformMessageDecorationBeforeUnwind(DynValue messageHandler, string decoratedMessage, SourceRef sourceRef)
		{
			try
			{
				DynValue[] args = new DynValue[] { DynValue.NewString(decoratedMessage) };
				DynValue ret = DynValue.Nil;

				if (messageHandler.Type == DataType.Function)
				{
					ret = this.Call(messageHandler, args);
				}
				else if (messageHandler.Type == DataType.ClrFunction)
				{
					ScriptExecutionContext ctx = new ScriptExecutionContext(this, messageHandler.Callback, sourceRef);
					ret = messageHandler.Callback.Invoke(ctx, args);
				}
				else
				{
					throw new ScriptRuntimeException("error handler not set to a function");
				}

				string newmsg = ret.ToPrintString();
				if (newmsg != null)
					return newmsg;
			}
			catch (ScriptRuntimeException innerEx)
			{
				return innerEx.Message + "\n" + decoratedMessage;
			}

			return decoratedMessage;
		}



		/// <summary>
		/// Stores a value in a local of the running function. The local's slot holds a copy, so a value read
		/// from the variable earlier does not change when the variable is assigned again.
		/// </summary>
		private void AssignLocal(SymbolRef symref, LuaValue value)
		{
			m_ValueStack[m_ExecutionStack.Peek().BasePointer + symref.i_Index] = value;
		}

		private void ExecStoreUpv(Instruction i)
		{
			LuaValue value = GetStoreValue(i);
			m_ExecutionStack.Peek().Upvalues[i.Symbol.i_Index].Set(value);
		}

		private void ExecSwap(Instruction i)
		{
			ref LuaValue v1 = ref m_ValueStack.Peek(i.NumVal);
			ref LuaValue v2 = ref m_ValueStack.Peek(i.NumVal2);

			LuaValue tmp = v1;
			v1 = v2;
			v2 = tmp;
		}


		private LuaValue GetStoreValue(Instruction i)
		{
			int stackofs = i.NumVal;
			int tupleidx = i.NumVal2;

			ref LuaValue v = ref m_ValueStack.Peek(stackofs);

			if (v.Type == DataType.Tuple)
			{
				DynValue[] tuple = v.TupleValue.Tuple;
				DynValue e = (tupleidx < tuple.Length) ? tuple[tupleidx] : null;

				// A call that returns nothing leaves void; a variable takes nil, as in Lua.
				return (e == null || e.Type == DataType.Void) ? LuaValue.Nil : e.ToLuaValue();
			}

			if (tupleidx != 0 || v.Type == DataType.Void)
				return LuaValue.Nil;

			return v;
		}

		private void ExecClosure(Instruction i)
		{
			// Fill the upvalue list directly: no LINQ iterator, no intermediate list, no per-closure name array.
			SymbolRef[] symbols = i.SymbolList;
			Upvalue[] upvalues = null;

			if (symbols.Length > 0)
			{
				int basePointer = m_ExecutionStack.Peek().BasePointer;
				Upvalue[] enclosing = m_ExecutionStack.Peek().Upvalues;
				upvalues = new Upvalue[symbols.Length];

				for (int k = 0; k < symbols.Length; k++)
				{
					SymbolRef s = symbols[k];

					// A local of the running function: share its open upvalue. An upvalue of the running
					// function: share the same upvalue.
					if (s.Type == SymbolRefType.Local)
						upvalues[k] = FindUpvalue(basePointer + s.i_Index);
					else if (s.Type == SymbolRefType.Upvalue)
						upvalues[k] = enclosing[s.i_Index];
					else
						throw new Exception("unsupported symbol type");
				}
			}

			m_ValueStack.Push(LuaValue.FromObject(DataType.Function, new Closure(this.m_Script, i.NumVal, symbols, upvalues)));
		}

		private void ExecMkTuple(Instruction i)
		{
			int count = i.NumVal;
			int first = m_ValueStack.Count - count;
			DynValue[] values;

			// The last expression is a call that returned nothing (return 1, f()): it adds no value.
			if (count > 0 && m_ValueStack[first + count - 1].Type == DataType.Void)
			{
				m_ValueStack.RemoveLast();
				count--;
			}

			if (count > 0 && m_ValueStack[first + count - 1].Type == DataType.Tuple)
			{
				// The last expression is a call or '...': its values extend the list.
				DynValue[] items = new DynValue[count];

				for (int k = 0; k < count; k++)
					items[k] = m_ValueStack[first + k].ToDynValue();

				values = Internal_AdjustTuple(items);
			}
			else
			{
				values = new DynValue[count];

				for (int k = 0; k < count; k++)
					values[k] = m_ValueStack[first + k].ToScalar().ToDynValue();
			}

			m_ValueStack.RemoveLast(count);
			m_ValueStack.Push(DynValue.NewTuple(values).ToLuaValue());
		}

		private void ExecToNum(Instruction i)
		{
			ref LuaValue top = ref m_ValueStack.Peek();
			double? v = top.ToScalar().CastToNumber();

			if (v.HasValue)
				top = LuaValue.FromNumber(v.Value);
			else
				throw ScriptRuntimeException.ConvertToNumberFailed(i.NumVal);
		}


		private void ExecIterUpd(Instruction i)
		{
			// stack: iterator state (a LuaValue[3]: f, s, var), new value of var
			LuaValue v = m_ValueStack.Peek(0);
			LuaValue[] state = (LuaValue[])m_ValueStack.Peek(1).Object;
			state[2] = v;
		}

		private void ExecExpTuple(Instruction i)
		{
			LuaValue t = m_ValueStack.Peek(i.NumVal);

			if (t.Type == DataType.Tuple)
			{
				LuaValue[] state = t.Object as LuaValue[];

				if (state != null)
				{
					for (int idx = 0; idx < state.Length; idx++)
						m_ValueStack.Push(state[idx]);
				}
				else
				{
					DynValue[] tuple = t.TupleValue.Tuple;

					for (int idx = 0; idx < tuple.Length; idx++)
						m_ValueStack.Push(tuple[idx].ToLuaValue());
				}
			}
			else
			{
				m_ValueStack.Push(t);
			}

		}

		private void ExecIterPrep(Instruction i)
		{
			DynValue v = m_ValueStack.Pop().ToDynValue();

			if (v.Type != DataType.Tuple)
			{
				v = DynValue.NewTuple(v, DynValue.Nil, DynValue.Nil);
			}

			DynValue f = v.Tuple.Length >= 1 ? v.Tuple[0] : DynValue.Nil;
			DynValue s = v.Tuple.Length >= 2 ? v.Tuple[1] : DynValue.Nil;
			DynValue var = v.Tuple.Length >= 3 ? v.Tuple[2] : DynValue.Nil;

			// MoonSharp additions - given f, s, var
			// 1) if f is not a function and has a __iterator metamethod, call __iterator to get the triplet
			// 2) if f is a table with no __call metamethod, use a default table iterator

			if (f.Type != DataType.Function && f.Type != DataType.ClrFunction)
			{
				DynValue meta = this.GetMetamethod(f, "__iterator");

				if (meta != null && !meta.IsNil())
				{
					if (meta.Type != DataType.Tuple)
						v = this.GetScript().Call(meta, f, s, var);
					else
						v = meta;

					f = v.Tuple.Length >= 1 ? v.Tuple[0] : DynValue.Nil;
					s = v.Tuple.Length >= 2 ? v.Tuple[1] : DynValue.Nil;
					var = v.Tuple.Length >= 3 ? v.Tuple[2] : DynValue.Nil;

					PushIteratorState(f, s, var);
					return;
				}
				else if (f.Type == DataType.Table)
				{
					DynValue callmeta = this.GetMetamethod(f, "__call");

					if (callmeta == null || callmeta.IsNil())
					{
						DynValue[] wrapper = EnumerableWrapper.ConvertTable(f.Table).Tuple;
						PushIteratorState(wrapper[0], wrapper[1], wrapper[2]);
						return;
					}
				}
			}

			PushIteratorState(f, s, var);
		}

		/// <summary>
		/// Pushes the generic for's state as a LuaValue[3], which the loop updates in place every iteration
		/// (IterUpd) without creating a DynValue for the control variable.
		/// </summary>
		private void PushIteratorState(DynValue f, DynValue s, DynValue var)
		{
			LuaValue[] state = new LuaValue[] { f.ToLuaValue(), s.ToLuaValue(), var.ToLuaValue() };
			m_ValueStack.Push(LuaValue.FromObject(DataType.Tuple, state));
		}


		/// <summary>
		/// The result of a fused compare (1 true, 0 false) for operands that need no conversion or
		/// metamethod; -1 otherwise. Follows ExecLess, ExecLessEq and ExecEq: numbers compare as doubles
		/// (false with NaN), strings by ordinal; == is raw equality unless a table, a userdata or a tuple
		/// is involved.
		/// </summary>
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private static int FusedCompare(OpCode op, ref LuaValue l, ref LuaValue r)
		{
			if (op == OpCode.JEq || op == OpCode.JNe)
			{
				bool eq;

				if (l.Type == r.Type)
				{
					if (l.Type == DataType.Table || l.Type == DataType.UserData || l.Type == DataType.Tuple)
						return -1;

					eq = LuaValue.RawEquals(ref l, ref r);
				}
				else
				{
					if (l.Type == DataType.UserData || r.Type == DataType.UserData || l.Type == DataType.Tuple || r.Type == DataType.Tuple)
						return -1;

					eq = (l.Type == DataType.Nil && r.Type == DataType.Void) || (l.Type == DataType.Void && r.Type == DataType.Nil);
				}

				return (eq == (op == OpCode.JEq)) ? 1 : 0;
			}

			int c;

			if (l.Type == DataType.Number && r.Type == DataType.Number)
			{
				double a = l.Number, b = r.Number;

				switch (op)
				{
					case OpCode.JLt: return a < b ? 1 : 0;
					case OpCode.JLe: return a <= b ? 1 : 0;
					case OpCode.JGt: return b < a ? 1 : 0;
					default: return b <= a ? 1 : 0;
				}
			}
			else if (l.Type == DataType.String && r.Type == DataType.String)
			{
				c = string.CompareOrdinal((string)l.Object, (string)r.Object);
			}
			else
			{
				return -1;
			}

			switch (op)
			{
				case OpCode.JLt: return c < 0 ? 1 : 0;
				case OpCode.JLe: return c <= 0 ? 1 : 0;
				case OpCode.JGt: return c > 0 ? 1 : 0;
				default: return c >= 0 ? 1 : 0;
			}
		}

		private int CurrentBasePointer()
		{
			return m_ExecutionStack.Count > 0 ? m_ExecutionStack.Peek().BasePointer : -1;
		}

		private int ExecJFor(Instruction i, int instructionPtr)
		{
			double val = m_ValueStack.Peek(0).Number;
			double step = m_ValueStack.Peek(1).Number;
			double stop = m_ValueStack.Peek(2).Number;

			bool whileCond = (step > 0) ? val <= stop : val >= stop;

			if (!whileCond)
				return i.NumVal;
			else
				return instructionPtr;
		}



		private void ExecIncr(Instruction i)
		{
			// The counter is a stack slot of its own (the loop variable gets a copy), so it changes in place.
			m_ValueStack.Peek(0).Number += m_ValueStack.Peek(i.NumVal).Number;
		}


		private void ExecCNot(Instruction i)
		{
			LuaValue v = m_ValueStack.Pop().ToScalar();
			LuaValue not = m_ValueStack.Pop().ToScalar();

			if (not.Type != DataType.Boolean)
				throw new InternalErrorException("CNOT had non-bool arg");

			if (not.CastToBool())
				m_ValueStack.Push(LuaValue.FromBoolean(!(v.CastToBool())));
			else
				m_ValueStack.Push(LuaValue.FromBoolean(v.CastToBool()));
		}

		private void ExecNot(Instruction i)
		{
			ref LuaValue top = ref m_ValueStack.Peek();
			top = LuaValue.FromBoolean(!top.ToScalar().CastToBool());
		}

		private void ExecBeginFn(Instruction i)
		{
			ref CallStackItem cur = ref m_ExecutionStack.Peek();

			cur.Debug_Symbols = i.SymbolList;

			// The locals live on the value stack at the base pointer, one slot per local symbol, each
			// undeclared until its declaration runs.
			m_ValueStack.Expand(i.NumVal, LuaValue.Undeclared);
		}

		private CallStackItem PopToBasePointer()
		{
			var csi = m_ExecutionStack.Pop();
			if (csi.BasePointer >= 0)
			{
				CloseUpvalues(csi.BasePointer);
				m_ValueStack.CropAtCount(csi.BasePointer);
			}
			return csi;
		}

		/// <summary>
		/// Leaves the running script function: closes the upvalues of its locals and removes its frame,
		/// locals, temporaries, arguments and the function value. Returns the return address.
		/// </summary>
		private int PopScriptFrame(out CallStackItemHandlers handlers)
		{
			ref CallStackItem csi = ref m_ExecutionStack.Peek();
			int retpoint = csi.ReturnAddress;
			int basePointer = csi.BasePointer;
			int argCount = csi.ArgCount;
			handlers = csi.Handlers;
			m_ExecutionStack.RemoveLast();

			if (m_OpenUpvalues != null)
				CloseUpvalues(basePointer);

			m_ValueStack.CropAtCount(basePointer - argCount - 1);
			return retpoint;
		}

		private int PopExecStackAndCheckVStack(int vstackguard)
		{
			ref CallStackItem xs = ref m_ExecutionStack.Peek();
			int returnAddress = xs.ReturnAddress;
			int basePointer = xs.BasePointer;
			m_ExecutionStack.RemoveLast();

			if (vstackguard != basePointer)
				throw new InternalErrorException("StackGuard violation");

			return returnAddress;
		}

		static readonly DynValue[] s_NoArgs = new DynValue[0];

		/// <summary>
		/// The arguments of a call to a C# function, the top <paramref name="numargs"/> stack values, as
		/// DynValues. A last argument that is a tuple of more than one value is expanded.
		/// </summary>
		private DynValue[] CreateArgsListForFunctionCall(int numargs)
		{
			if (numargs == 0) return s_NoArgs;

			int first = m_ValueStack.Count - numargs;
			ref LuaValue lastParam = ref m_ValueStack[first + numargs - 1];

			if (lastParam.Type == DataType.Tuple && lastParam.TupleValue.Tuple.Length > 1)
			{
				DynValue[] tuple = lastParam.TupleValue.Tuple;
				DynValue[] values = new DynValue[numargs - 1 + tuple.Length];

				for (int idx = 0; idx < numargs - 1; idx++)
					values[idx] = m_ValueStack[first + idx].ToDynValue();

				Array.Copy(tuple, 0, values, numargs - 1, tuple.Length);
				return values;
			}
			else
			{
				DynValue[] values = new DynValue[numargs];

				for (int idx = 0; idx < numargs; idx++)
					values[idx] = m_ValueStack[first + idx].ToDynValue();

				return values;
			}
		}


		private void ExecArgs(Instruction I)
		{
			// The arguments sit right below the frame's base pointer; the count is in the frame.
			ref CallStackItem frame = ref m_ExecutionStack.Peek();
			int numargs = frame.ArgCount;
			int basePointer = frame.BasePointer;
			int argsBase = basePointer - numargs;
			SymbolRef[] symbols = I.SymbolList;

			bool lastArgExpands = numargs > 0 && m_ValueStack[argsBase + numargs - 1].Type == DataType.Tuple
				&& m_ValueStack[argsBase + numargs - 1].TupleValue.Tuple.Length > 1;
			bool hasVarArgs = symbols.Length > 0 && symbols[symbols.Length - 1].i_Name == WellKnownSymbols.VARARGS;

			if (!lastArgExpands && !hasVarArgs)
			{
				// Common case: copy the arguments, read in place, into the parameters' slots.
				for (int i = 0; i < symbols.Length; i++)
				{
					m_ValueStack[basePointer + symbols[i].i_Index] = (i < numargs)
						? ParameterValue(m_ValueStack[argsBase + i])
						: LuaValue.Nil;
				}

				return;
			}

			// unpacks last tuple arguments to simplify a lot of code down under
			List<LuaValue> argsList = new List<LuaValue>(numargs);

			for (int i = 0; i < numargs; i++)
			{
				LuaValue a = m_ValueStack[argsBase + i];

				if (i == numargs - 1 && lastArgExpands)
				{
					foreach (DynValue d in a.TupleValue.Tuple)
						argsList.Add(d.ToLuaValue());
				}
				else if (i == numargs - 1 && (a.Type == DataType.Void || a.Type == DataType.Tuple))
				{
					// A last argument that is a call returning no values (f(1, g())) adds none, or its one value.
					LuaValue v = a.ToScalar();
					if (v.Type != DataType.Void)
						argsList.Add(v);
				}
				else
				{
					argsList.Add(a);
				}
			}

			for (int i = 0; i < symbols.Length; i++)
			{
				bool isVarArgs = (i == symbols.Length - 1) && (symbols[i].i_Name == WellKnownSymbols.VARARGS);

				if (i >= argsList.Count)
				{
					// '...' with no values is an empty list, not one nil (select('#', ...) is 0).
					this.AssignLocal(symbols[i], isVarArgs ? DynValue.EmptyTuple.ToLuaValue() : LuaValue.Nil);
				}
				else if (isVarArgs)
				{
					int len = argsList.Count - i;
					DynValue[] varargs = new DynValue[len];

					for (int ii = 0; ii < len; ii++, i++)
					{
						varargs[ii] = argsList[i].ToScalar().ToDynValue();
					}

					DynValue[] adjusted = Internal_AdjustTuple(varargs);
					this.AssignLocal(symbols[symbols.Length - 1], (adjusted.Length == 0 ? DynValue.EmptyTuple : DynValue.NewTuple(adjusted)).ToLuaValue());
				}
				else
				{
					this.AssignLocal(symbols[i], ParameterValue(argsList[i]));
				}
			}
		}

		/// <summary>
		/// The value a parameter takes from an argument: the first value of a tuple, and nil for no value
		/// (an argument f() where f returns nothing), since a parameter is a variable.
		/// </summary>
		private static LuaValue ParameterValue(LuaValue argument)
		{
			LuaValue v = argument.ToScalar();
			return v.Type == DataType.Void ? LuaValue.Nil : v;
		}




		/// <summary>
		/// Calls the function below the top <paramref name="argsCount"/> values. A Lua function gets a frame
		/// and the returned address is its entry point; a C# function runs here. <paramref name="metamethod"/>
		/// counts the call against the nesting limit and adjusts its result to one value;
		/// <paramref name="returnsToPending"/> says that the frame below is a pending C# frame that takes the
		/// result (see <see cref="ClrCallWithHandlersReturned"/>).
		/// </summary>
		private int Internal_ExecCall(int argsCount, int instructionPtr, CallbackFunction handler = null,
			CallbackFunction continuation = null, bool thisCall = false, string debugText = null, DynValue unwindHandler = null,
			bool metamethod = false, bool returnsToPending = false)
		{
			LuaValue fn = m_ValueStack.Peek(argsCount);
			CallStackItemFlags flags = (thisCall ? CallStackItemFlags.MethodCall : CallStackItemFlags.None);

			// Lua raises "C stack overflow" when metamethod calls nest LUAI_MAXCCALLS (200) deep: an __index
			// function that indexes its own table recursed until then, here it ran without end, since the
			// tail call optimisation below kept the stacks from growing.
			int cCalls = m_ExecutionStack.Count > 0 ? m_ExecutionStack.Peek().CCalls : 0;

			if (metamethod && ++cCalls >= MAX_NATIVE_NESTING)
				throw new ScriptRuntimeException("C stack overflow");

			// if TCO threshold reached
			if ((m_ExecutionStack.Count > this.m_Script.Options.TailCallOptimizationThreshold && m_ExecutionStack.Count > 1)
				|| (m_ValueStack.Count > this.m_Script.Options.TailCallOptimizationThreshold && m_ValueStack.Count > 1))
			{
				// and the "will-be" return address is valid (we don't want to crash here)
				if (instructionPtr >= 0 && instructionPtr < this.m_RootChunk.Code.Count)
				{
					Instruction I = this.m_RootChunk.Code[instructionPtr];

					// and we are followed *exactly* by a RET 1
					if (I.OpCode == OpCode.Ret && I.NumVal == 1)
					{
						ref CallStackItem csi = ref m_ExecutionStack.Peek();

						// if the current stack item has no "odd" things pending and neither has the new coming one..
						if (csi.ClrFunction == null && csi.Handlers == null
							&& continuation == null && unwindHandler == null && handler == null)
						{
							instructionPtr = PerformTCO(instructionPtr, argsCount);
							flags |= CallStackItemFlags.TailCall;
						}
					}
				}
			}



			if (fn.Type == DataType.ClrFunction)
			{
				CallbackFunction callback = (CallbackFunction)fn.Object;
				CallStackItemHandlers handlers = CallStackItemHandlers.Create(continuation, handler, unwindHandler, metamethod, returnsToPending);

				if (callback.BuiltinId != 0 && handlers == null && TryFastCall(callback.BuiltinId, argsCount))
					return instructionPtr;

				DynValue[] args = callback.ReadsArgumentsInPlace ? null : CreateArgsListForFunctionCall(argsCount);

				// instructionPtr - 1: instructionPtr already points to the next instruction at this moment
				// but we need the current instruction here
				SourceRef sref = GetCurrentSourceRef(instructionPtr - 1);

				int depth = m_ExecutionStack.Count;
				ref CallStackItem frame = ref m_ExecutionStack.Push();
				frame.ClrFunction = callback;
				frame.ReturnAddress = instructionPtr;
				frame.CallingSourceRef = sref;
				frame.BasePointer = -1;
				frame.ArgCount = argsCount;
				frame.Handlers = handlers;
				frame.Flags = flags;
				frame.CCalls = cCalls;

				ScriptExecutionContext ctx = CallContext(depth, callback, sref);
				var ret = args == null
					? callback.InvokeOnStack(ctx, CallArguments(depth), m_ValueStack, m_ValueStack.Count - argsCount, argsCount, thisCall)
					: callback.Invoke(ctx, args, isMethodCall: thisCall);
				m_ValueStack.RemoveLast(argsCount + 1);
				m_ValueStack.Push(ret);

				if (handlers != null)
					return ClrCallWithHandlersReturned(instructionPtr);

				m_ExecutionStack.RemoveLast();

				return Internal_CheckForTailRequests(null, instructionPtr);
			}
			else if (fn.Type == DataType.Function)
			{
				Closure closure = (Closure)fn.Object;
				ref CallStackItem frame = ref m_ExecutionStack.Push();
				frame.BasePointer = m_ValueStack.Count;
				frame.ArgCount = argsCount;
				frame.ReturnAddress = instructionPtr;
				frame.Debug_EntryPoint = closure.EntryPointByteCodeLocation;
				frame.CallingSourceRef = GetCurrentSourceRef(instructionPtr - 1); // See right above in GetCurrentSourceRef(instructionPtr - 1)
				frame.Function = closure;
				frame.Upvalues = closure.Upvalues;
				frame.Handlers = CallStackItemHandlers.Create(continuation, handler, unwindHandler, metamethod, returnsToPending);
				frame.Flags = flags;
				frame.CCalls = cCalls;
				return closure.EntryPointByteCodeLocation;
			}

			// fallback to __call metamethod
			var m = GetMetamethod(fn, "__call", MetaCall);

			if (m != null && m.IsNotNil())
			{
				LuaValue[] tmp = new LuaValue[argsCount + 1];
				for (int i = 0; i < argsCount + 1; i++)
					tmp[i] = m_ValueStack.Pop();

				m_ValueStack.Push(m);

				for (int i = argsCount; i >= 0; i--)
					m_ValueStack.Push(tmp[i]);

				return Internal_ExecCall(argsCount + 1, instructionPtr, handler, continuation, false, null, unwindHandler, metamethod, returnsToPending);
			}

			throw ScriptRuntimeException.AttemptToCallNonFunc(fn.Type, debugText);
		}

		/// <summary>
		/// A C# function called with handlers (a continuation, pcall's error handlers, a metamethod's
		/// adjustment, or a pending frame below) returned; its frame is on top and its result on the value
		/// stack. A plain result goes through the handlers now. A tail call or a yield leaves the frame on
		/// the stack as a pending frame, without its function and arguments: the call its result asks for
		/// returns into it (<see cref="ReturnToPendingFrames"/>), and the frame's error handler catches
		/// errors raised before that, as a Lua frame's does.
		/// </summary>
		private int ClrCallWithHandlersReturned(int instructionPtr)
		{
			DataType type = m_ValueStack.Peek().Type;

			if (type == DataType.TailCallRequest || type == DataType.YieldRequest)
			{
				m_ExecutionStack.Peek().ArgCount = -1;
				return Internal_CheckForTailRequests(null, instructionPtr, true);
			}

			ReturnToPendingFrames();
			return instructionPtr;
		}

		/// <summary>
		/// Pops the top frame, whose call has returned the value on top of the value stack, and passes that
		/// value through its handlers; then the same for the pending frame below, while the popped frame
		/// returns into one.
		/// </summary>
		private void ReturnToPendingFrames()
		{
			CallStackItemHandlers handlers;

			do
			{
				CallStackItem csi = m_ExecutionStack.Pop();
				handlers = csi.Handlers;
				ApplyReturnHandlers(handlers, csi.CallingSourceRef);
			}
			while (handlers.ReturnsToPending);
		}

		/// <summary>
		/// Passes the result on top of the value stack through the handlers of the frame that returned it:
		/// its continuation, and the one-value adjustment of a metamethod call.
		/// </summary>
		private void ApplyReturnHandlers(CallStackItemHandlers handlers, SourceRef sourceRef)
		{
			if (handlers.Continuation != null)
				m_ValueStack.Push(handlers.Continuation.Invoke(new ScriptExecutionContext(this, handlers.Continuation, sourceRef),
					new DynValue[1] { m_ValueStack.Pop().ToDynValue() }));

			if (handlers.Metamethod)
			{
				ref LuaValue top = ref m_ValueStack.Peek();
				top = ParameterValue(top);
			}
		}

		private int PerformTCO(int instructionPtr, int argsCount)
		{
			LuaValue[] args = new LuaValue[argsCount + 1];

			// Remove all cur args and func ptr
			for (int i = 0; i <= argsCount; i++)
				args[i] = m_ValueStack.Pop();

			// perform a fake RET
			CallStackItem csi = PopToBasePointer();
			int retpoint = csi.ReturnAddress;
			m_ValueStack.RemoveLast(csi.ArgCount + 1);

			// Re-push all cur args and func ptr
			for (int i = argsCount; i >= 0; i--)
				m_ValueStack.Push(args[i]);

			return retpoint;
		}




		private int ExecRet(Instruction i)
		{
			CallStackItemHandlers handlers;
			int retpoint = 0;

			if (i.NumVal == 0)
			{
				retpoint = PopScriptFrame(out handlers);
				m_ValueStack.Push(LuaValue.Void);
			}
			else if (i.NumVal == 1)
			{
				var retval = m_ValueStack.Pop();
				retpoint = PopScriptFrame(out handlers);
				m_ValueStack.Push(retval);
				retpoint = Internal_CheckForTailRequests(i, retpoint);
			}
			else
			{
				throw new InternalErrorException("RET supports only 0 and 1 ret val scenarios");
			}

			if (handlers != null)
			{
				ApplyReturnHandlers(handlers, i.SourceCodeRef);

				if (handlers.ReturnsToPending)
					ReturnToPendingFrames();
			}

			return retpoint;
		}



		private int Internal_CheckForTailRequests(Instruction i, int instructionPtr, bool returnsToPending = false)
		{
			ref LuaValue tail = ref m_ValueStack.Peek(0);

			if (tail.Type == DataType.TailCallRequest)
			{
				TailCallData tcd = tail.TupleValue.TailCallData;

				m_ValueStack.RemoveLast(); // discard tail call request

				m_ValueStack.Push(tcd.Function);

				for (int ii = 0; ii < tcd.Args.Length; ii++)
					m_ValueStack.Push(tcd.Args[ii]);

				return Internal_ExecCall(tcd.Args.Length, instructionPtr, tcd.ErrorHandler, tcd.Continuation, false, null, tcd.ErrorHandlerBeforeUnwind,
					false, returnsToPending);
			}
			else if (tail.Type == DataType.YieldRequest)
			{
				m_SavedInstructionPtr = instructionPtr;
				return YIELD_SPECIAL_TRAP;
			}


			return instructionPtr;
		}



		private int JumpBool(Instruction i, bool expectedValueForJump, int instructionPtr)
		{
			LuaValue op = m_ValueStack.Pop().ToScalar();

			if (op.CastToBool() == expectedValueForJump)
				return i.NumVal;

			return instructionPtr;
		}

		private int ExecShortCircuitingOperator(Instruction i, int instructionPtr)
		{
			bool expectedValToShortCircuit = i.OpCode == OpCode.JtOrPop;

			LuaValue op = m_ValueStack.Peek().ToScalar();

			if (op.CastToBool() == expectedValToShortCircuit)
			{
				return i.NumVal;
			}
			else
			{
				m_ValueStack.RemoveLast();
				return instructionPtr;
			}
		}


		private int ExecArithmetic(OpCode op, int instructionPtr)
		{
			LuaValue[] items = m_ValueStack.Items;
			int top = m_ValueStack.Count;
			ref LuaValue l = ref items[top - 2];
			ref LuaValue r = ref items[top - 1];
			double ln, rn;

			// Numbers first, in place: the result replaces the left operand, and the right operand's slot
			// holds no object, so it needs no clearing. Strings that convert to numbers next; then metamethods.
			if (l.Type == DataType.Number && r.Type == DataType.Number)
			{
				ln = l.Number;
				rn = r.Number;
			}
			else
			{
				LuaValue ls = l.ToScalar();
				LuaValue rs = r.ToScalar();
				double? lc = ls.CastToNumber();
				double? rc = rs.CastToNumber();

				if (!lc.HasValue || !rc.HasValue)
				{
					m_ValueStack.RemoveLast(2);
					int ip = Internal_InvokeBinaryMetaMethod(ls, rs, ArithmeticEvent(op), ArithmeticBit(op), instructionPtr);
					if (ip >= 0) return ip;
					else throw ScriptRuntimeException.ArithmeticOnNonNumber(ls.ToDynValue(), rs.ToDynValue());
				}

				ln = lc.Value;
				rn = rc.Value;
				r = LuaValue.Nil;
			}

			double result;

			switch (op)
			{
				case OpCode.Add: result = ln + rn; break;
				case OpCode.Sub: result = ln - rn; break;
				case OpCode.Mul: result = ln * rn; break;
				case OpCode.Div: result = ln / rn; break;
				case OpCode.Mod: result = Modulo(ln, rn); break;
				default: result = Math.Pow(ln, rn); break;
			}

			l = LuaValue.FromNumber(result);
			m_ValueStack.Count = top - 1;
			return instructionPtr;
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
		private static double FusedArithmetic(OpCode op, double a, double b)
		{
			switch (op)
			{
				case OpCode.AddF: return a + b;
				case OpCode.SubF: return a - b;
				case OpCode.MulF: return a * b;
				case OpCode.DivF: return a / b;
				case OpCode.ModF: return Modulo(a, b);
				default: return Math.Pow(a, b);
			}
		}

		private static OpCode UnfusedArithmetic(OpCode op)
		{
			switch (op)
			{
				case OpCode.AddF: return OpCode.Add;
				case OpCode.SubF: return OpCode.Sub;
				case OpCode.MulF: return OpCode.Mul;
				case OpCode.DivF: return OpCode.Div;
				case OpCode.ModF: return OpCode.Mod;
				default: return OpCode.Power;
			}
		}

		private static string ArithmeticEvent(OpCode op)
		{
			switch (op)
			{
				case OpCode.Add: return "__add";
				case OpCode.Sub: return "__sub";
				case OpCode.Mul: return "__mul";
				case OpCode.Div: return "__div";
				case OpCode.Mod: return "__mod";
				default: return "__pow";
			}
		}

		private static int ArithmeticBit(OpCode op)
		{
			switch (op)
			{
				case OpCode.Add: return MetaAdd;
				case OpCode.Sub: return MetaSub;
				case OpCode.Mul: return MetaMul;
				case OpCode.Div: return MetaDiv;
				case OpCode.Mod: return MetaMod;
				default: return MetaPow;
			}
		}

		/// <summary>
		/// Lua 5.2 modulo (manual 3.4.1): a - floor(a/b)*b, so the result has the sign of the divisor.
		/// </summary>
		internal static double Modulo(double a, double b)
		{
			return a - Math.Floor(a / b) * b;
		}

		private int ExecNeg(Instruction i, int instructionPtr)
		{
			ref LuaValue top = ref m_ValueStack.Peek();

			if (top.Type == DataType.Number)
			{
				top.Number = -top.Number;
				return instructionPtr;
			}

			LuaValue r = m_ValueStack.Pop().ToScalar();
			double? rn = r.CastToNumber();

			if (rn.HasValue)
			{
				m_ValueStack.Push(LuaValue.FromNumber(-rn.Value));
				return instructionPtr;
			}
			else
			{
				int ip = Internal_InvokeUnaryMetaMethod(r, "__unm", MetaUnm, instructionPtr);
				if (ip >= 0) return ip;
				else throw ScriptRuntimeException.ArithmeticOnNonNumber(r.ToDynValue());
			}
		}


		private int ExecEq(Instruction i, int instructionPtr)
		{
			LuaValue r = m_ValueStack.Pop().ToScalar();
			LuaValue l = m_ValueStack.Pop().ToScalar();

			// Same type, not a table or userdata: no metamethod can apply.
			if (r.Type == l.Type && r.Type != DataType.Table && r.Type != DataType.UserData)
			{
				m_ValueStack.Push(LuaValue.FromBoolean(LuaValue.RawEquals(ref l, ref r)));
				return instructionPtr;
			}

			// first we do a brute force equals over the references
			if (r.Type == l.Type && ReferenceEquals(r.Object, l.Object))
			{
				m_ValueStack.Push(LuaValue.True);
				return instructionPtr;
			}

			// then if they are userdatas, attempt meta
			if (l.Type == DataType.UserData || r.Type == DataType.UserData)
			{
				int ip = Internal_InvokeBinaryMetaMethod(l, r, "__eq", MetaEq, instructionPtr);
				if (ip >= 0) return ip;
			}

			// then if types are different, ret false
			if (r.Type != l.Type)
			{
				if ((l.Type == DataType.Nil && r.Type == DataType.Void) || (l.Type == DataType.Void && r.Type == DataType.Nil))
					m_ValueStack.Push(LuaValue.True);
				else
					m_ValueStack.Push(LuaValue.False);

				return instructionPtr;
			}

			// then attempt metatables for tables
			if ((l.Type == DataType.Table) && (GetMetatable(l) != null) && (GetMetatable(l) == GetMetatable(r)))
			{
				int ip = Internal_InvokeBinaryMetaMethod(l, r, "__eq", MetaEq, instructionPtr);
				if (ip >= 0) return ip;
			}

			// else perform standard comparison
			m_ValueStack.Push(LuaValue.FromBoolean(LuaValue.RawEquals(ref r, ref l)));
			return instructionPtr;
		}

		private int ExecLess(Instruction i, int instructionPtr)
		{
			LuaValue[] items = m_ValueStack.Items;
			int top = m_ValueStack.Count;
			ref LuaValue l = ref items[top - 2];
			ref LuaValue r = ref items[top - 1];

			if (l.Type == DataType.Number && r.Type == DataType.Number)
			{
				l = LuaValue.FromBoolean(l.Number < r.Number);
				m_ValueStack.Count = top - 1;
				return instructionPtr;
			}

			LuaValue rs = m_ValueStack.Pop().ToScalar();
			LuaValue ls = m_ValueStack.Pop().ToScalar();

			if (ls.Type == DataType.Number && rs.Type == DataType.Number)
			{
				m_ValueStack.Push(LuaValue.FromBoolean(ls.Number < rs.Number));
			}
			else if (ls.Type == DataType.String && rs.Type == DataType.String)
			{
				m_ValueStack.Push(LuaValue.FromBoolean(string.CompareOrdinal((string)ls.Object, (string)rs.Object) < 0));
			}
			else
			{
				int ip = Internal_InvokeBinaryMetaMethod(ls, rs, "__lt", MetaLt, instructionPtr);
				if (ip < 0)
					throw ScriptRuntimeException.CompareInvalidType(ls.ToDynValue(), rs.ToDynValue());
				else
					return ip;
			}

			return instructionPtr;
		}

		private int ExecLessEq(Instruction i, int instructionPtr)
		{
			LuaValue r = m_ValueStack.Pop().ToScalar();
			LuaValue l = m_ValueStack.Pop().ToScalar();

			if (l.Type == DataType.Number && r.Type == DataType.Number)
			{
				m_ValueStack.Push(LuaValue.False);
				m_ValueStack.Push(LuaValue.FromBoolean(l.Number <= r.Number));
			}
			else if (l.Type == DataType.String && r.Type == DataType.String)
			{
				m_ValueStack.Push(LuaValue.False);
				m_ValueStack.Push(LuaValue.FromBoolean(string.CompareOrdinal((string)l.Object, (string)r.Object) <= 0));
			}
			else
			{
				int ip = Internal_InvokeBinaryMetaMethod(l, r, "__le", MetaLe, instructionPtr, LuaValue.False);
				if (ip < 0)
				{
					ip = Internal_InvokeBinaryMetaMethod(r, l, "__lt", MetaLt, instructionPtr, LuaValue.True);

					if (ip < 0)
						throw ScriptRuntimeException.CompareInvalidType(l.ToDynValue(), r.ToDynValue());
					else
						return ip;
				}
				else
					return ip;
			}

			return instructionPtr;
		}

		private int ExecLen(Instruction i, int instructionPtr)
		{
			LuaValue r = m_ValueStack.Pop().ToScalar();

			if (r.Type == DataType.String)
				m_ValueStack.Push(LuaValue.FromNumber(((string)r.Object).Length));
			else
			{
				int ip = Internal_InvokeUnaryMetaMethod(r, "__len", MetaLen, instructionPtr);
				if (ip >= 0)
					return ip;
				else if (r.Type == DataType.Table)
					m_ValueStack.Push(LuaValue.FromNumber(((Table)r.Object).Length));

				else throw ScriptRuntimeException.LenOnInvalidType(r.ToDynValue());
			}

			return instructionPtr;
		}


		private int ExecConcat(Instruction i, int instructionPtr)
		{
			LuaValue r = m_ValueStack.Pop().ToScalar();
			LuaValue l = m_ValueStack.Pop().ToScalar();

			string rs = r.CastToString();
			string ls = l.CastToString();

			if (rs != null && ls != null)
			{
				m_ValueStack.Push(LuaValue.FromString(ls + rs));
				return instructionPtr;
			}
			else
			{
				int ip = Internal_InvokeBinaryMetaMethod(l, r, "__concat", MetaConcat, instructionPtr);
				if (ip >= 0) return ip;
				else throw ScriptRuntimeException.ConcatOnNonString(l.ToDynValue(), r.ToDynValue());
			}

		}


		private void ExecTblInitI(Instruction i)
		{
			// stack: tbl - val
			LuaValue val = m_ValueStack.Pop();
			LuaValue tbl = m_ValueStack.Peek();

			if (tbl.Type != DataType.Table)
				throw new InternalErrorException("Unexpected type in table ctor : {0}", tbl);

			((Table)tbl.Object).InitNextArrayKeys(val.ToDynValue(), i.NumVal != 0);
		}

		private void ExecTblInitN(Instruction i)
		{
			// stack: tbl - key - val
			LuaValue val = m_ValueStack.Pop();
			LuaValue key = m_ValueStack.Pop();
			LuaValue tbl = m_ValueStack.Peek();

			if (tbl.Type != DataType.Table)
				throw new InternalErrorException("Unexpected type in table ctor : {0}", tbl);

			TableSet((Table)tbl.Object, ref key, val.ToScalar().ToDynValue());
		}

		/// <summary>
		/// t[key] without metamethods, without creating a DynValue key for string and integer keys.
		/// </summary>
		private static DynValue TableGet(Table t, ref LuaValue key)
		{
			if (key.Type == DataType.String)
				return t.Get((string)key.Object);

			if (key.Type == DataType.Number)
			{
				// Table's own rule for keys that go to its integer map.
				double d = key.Number;
				int idx = (int)d;

				if (d >= 1.0 && d == idx)
					return t.Get(idx);
			}

			return t.Get(key.ToDynValue());
		}

		/// <summary>
		/// t[key] = value without metamethods, without creating a DynValue key for string and integer keys.
		/// </summary>
		private static void TableSet(Table t, ref LuaValue key, DynValue value)
		{
			if (key.Type == DataType.String)
			{
				t.Set((string)key.Object, value);
				return;
			}

			if (key.Type == DataType.Number)
			{
				double d = key.Number;
				int idx = (int)d;

				if (d >= 1.0 && d == idx)
				{
					t.Set(idx, value);
					return;
				}
			}

			t.Set(key.ToDynValue(), value);
		}

		private int ExecIndexSet(Instruction i, int instructionPtr)
		{
			int nestedMetaOps = 100; // sanity check, to avoid potential infinite loop here

			// stack: vals.. - base - index
			bool isNameIndex = i.OpCode == OpCode.IndexSetN;
			bool isMultiIndex = (i.OpCode == OpCode.IndexSetL);

			LuaValue originalIdx = i.Value != null ? i.Value.ToLuaValue() : m_ValueStack.Pop();
			LuaValue idx = originalIdx.ToScalar();
			LuaValue obj = m_ValueStack.Pop().ToScalar();
			LuaValue value = GetStoreValue(i);
			DynValue h = null;

			// A constant string key stored into a table: write the cached entry in place, or find it and
			// cache it. A new key goes straight in when there is no metatable, else the general path runs.
			if (obj.Type == DataType.Table && idx.Type == DataType.String && i.Value != null && !isMultiIndex && value.IsNotNil())
			{
				Table table = (Table)obj.Object;
				FieldCache cache = i.Cache ?? (i.Cache = new FieldCache((string)idx.Object));
				DynValue v = value.ToDynValue();

				if (cache.TrySet(table, v))
					return instructionPtr;

				int e = cache.FindLive(table);

				if (e >= 0)
				{
					table.SetEntryValue(e, v);
					return instructionPtr;
				}

				if (table.MetaTable == null)
				{
					cache.Add(table, v);
					return instructionPtr;
				}
			}

			while (nestedMetaOps > 0)
			{
				--nestedMetaOps;

				if (obj.Type == DataType.Table)
				{
					Table table = (Table)obj.Object;

					if (!isMultiIndex)
					{
						// Without a metatable there is no __newindex to find, so skip the lookup of the old value.
						if (table.MetaTable == null || !TableGet(table, ref idx).IsNil())
						{
							TableSet(table, ref idx, value.ToDynValue());
							return instructionPtr;
						}
					}

					h = GetMetamethodRaw(obj, "__newindex", MetaNewIndex);

					if (h == null || h.IsNil())
					{
						if (isMultiIndex) throw new ScriptRuntimeException("cannot multi-index a table. userdata expected");

						TableSet(table, ref idx, value.ToDynValue());
						return instructionPtr;
					}
				}
				else if (obj.Type == DataType.UserData)
				{
					UserData ud = (UserData)obj.Object;

					if (!ud.Descriptor.SetIndex(this.GetScript(), ud.Object, i.Value ?? originalIdx.ToDynValue(), value.ToDynValue(), isNameIndex))
					{
						throw ScriptRuntimeException.UserDataMissingField(ud.Descriptor.Name, idx.String);
					}

					return instructionPtr;
				}
				else
				{
					h = GetMetamethodRaw(obj, "__newindex", MetaNewIndex);

					if (h == null || h.IsNil())
						throw ScriptRuntimeException.IndexType(obj.ToDynValue());
				}

				if (h.Type == DataType.Function || h.Type == DataType.ClrFunction)
				{
					if (isMultiIndex) throw new ScriptRuntimeException("cannot multi-index through metamethods. userdata expected");
					m_ValueStack.RemoveLast(); // burn extra value ?

					m_ValueStack.Push(h);
					m_ValueStack.Push(obj);
					m_ValueStack.Push(idx);
					m_ValueStack.Push(value);
					return Internal_ExecCall(3, instructionPtr, metamethod: true);
				}
				else
				{
					obj = h.ToLuaValue();
					h = null;
				}
			}
			throw ScriptRuntimeException.LoopInNewIndex();
		}

		private int ExecIndex(Instruction i, int instructionPtr)
		{
			int nestedMetaOps = 100; // sanity check, to avoid potential infinite loop here

			// stack: base - index
			bool isNameIndex = i.OpCode == OpCode.IndexN;

			bool isMultiIndex = (i.OpCode == OpCode.IndexL);

			LuaValue originalIdx = i.Value != null ? i.Value.ToLuaValue() : m_ValueStack.Pop();
			LuaValue idx = originalIdx.ToScalar();
			LuaValue obj = m_ValueStack.Pop().ToScalar();

			DynValue h = null;

			// A constant string key read from a table: the cached path, or a lookup through __index tables
			// that caches it.
			if (obj.Type == DataType.Table && idx.Type == DataType.String && i.Value != null && !isMultiIndex)
			{
				FieldCache cache = i.Cache ?? (i.Cache = new FieldCache((string)idx.Object));

				if (!cache.Disabled)
				{
					Table table = (Table)obj.Object;
					DynValue v = cache.Get(table);

					if (v == null)
					{
						v = cache.Lookup(table, out table);

						// an __index the cache does not follow: the general lookup goes on from there
						if (v == null)
							obj = LuaValue.FromObject(DataType.Table, table);
					}

					if (v != null)
					{
						m_ValueStack.Push(v);
						return instructionPtr;
					}
				}
			}

			while (nestedMetaOps > 0)
			{
				--nestedMetaOps;

				if (obj.Type == DataType.Table)
				{
					if (!isMultiIndex)
					{
						var v = TableGet((Table)obj.Object, ref idx);

						if (!v.IsNil())
						{
							m_ValueStack.Push(v);
							return instructionPtr;
						}
					}

					h = GetMetamethodRaw(obj, "__index", MetaIndex);

					if (h == null || h.IsNil())
					{
						if (isMultiIndex) throw new ScriptRuntimeException("cannot multi-index a table. userdata expected");

						m_ValueStack.Push(LuaValue.Nil);
						return instructionPtr;
					}
				}
				else if (obj.Type == DataType.UserData)
				{
					UserData ud = (UserData)obj.Object;
					DynValue index = i.Value ?? originalIdx.ToDynValue();

					var dispatching = ud.Descriptor as Interop.BasicDescriptors.DispatchingUserDataDescriptor;
					var v = (dispatching != null)
						? dispatching.IndexUserData(this.GetScript(), ud, index, isNameIndex)
						: ud.Descriptor.Index(this.GetScript(), ud.Object, index, isNameIndex);

					if (v == null)
					{
						throw ScriptRuntimeException.UserDataMissingField(ud.Descriptor.Name, idx.String);
					}

					m_ValueStack.Push(v);
					return instructionPtr;
				}
				else
				{
					h = GetMetamethodRaw(obj, "__index", MetaIndex);

					if (h == null || h.IsNil())
						throw ScriptRuntimeException.IndexType(obj.ToDynValue());
				}

				if (h.Type == DataType.Function || h.Type == DataType.ClrFunction)
				{
					if (isMultiIndex) throw new ScriptRuntimeException("cannot multi-index through metamethods. userdata expected");
					m_ValueStack.Push(h);
					m_ValueStack.Push(obj);
					m_ValueStack.Push(idx);
					return Internal_ExecCall(2, instructionPtr, metamethod: true);
				}
				else
				{
					obj = h.ToLuaValue();
					h = null;
				}
			}

			throw ScriptRuntimeException.LoopInIndex();
		}
	}
}
