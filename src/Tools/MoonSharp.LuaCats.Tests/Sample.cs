using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;

// A small host API that covers every mapping the generator makes. SampleApi is set up the way
// docs/registration.md recommends for mobile hosts: one explicit list, registration locked to it.
namespace Sample
{
	public static class SampleApi
	{
		/// <summary>The only C# types scripts can reach.</summary>
		public static readonly Type[] Types =
		{
			typeof(Abilities), typeof(DamageEventArgs), typeof(Entity), typeof(Inventory), typeof(Item),
			typeof(Player), typeof(Player.Badge), typeof(Team), typeof(Vector2),
		};

		/// <summary>Classes whose extension methods scripts can call on the types above.</summary>
		public static readonly Type[] ExtensionTypes = { typeof(PlayerExtensions) };

		/// <summary>Creates a script the way the host does. The LuaCATS generator calls this too.</summary>
		public static Script CreateScript()
		{
			UserData.RegistrationPolicy = new AllowListRegistrationPolicy(Types);

			foreach (Type t in Types)
				UserData.RegisterType(t);

			foreach (Type t in ExtensionTypes)
				UserData.RegisterExtensionType(t);

			var script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.Int64);
			script.Options.ExactInt64Interop = true;

			script.Globals["Player"] = typeof(Player);
			script.Globals["Vector2"] = typeof(Vector2);
			script.Globals["Item"] = typeof(Item);
			script.Globals["Team"] = typeof(Team);
			script.Globals["Abilities"] = typeof(Abilities);
			script.Globals["hero"] = new Player("hero");

			var game = new Table(script);
			game["version"] = "1.0";
			game["log"] = (Action<string>)(s => { });
			script.Globals["game"] = game;

			return script;
		}
	}

	/// <summary>
	/// Allows only the listed types (and closed versions of listed generic types) to be registered, plus
	/// MoonSharp's own helper types, which its modules register when a script is created.
	/// </summary>
	public sealed class AllowListRegistrationPolicy : IRegistrationPolicy
	{
		readonly HashSet<Type> m_Allowed;

		public AllowListRegistrationPolicy(IEnumerable<Type> allowed)
		{
			m_Allowed = new HashSet<Type>(allowed);
		}

		public IUserDataDescriptor HandleRegistration(IUserDataDescriptor newDescriptor, IUserDataDescriptor oldDescriptor)
		{
			if (newDescriptor == null || oldDescriptor != null)
				return oldDescriptor; // no unregistering, no replacing

			Type t = newDescriptor.Type;
			bool allowed = m_Allowed.Contains(t)
				|| (t.IsGenericType && m_Allowed.Contains(t.GetGenericTypeDefinition()))
				|| t.Assembly == typeof(Script).Assembly;

			return allowed ? newDescriptor : null;
		}

		public bool AllowTypeAutoRegistration(Type type)
		{
			return false;
		}
	}

	public enum Team { Red, Blue }

	[Flags]
	public enum Abilities { None = 0, Jump = 1, Swim = 2, Fly = 4 }

	/// <summary>Not registered: scripts see its values as numbers.</summary>
	public enum Mood { Calm, Angry }

	/// <summary>Not registered: scripts can pass a Player where one is expected.</summary>
	public interface IDamageable
	{
		void Damage(double amount);
	}

	/// <summary>Not registered: a member returning it is reported by the generator.</summary>
	public class Secret { }

	public struct Vector2
	{
		public double X;
		public double Y;

		public Vector2(double x, double y)
		{
			X = x;
			Y = y;
		}

		public double Magnitude { get { return Math.Sqrt(X * X + Y * Y); } }

		public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.X + b.X, a.Y + b.Y); }
		public static Vector2 operator *(Vector2 a, double k) { return new Vector2(a.X * k, a.Y * k); }
		public static Vector2 operator -(Vector2 a) { return new Vector2(-a.X, -a.Y); }
	}

	public class Entity
	{
		static long s_NextId = 9007199254740993; // above 2^53: exact only as int64

		public Entity()
		{
			Id = s_NextId++;
		}

		public string Name { get; set; }
		public long Id { get; private set; }
		public Vector2 Position;

		public virtual string Describe() { return Name; }
	}

	public class DamageEventArgs : EventArgs
	{
		public DamageEventArgs(double amount) { Amount = amount; }
		public double Amount { get; private set; }
	}

	public class Item
	{
		public string Name;
		public int Quantity;
	}

	public class Inventory
	{
		readonly List<Item> m_Items = new List<Item>();

		public int Count { get { return m_Items.Count; } }
		public Item this[int index] { get { return m_Items[index]; } }

		public void Add(Item item) { m_Items.Add(item); }
		public IEnumerable<Item> All() { return m_Items; }
	}

	public class Player : Entity, IDamageable
	{
		public const int MaxLevel = 99;

		public class Badge
		{
			public string Title;
		}

		public Player(string name) : this(name, 100) { }

		public Player(string name, double health)
		{
			Name = name;
			Health = health;
			Inventory = new Inventory();
			Tags = new List<string>();
			Stats = new Dictionary<string, double>();
			Scaler = x => x * 2;
		}

		public static int Created { get; private set; }

		public double Health { get; set; }
		public double? Shield { get; set; }
		public Team Team { get; set; }
		public Abilities Abilities { get; set; }
		public Mood Mood { get; set; }
		public Inventory Inventory { get; private set; }
		public List<string> Tags { get; private set; }
		public Dictionary<string, double> Stats { get; private set; }
		public Func<double, double> Scaler { get; set; }
		public long Score;

		[MoonSharpHidden]
		public string Password;

		[MoonSharpVisible(false)]
		public void Internal() { }

		public event EventHandler<DamageEventArgs> Damaged;

		public static Player Create(string name) { Created++; return new Player(name); }

		public void Damage(double amount)
		{
			Health -= amount;
			if (Damaged != null)
				Damaged(this, new DamageEventArgs(amount));
		}

		public bool Heal(double amount, bool overheal = false)
		{
			Health = overheal ? Health + amount : Math.Min(100, Health + amount);
			return true;
		}

		public string Greet(string name) { return "hello " + name; }
		public string Greet(Player other) { return "hello " + other.Name; }

		public bool TryGetItem(string name, out Item item)
		{
			item = Inventory.All().FirstOrDefault(i => i.Name == name);
			return item != null;
		}

		public void Clamp(ref double value) { value = Math.Max(0, Math.Min(100, value)); }

		public string Format(string format, params object[] args) { return string.Format(format, args); }

		public void SetTags(List<string> tags) { Tags = tags; }
		public void AddStats(Dictionary<string, double> stats) { foreach (var s in stats) Stats[s.Key] = s.Value; }
		public void AddScore(long points) { Score += points; }
		public void Hit(IDamageable target) { target.Damage(1); }
		public void SetMood(Mood mood) { Mood = mood; }
		public int CountArgs(CallbackArguments args) { return args.Count; }
		public DynValue Echo(DynValue value) { return value; }

		/// <summary>Scripts cannot pass a function for a delegate parameter; the generator reports it.</summary>
		public void OnTick(Action<double> callback) { }

		/// <summary>Secret is not registered; the generator reports it.</summary>
		public Secret GetSecret() { return new Secret(); }
	}

	public static class PlayerExtensions
	{
		public static bool IsAlive(this Player player) { return player.Health > 0; }
	}
}
