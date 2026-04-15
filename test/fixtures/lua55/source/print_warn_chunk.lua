warn("@on")
print("head", 42)
warn("a", "b", "c")
warn("@off")
warn("hidden")

local obj = setmetatable({}, {
  __tostring = function()
    return "obj"
  end
})

print(obj, 3.0)

return 99
