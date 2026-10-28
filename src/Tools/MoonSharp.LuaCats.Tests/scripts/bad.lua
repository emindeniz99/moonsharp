-- Misuses the sample API. Each line marked "-- expect: <code>" must get exactly
-- that lua-language-server diagnostic, and no other line any.

local p = Player.__new("ann")
p:Damage("lots") -- expect: param-type-mismatch
print(p.Healt) -- expect: undefined-field
p:Heal() -- expect: missing-parameter
p:Greet("a", "b") -- expect: redundant-parameter
p.Team = "blue" -- expect: assign-type-mismatch
p:SetTags("a") -- expect: param-type-mismatch
print(p.Password) -- expect: undefined-field
p:Internal() -- expect: undefined-field
p:OnTick(function() end) -- expect: param-type-mismatch
p:AddScore("1") -- expect: param-type-mismatch
print(hero.Inventory.Size) -- expect: undefined-field

-- The soft sandbox has no load, io or os.execute.
print(load("return 1")) -- expect: undefined-global
io.write("x") -- expect: undefined-global
os.execute("ls") -- expect: undefined-field
