local mt = {
  __add = function(a, b)
    return b.base - a
  end
}

local x = setmetatable({ base = 50 }, mt)

return 2 + x
