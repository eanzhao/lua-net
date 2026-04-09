local mt = {
  __call = function(self, a, b)
    return self.base + a + b
  end
}

local x = setmetatable({ base = 40 }, mt)
local y = x(1, 2)

return y
