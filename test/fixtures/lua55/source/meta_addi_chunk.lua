local mt = {
  __add = function(a, b)
    return a.base + b
  end
}

local x = setmetatable({ base = 40 }, mt)

return x + 2
