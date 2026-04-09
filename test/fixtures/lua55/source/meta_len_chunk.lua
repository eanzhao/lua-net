local mt = {
  __len = function(a)
    return a.base + 2
  end
}

local x = setmetatable({ base = 40 }, mt)

return #x
