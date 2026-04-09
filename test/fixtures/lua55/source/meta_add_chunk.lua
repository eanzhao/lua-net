local mt = {
  __add = function(a, b)
    return a.base + b.base
  end
}

local x = setmetatable({ base = 40 }, mt)
local y = setmetatable({ base = 2 }, mt)

return x + y
