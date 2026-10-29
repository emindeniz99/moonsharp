// Upstream fbd0367 declares its own ReferenceEqualityComparer, which clashes with the BCL type on net5+.
global using ReferenceEqualityComparer = MoonSharp.Interpreter.DataStructs.ReferenceEqualityComparer;
