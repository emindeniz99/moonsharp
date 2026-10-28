-- Uses the sample API (Sample.cs) only as its definitions describe it. The tests
-- run it on the sample script, and lua-language-server must find nothing in it.

local p = Player.__new("ann", 50)
p:Damage(5)
assert(p:Heal(10, true))
p:OnTick(function(dt) end)
assert(p.Health == 55)
p.Shield = nil
p.Shield = 3
p.Team = Team.Blue
p.Abilities = Abilities.flagsOr(Abilities.Jump, Abilities.Swim)
assert(Abilities.hasAll(p.Abilities, Abilities.Jump))
p.Mood = 1
p:SetMood(0)
assert(p:Greet("bob") == "hello bob")
assert(p:Greet(hero) == "hello hero")

local item = Item.__new()
item.Name = "key"
item.Quantity = 2
p.Inventory:Add(item)
assert(#p.Inventory == 1)
assert(p.Inventory[0].Name == "key")
for it in p.Inventory:All() do
	assert(it.Quantity == 2)
end

local found, got = p:TryGetItem("key")
assert(found and got.Quantity == 2)
local _, clamped = p:Clamp(150)
assert(clamped == 100)
assert(p:Format("{0}-{1}", 1, "a") == "1-a")

-- Lists and dictionaries cross as tables, in both directions.
p:SetTags({ "a", "b" })
assert(p.Tags[1] == "a")
p:AddStats({ hp = 1.5 })
assert(p.Stats.hp == 1.5)

-- long parameters take numbers and int64; long values come back as int64.
p:AddScore(int64("9007199254740993"))
p:AddScore(1)
assert(int64.tostring(p.Score) == "9007199254740994")
assert(int64.is(p.Id))

assert(p.Scaler(2) == 4)
local hits = 0
p.Damaged.add(function(sender, e) hits = hits + e.Amount end)
p:Damage(2)
assert(hits == 2)
p:Hit(hero)
assert(p:CountArgs(1, 2, 3) == 3)
assert(p:Echo("x") == "x")
assert(p:IsAlive())

local v = Vector2.__new(3, 4)
assert(v.Magnitude == 5)
local w = -(v + v) * 2
assert(w.X == -12)

assert(Player.Create("cat").Name == "cat")
assert(Player.Created >= 1)
assert(Player.MaxLevel == 99)
assert(Player.PeekNextId() > p.Id)
local badge = Player.Badge.__new()
badge.Title = "first"

game.log("done " .. game.version)
local data = json.parse('{"a": [1, 2]}')
assert(json.serialize(data) ~= "")
assert(os.time() > 0)
assert(string.contains("abc", "b"))
assert(pcall(error, "caught") == false)
