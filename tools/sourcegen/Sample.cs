using MoonSharp.Interpreter;

[assembly: MoonSharpUserDataTypes(typeof(Sample.ThirdPartyVector))]

namespace Sample
{
	[MoonSharpUserData]
	public class Counter
	{
		public int Value { get; set; }
		public int Add(int a, int b = 1) { Value += a + b; return Value; }
		public static string Hello(string who) { return "hi " + who; }
		public void Each(int n, System.Action<int> f) { for (int i = 0; i < n; i++) f(i); }
	}

	[MoonSharpUserData]
	public partial struct Owned
	{
		public int X;
		public void Bump() { X++; }
	}

	public struct ThirdPartyVector
	{
		public float x, y;
		public ThirdPartyVector(float x, float y) { this.x = x; this.y = y; }
		public static ThirdPartyVector operator +(ThirdPartyVector a, ThirdPartyVector b) { return new ThirdPartyVector(a.x + b.x, a.y + b.y); }
	}

	public static class Entry
	{
		public static string Run()
		{
			MoonSharp.Generated.GeneratedUserData.RegisterAll();
			Script s = new Script();
			s.Globals["c"] = new Counter();
			s.Globals["o"] = new Owned();
			s.Globals["V"] = typeof(ThirdPartyVector);
			s.Globals["C"] = typeof(Counter);
			return s.DoString("c:Each(2, function(i) c:Add(i, 0) end) c:Add(2) o:Bump() o:Bump() local v = V.__new(1, 2) + V.__new(3, 4) return c.Value .. ',' .. o.X .. ',' .. v.x .. ',' .. C.Hello('lua')").String;
		}
	}
}
