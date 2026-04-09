local mt = {
  __bnot = function(a)
    return a.base ~ 7
  end
}

local x = setmetatable({ base = 2 }, mt)

return ~x
