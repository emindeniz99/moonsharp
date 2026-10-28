# Registering only the types scripts may use

Mobile hosts expose only the C# types they choose (roadmap decision): a
script downloaded at run time must not reach other native APIs. This page
says what MoonSharp guarantees today, how a host locks registration to one
explicit list, and how the LuaCATS definitions follow the same list.

## What MoonSharp does by default

A script reaches C# only through userdata, and userdata exists only for
types that have a descriptor. With the default registration policy
(`InteropRegistrationPolicy.Default`):

- No type is registered on its own. `[MoonSharpUserData]` marks a type for
  `UserData.RegisterAssembly`; without that call it registers nothing.
- A registered member that returns an unregistered type fails with "cannot
  convert clr type", also when it is declared as `object`. Exceptions: lists,
  dictionaries and other enumerables become tables or iterators of their
  elements (each element must convert in turn), delegates become callable
  functions, and unregistered enums become numbers.
- `obj:GetType()` returns the registered type's static userdata, not a
  `System.Type` a script could reflect on.
- A type that is not registered but derives from a registered type, or
  implements a registered interface, is seen through that type's members
  only.

`InteropRegistrationPolicy.Automatic` registers every type a script meets.
Do not use it in a host that runs downloaded scripts.

`ExplicitRegistrationTests` in the unit tests checks each point above.

## Locking registration to a list

Registering a type also registers its public nested types, and any code in
the process can call `UserData.RegisterType` later. To make the list the only
source, install a policy that refuses everything else:

```csharp
using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;

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
```

and keep the list and the setup in one place:

```csharp
public static class ScriptApi
{
    // The only C# types scripts can reach.
    public static readonly Type[] Types = { typeof(Player), typeof(Inventory), typeof(Team) };

    // Classes whose extension methods scripts can call on the types above.
    public static readonly Type[] ExtensionTypes = { typeof(PlayerExtensions) };

    public static Script CreateScript()
    {
        UserData.RegistrationPolicy = new AllowListRegistrationPolicy(Types);

        foreach (Type t in Types)
            UserData.RegisterType(t);

        foreach (Type t in ExtensionTypes)
            UserData.RegisterExtensionType(t);

        var script = new Script(CoreModules.Preset_SoftSandbox);
        script.Globals["Player"] = typeof(Player);
        return script;
    }
}
```

Notes:

- The third clause allows MoonSharp's own helper types (event facades,
  iterators, JSON markers, the `dynamic` and `io` wrappers). Its modules
  register them when a script is created, and refusing them breaks those
  modules.
- Listing an open generic type such as `typeof(List<>)` allows every closed
  version; MoonSharp registers `List<int>` the first time one reaches a
  script.
- A public nested type is refused unless it is listed itself.
- Extension methods are not covered by the policy. `RegisterExtensionType`
  is a second explicit list; keep it next to the first.
- The policy is process-wide, like the registry. Set it before the first
  registration.

No runtime change was needed for this: `IRegistrationPolicy` already decides
every registration. The unit tests check this policy too.

## Keeping the definitions on the same list

The LuaCATS generator ([luacats.md](luacats.md)) calls the same
`CreateScript` and reads the registry and the script's globals afterwards,
so the definitions describe exactly the types the list let through, and
nothing else:

```sh
moonsharp-luacats Host.dll Game.Scripting.ScriptApi.CreateScript scripts/types --check
```

Run that in CI. A type added to the list without regenerating, or a member
whose type is missing from the list, then shows up as a stale file or as a
generator warning instead of a script that type-checks and fails on a
device. `src/Tools/MoonSharp.LuaCats.Tests/Sample.cs` is a complete example
of this setup.
