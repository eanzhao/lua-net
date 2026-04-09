local mt = {
  __call = function(self, a, b)
    return self.base + a + b
  end
}

local x = setmetatable({ base = 40 }, mt)

local function f(v)
  return x(v, 2)
end

return f(1)
