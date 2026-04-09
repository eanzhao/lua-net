local mt = {
  __concat = function(a, b)
    return a.left .. b.right
  end
}

local x = setmetatable({ left = "lua" }, mt)
local y = setmetatable({ right = "-net" }, mt)

return x .. y
