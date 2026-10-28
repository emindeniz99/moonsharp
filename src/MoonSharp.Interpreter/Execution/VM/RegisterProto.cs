using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	// SPIKE (branch spike/register-vm, not for merging): a register instruction set for a subset of Lua
	// (numbers, locals, upvalues, globals, field and index access, calls with fixed arguments, if, while,
	// repeat, numeric for). A function the RegisterCompiler accepts gets a RegisterProto next to its stack
	// code and runs in Processor_Registers.cs; everything else stays on the stack VM. See
	// docs/research/register-vm-spike.md.
	//
	// Operands: R(x) is the register x of the running frame (value stack slot base + x). An RK operand
	// is a register when x >= 0 and the constant K[~x] when x < 0. Jump targets are register code
	// indexes. D is the stack-code address of the instruction's resume trampoline: the slow path (a
	// metamethod, coercion, a call that is not a register function) runs the stack VM's own helper with
	// that address as its return address, and the trampoline brings the result back here. CallT's D is a
	// stack Ret 1 instead.
	internal enum ROp : byte
	{
		Move,       // R(A) = R(B)
		LoadK,      // R(A) = K[B]
		GetUpval,   // R(A) = upvalue B
		SetUpval,   // upvalue B = R(A)
		GetTabUp,   // R(A) = upvalue B [Aux[C].Value]           (a global read through the _ENV upvalue)
		SetTabUp,   // upvalue A [Aux[B].Value] = R(C)
		GetField,   // R(A) = R(B) [Aux[C].Value]                (constant string key, inline cache in Aux[C])
		SetField,   // R(A) [Aux[B].Value] = R(C)
		GetTable,   // R(A) = R(B) [RK(C)]
		SetTable,   // R(A) [RK(B)] = R(C)

		AddRR, SubRR, MulRR, DivRR, ModRR, PowRR,   // R(A) = R(B) op R(C)
		AddRK, SubRK, MulRK, DivRK, ModRK, PowRK,   // R(A) = R(B) op K[C]
		AddKR, SubKR, MulKR, DivKR, ModKR, PowKR,   // R(A) = K[B] op R(C)
		Unm,        // R(A) = -R(B)
		Not,        // R(A) = not R(B)

		Jmp,        // pc = A
		JmpIf,      // if R(A) then pc = B
		JmpIfNot,   // if not R(A) then pc = B
		LtJf, LtJt, // if (RK(A) < RK(B)) is false (Jf) / true (Jt) then pc = C
		LeJf, LeJt,
		EqJf, EqJt,

		ForNum,     // R(A) = tonumber(R(A)), or the 'for' error of stage B
		ForPrep,    // R(A), R(A+1), R(A+2) = counter, limit, step: if the loop runs, R(B) = counter, else pc = C
		ForLoop,    // counter += step; while in range, R(B) = counter and pc = C

		CallS,      // R(A) = R(A)(R(A+1) .. R(A+B)), one value; Aux[C].Name names the function in errors
		CallD,      // R(A)(R(A+1) .. R(A+B)), results discarded
		CallT,      // return R(A)(R(A+1) .. R(A+B)); D is a stack Ret 1, so a Lua callee returns through it

		Ret0,       // return
		Ret1,       // return R(A)
	}

	internal struct RInstr
	{
		public ROp Op;
		public int A, B, C, D;
	}

	internal sealed class RegisterProto
	{
		public RInstr[] Code;
		public LuaValue[] K;
		// Stack instructions the slow paths hand to the stack VM's helpers (ExecIndex, ExecIndexSet): one
		// per field access, holding the key and its inline cache, and per call, holding its debug name.
		public Instruction[] Aux;
		public SourceRef[] Refs;
		public int NumParams;
		// The register of each local by symbol index, and the symbols (BeginFn's list), for lookups by name.
		public int[] RegOf;
		public SymbolRef[] Symbols;
		// Registers: the function's locals (parameters first), then temporaries.
		public int FrameSize;
		// The local that holds _ENV in a chunk, set from upvalue 0 on entry; -1 in a function.
		public int EnvReg = -1;
		// For the slow paths of GetTable and SetTable (key on the stack).
		public Instruction IndexAux, IndexSetAux;
	}
}
